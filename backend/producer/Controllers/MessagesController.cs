using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Mvc;
using Producer.Services;

namespace Producer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly QueueClient? _queueClient;
    private readonly InMemoryQueueService? _inMemoryQueue;
    private static readonly ActivitySource ActivitySource = new("producer-api");

    public MessagesController(
        QueueClient? queueClient = null,
        InMemoryQueueService? inMemoryQueue = null)
    {
        _queueClient = queueClient;
        _inMemoryQueue = inMemoryQueue;
    }

    [HttpPost]
    public async Task<IActionResult> PostMessage([FromBody] MessageRequest request)
    {
        Console.WriteLine($"[DEBUG] PostMessage called at {DateTime.UtcNow:O}");

        using var activity = ActivitySource.StartActivity("EnqueueMessage");
        activity?.SetTag("message.text", request.Message);
        activity?.SetTag("message.length", request.Message?.Length ?? 0);

        try
        {
            var messageId = Guid.NewGuid().ToString();
            var messageData = new
            {
                MessageId = messageId,
                Message = request.Message,
                Timestamp = DateTime.UtcNow,
                TraceId = Activity.Current?.TraceId.ToString(),
                SpanId = Activity.Current?.SpanId.ToString(),
            };

            var jsonMessage = JsonSerializer.Serialize(messageData);
            var base64Message = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonMessage));

            Console.WriteLine($"[DEBUG] About to enqueue message at {DateTime.UtcNow:O}");

            // Use in-memory queue if available, otherwise use Azure Queue
            if (_inMemoryQueue != null)
            {
                await _inMemoryQueue.SendMessageAsync(base64Message);
                Console.WriteLine($"[DEBUG] Message enqueued to in-memory queue");
            }
            else if (_queueClient != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _queueClient.SendMessageAsync(base64Message, cancellationToken: cts.Token);
                Console.WriteLine($"[DEBUG] Message enqueued to Azure Queue");
            }
            else
            {
                throw new InvalidOperationException("No queue service available");
            }

            activity?.SetTag("message.id", messageId);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Ok(new { messageId, status = "enqueued" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Exception in PostMessage: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"[ERROR] StackTrace: {ex.StackTrace}");

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            activity?.SetTag("error.stack", ex.StackTrace);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("receive")]
    public async Task<IActionResult> ReceiveMessage()
    {
        if (_inMemoryQueue != null)
        {
            var message = await _inMemoryQueue.ReceiveMessageAsync();
            if (message != null)
            {
                return Ok(new { messageText = message.MessageText, enqueuedAt = message.EnqueuedAt });
            }
            return NoContent(); // No messages available
        }
        return BadRequest(new { error = "In-memory queue not available" });
    }
}

public class MessageRequest
{
    public string? Message { get; set; }
}
