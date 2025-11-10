using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace Producer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly IModel _channel;
    private readonly string _queueName;
    private readonly ILogger<MessagesController> _logger;
    
    // ActivitySource for creating custom spans - follows OpenTelemetry naming convention: service-name
    private static readonly ActivitySource ActivitySource = new("producer-api");
    
    // TextMapPropagator for injecting trace context into RabbitMQ message headers
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public MessagesController(IModel channel, IConfiguration configuration, ILogger<MessagesController> logger)
    {
        _channel = channel;
        _queueName =
            configuration["QueueName"]
            ?? Environment.GetEnvironmentVariable("QUEUE_NAME")
            ?? "otel-demo-queue";
        _logger = logger;
    }

    [HttpPost]
    public IActionResult PostMessage([FromBody] MessageRequest request)
    {
        _logger.LogInformation("PostMessage called at {Timestamp}", DateTime.UtcNow);

        var messageId = Guid.NewGuid().ToString();
        var messageData = new
        {
            MessageId = messageId,
            Message = request.Message,
            Timestamp = DateTime.UtcNow,
        };

        var jsonMessage = JsonSerializer.Serialize(messageData);
        var body = Encoding.UTF8.GetBytes(jsonMessage);

        // Create message properties
        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.MessageId = messageId;
        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Create a span for the message publishing operation
        // Following OpenTelemetry semantic conventions for messaging:
        // - Span name: {destination} send
        // - Span kind: Producer (indicates this service is producing messages)
        var activityName = $"{_queueName} send";
        using var activity = ActivitySource.StartActivity(activityName, ActivityKind.Producer);

        // Set OpenTelemetry semantic convention attributes for messaging
        // These attributes help Azure AppInsights and Jaeger understand the message flow
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _queueName);
        activity?.SetTag("messaging.destination_kind", "queue");
        activity?.SetTag("messaging.operation", "send");
        activity?.SetTag("messaging.message_id", messageId);
        activity?.SetTag("message.text", request.Message);
        activity?.SetTag("message.length", request.Message?.Length ?? 0);
        
        // USER JOURNEY EXAMPLE: Check if this is part of a user journey
        // The journey context is automatically propagated from the frontend via HTTP headers
        // We can add journey attributes to help AppInsights track the journey through the system
        if (Activity.Current != null)
        {
            // Add attributes to link this operation to the user journey
            // These will appear in AppInsights and help correlate the journey across services
            activity?.SetTag("user.journey.step", "message_enqueued");
            activity?.AddEvent(new ActivityEvent("journey.message_enqueued"));
        }

        try
        {
            // CRITICAL: Propagate trace context through RabbitMQ message headers
            // This ensures the trace continues in the consumer service
            // The trace context (trace ID, span ID) is injected into message headers
            // so the consumer can create a child span linked to this producer span
            ActivityContext contextToInject = default;
            if (activity != null)
            {
                contextToInject = activity.Context;
            }
            else if (Activity.Current != null)
            {
                // Fallback to current activity if our activity wasn't created
                contextToInject = Activity.Current.Context;
            }

            // Inject trace context into RabbitMQ message headers
            // The propagator handles encoding trace context into W3C Trace Context format
            if (contextToInject != default)
            {
                properties.Headers ??= new Dictionary<string, object>();
                Propagator.Inject(
                    new PropagationContext(contextToInject, Baggage.Current),
                    properties.Headers,
                    (headers, key, value) => headers[key] = value);
            }

            _logger.LogInformation("Publishing message to RabbitMQ queue '{QueueName}'", _queueName);

            // Publish message
            _channel.BasicPublish(
                exchange: "",
                routingKey: _queueName,
                basicProperties: properties,
                body: body
            );

            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger.LogInformation("Message published successfully. MessageId: {MessageId}", messageId);

            return Ok(new { messageId, status = "enqueued" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception in PostMessage: {ExceptionType} - {Message}", ex.GetType().Name, ex.Message);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetTag("error.stack", ex.StackTrace);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class MessageRequest
{
    public string? Message { get; set; }
}
