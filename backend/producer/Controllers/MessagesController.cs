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
    private static readonly ActivitySource ActivitySource = new("producer-api");
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public MessagesController(IModel channel, IConfiguration configuration)
    {
        _channel = channel;
        _queueName =
            configuration["QueueName"]
            ?? Environment.GetEnvironmentVariable("QUEUE_NAME")
            ?? "otel-demo-queue";
    }

    [HttpPost]
    public async Task<IActionResult> PostMessage([FromBody] MessageRequest request)
    {
        Console.WriteLine($"[DEBUG] PostMessage called at {DateTime.UtcNow:O}");

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

        // Start messaging span following OpenTelemetry semantic conventions
        // Span name: {destination} send
        var activityName = $"{_queueName} send";
        using var activity = ActivitySource.StartActivity(activityName, ActivityKind.Producer);

        // Set messaging semantic convention attributes
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", _queueName);
        activity?.SetTag("messaging.destination_kind", "queue");
        activity?.SetTag("messaging.operation", "send");
        activity?.SetTag("messaging.message_id", messageId);
        activity?.SetTag("message.text", request.Message);
        activity?.SetTag("message.length", request.Message?.Length ?? 0);

        try
        {
            // Propagate trace context through RabbitMQ headers using OpenTelemetry propagator
            ActivityContext contextToInject = default;
            if (activity != null)
            {
                contextToInject = activity.Context;
            }
            else if (Activity.Current != null)
            {
                contextToInject = Activity.Current.Context;
            }

            // Inject the ActivityContext into the message headers
            if (contextToInject != default)
            {
                properties.Headers ??= new Dictionary<string, object>();
                Propagator.Inject(
                    new PropagationContext(contextToInject, Baggage.Current),
                    properties.Headers,
                    (headers, key, value) => headers[key] = value);
            }

            Console.WriteLine($"[DEBUG] Publishing message to RabbitMQ queue '{_queueName}'");

            // Publish message
            _channel.BasicPublish(
                exchange: "",
                routingKey: _queueName,
                basicProperties: properties,
                body: body
            );

            activity?.SetStatus(ActivityStatusCode.Ok);

            Console.WriteLine($"[DEBUG] Message published successfully");

            return Ok(new { messageId, status = "enqueued" });
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[ERROR] Exception in PostMessage: {ex.GetType().Name} - {ex.Message}"
            );
            Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");

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
