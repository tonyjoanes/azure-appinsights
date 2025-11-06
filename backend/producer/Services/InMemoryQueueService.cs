using System.Collections.Concurrent;

namespace Producer.Services;

// Simple in-memory queue for development/testing
public class InMemoryQueueService
{
    private readonly ConcurrentQueue<QueueMessage> _queue = new();
    private readonly ILogger<InMemoryQueueService>? _logger;

    public InMemoryQueueService(ILogger<InMemoryQueueService>? logger = null)
    {
        _logger = logger;
    }

    public Task SendMessageAsync(string messageText, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Enqueueing message to in-memory queue");
        _queue.Enqueue(
            new QueueMessage { MessageText = messageText, EnqueuedAt = DateTime.UtcNow }
        );
        return Task.CompletedTask;
    }

    public Task<QueueMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
    {
        if (_queue.TryDequeue(out var message))
        {
            _logger?.LogInformation("Dequeued message from in-memory queue");
            return Task.FromResult<QueueMessage?>(message);
        }
        return Task.FromResult<QueueMessage?>(null);
    }

    public int Count => _queue.Count;
}

public class QueueMessage
{
    public string MessageText { get; set; } = string.Empty;
    public DateTime EnqueuedAt { get; set; }
}
