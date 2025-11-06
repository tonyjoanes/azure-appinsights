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

    public QueueProcessorWorker(
        IModel channel,
        ILogger<QueueProcessorWorker> logger)
    {
        _channel = channel;
        _queueName =
            Environment.GetEnvironmentVariable("QUEUE_NAME")
            ?? "otel-demo-queue";
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Consumer service started. Waiting for messages from RabbitMQ...");

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var messageText = Encoding.UTF8.GetString(body);
            var properties = ea.BasicProperties;

            // Extract trace context from RabbitMQ headers using OpenTelemetry propagator
            var parentContext = Propagator.Extract(
                default,
                properties.Headers ?? new Dictionary<string, object>(),
                (headers, key) =>
                {
                    if (headers.TryGetValue(key, out var value))
                    {
                        var stringValue = value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value?.ToString();
                        return string.IsNullOrEmpty(stringValue) ? Array.Empty<string>() : new[] { stringValue };
                    }
                    return Array.Empty<string>();
                });

            // Start messaging span following OpenTelemetry semantic conventions
            // Span name: {destination} receive
            var activityName = $"{_queueName} receive";
            using var activity = ActivitySource.StartActivity(
                activityName,
                ActivityKind.Consumer,
                parentContext.ActivityContext);

            try
            {
                var messageData = JsonSerializer.Deserialize<MessageData>(messageText);

                if (messageData != null)
                {
                    // Set messaging semantic convention attributes
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

                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger.LogInformation(
                        "Successfully processed message: {MessageId}",
                        messageData.MessageId
                    );
                }
                else
                {
                    _logger.LogWarning("Failed to deserialize message");
                    _channel.BasicNack(ea.DeliveryTag, false, true); // Requeue
                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to deserialize message");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                _channel.BasicNack(ea.DeliveryTag, false, true); // Requeue on error

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
