using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Consumer;

public class QueueProcessorWorker : BackgroundService
{
    private readonly IModel _channel;
    private readonly string _queueName;
    private readonly ILogger<QueueProcessorWorker> _logger;
    private static readonly ActivitySource ActivitySource = new("consumer-service");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public QueueProcessorWorker(IModel channel, ILogger<QueueProcessorWorker> logger)
    {
        _channel = channel;
        _queueName = Environment.GetEnvironmentVariable("QUEUE_NAME") ?? "otel-demo-queue";
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Consumer service started. Waiting for messages from RabbitMQ...");

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.Received += async (model, ea) =>
        {
            _logger.LogInformation("Message received from RabbitMQ queue");
            
            var body = ea.Body.ToArray();
            var messageText = Encoding.UTF8.GetString(body);
            var properties = ea.BasicProperties;

            // CRITICAL: Extract trace context from RabbitMQ message headers
            // This continues the trace from the producer service
            // The propagator extracts W3C Trace Context headers and creates a parent context
            var parentContext = Propagator.Extract(
                default,
                properties.Headers ?? new Dictionary<string, object>(),
                (headers, key) =>
                {
                    if (headers.TryGetValue(key, out var value))
                    {
                        // RabbitMQ headers can be byte arrays, convert to string
                        var stringValue = value is byte[] bytes
                            ? Encoding.UTF8.GetString(bytes)
                            : value?.ToString();
                        return string.IsNullOrEmpty(stringValue)
                            ? Array.Empty<string>()
                            : new[] { stringValue };
                    }
                    return Array.Empty<string>();
                }
            );
            
            _logger.LogInformation(
                "Extracted trace context - Valid: {IsValid}, TraceId: {TraceId}",
                parentContext.ActivityContext.IsValid(),
                parentContext.ActivityContext.IsValid() ? parentContext.ActivityContext.TraceId.ToString() : "N/A"
            );

            // Create a span for message processing
            // Following OpenTelemetry semantic conventions for messaging:
            // - Span name: {destination} receive
            // - Span kind: Consumer (indicates this service is consuming messages)
            // - Parent context: Links this span to the producer span via trace context
            var activityName = $"{_queueName} receive";
            
            // CRITICAL: Ensure we have a valid parent context or create a new root activity
            // If parent context is invalid, create a new trace (shouldn't happen if propagation works)
            Activity? activity;
            if (parentContext.ActivityContext.IsValid())
            {
                activity = ActivitySource.StartActivity(
                    activityName,
                    ActivityKind.Consumer,
                    parentContext.ActivityContext
                );
            }
            else
            {
                // Fallback: create activity without parent (shouldn't normally happen)
                _logger.LogWarning("No valid parent context found in message headers, creating root activity");
                activity = ActivitySource.StartActivity(activityName, ActivityKind.Consumer);
            }

            // If activity is null, it means no listener is registered - log a warning
            if (activity == null)
            {
                _logger.LogWarning(
                    "Failed to create activity - ActivitySource '{ActivitySourceName}' may not be registered with OpenTelemetry. " +
                    "Check that 'consumer-service' is added to AddSource() in Program.cs",
                    ActivitySource.Name
                );
            }
            else
            {
                _logger.LogInformation(
                    "Created activity '{ActivityName}' with TraceId: {TraceId}, SpanId: {SpanId}",
                    activity.DisplayName,
                    activity.TraceId,
                    activity.SpanId
                );
            }

            try
            {
                var messageData = JsonSerializer.Deserialize<MessageData>(messageText);

                if (messageData != null)
                {
                    // Set OpenTelemetry semantic convention attributes for messaging
                    // These attributes help Azure AppInsights and Jaeger understand the message flow
                    activity?.SetTag("messaging.system", "rabbitmq");
                    activity?.SetTag("messaging.destination", _queueName);
                    activity?.SetTag("messaging.destination_kind", "queue");
                    activity?.SetTag("messaging.operation", "receive");
                    activity?.SetTag("messaging.message_id", messageData.MessageId);
                    activity?.SetTag("message.id", messageData.MessageId);
                    activity?.SetTag("message.text", messageData.Message);
                    activity?.SetTag("message.timestamp", messageData.Timestamp.ToString());

                    _logger.LogInformation(
                        "Processing message: {MessageId}, Content: {Message}",
                        messageData.MessageId,
                        messageData.Message
                    );

                    // Simulate some work
                    await Task.Delay(100, stoppingToken);

                    // Acknowledge message
                    _channel.BasicAck(ea.DeliveryTag, false);

                    // USER JOURNEY EXAMPLE: Mark the user journey as completed
                    // This is the final step in the user journey flow
                    // The journey started in the frontend and completes here after message processing
                    activity?.SetTag("user.journey.step", "completed");
                    activity?.SetTag("user.journey.status", "success");
                    activity?.AddEvent(new ActivityEvent("journey.completed"));

                    // Add attributes to help AppInsights identify completed journeys
                    activity?.SetTag(
                        "journey.duration_ms",
                        (DateTime.UtcNow - messageData.Timestamp).TotalMilliseconds
                    );

                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger.LogInformation(
                        "Successfully processed message: {MessageId}. User journey completed.",
                        messageData.MessageId
                    );
                }
                else
                {
                    _logger.LogWarning("Failed to deserialize message");
                    _channel.BasicNack(ea.DeliveryTag, false, true); // Requeue

                    // USER JOURNEY EXAMPLE: Mark journey as failed
                    activity?.SetTag("user.journey.step", "failed");
                    activity?.SetTag("user.journey.status", "error");
                    activity?.SetTag("user.journey.error", "Failed to deserialize message");
                    activity?.AddEvent(new ActivityEvent("journey.failed"));

                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to deserialize message");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                _channel.BasicNack(ea.DeliveryTag, false, true); // Requeue on error

                // USER JOURNEY EXAMPLE: Mark journey as failed with exception details
                activity?.SetTag("user.journey.step", "failed");
                activity?.SetTag("user.journey.status", "error");
                activity?.SetTag("user.journey.error", ex.Message);
                activity?.AddEvent(new ActivityEvent("journey.failed"));

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.SetTag("error.message", ex.Message);
            }
        };

        _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

        _logger.LogInformation($"Consumer registered for queue '{_queueName}'");

        // Keep running until cancellation
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consumer service stopping...");
        await base.StopAsync(cancellationToken);
    }
}

public class MessageData
{
    public string MessageId { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
}
