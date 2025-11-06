using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Storage.Queues;

namespace Consumer;

public class QueueProcessorWorker : BackgroundService
{
    private readonly QueueClient? _queueClient;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<QueueProcessorWorker> _logger;
    private static readonly ActivitySource ActivitySource = new("consumer-service");
    private readonly bool _useInMemoryQueue;

    public QueueProcessorWorker(
        ILogger<QueueProcessorWorker> logger,
        QueueClient? queueClient = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _queueClient = queueClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _useInMemoryQueue = httpClientFactory != null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Consumer service started. Polling queue for messages...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_useInMemoryQueue && _httpClientFactory != null)
                {
                    // Poll producer API for messages
                    await PollHttpQueueAsync(stoppingToken);
                }
                else if (_queueClient != null)
                {
                    // Poll Azure Storage Queue
                    await PollAzureQueueAsync(stoppingToken);
                }
                else
                {
                    _logger.LogError("No queue service available");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling queue");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PollHttpQueueAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var httpClient = _httpClientFactory!.CreateClient("ProducerApi");
            var response = await httpClient.GetAsync("/api/messages/receive", stoppingToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var content = await response.Content.ReadAsStringAsync(stoppingToken);
                var result = JsonSerializer.Deserialize<JsonElement>(content);
                
                if (result.TryGetProperty("messageText", out var messageTextElement))
                {
                    var base64Message = messageTextElement.GetString();
                    if (!string.IsNullOrEmpty(base64Message))
                    {
                        using var activity = ActivitySource.StartActivity("ProcessMessage");
                        
                        try
                        {
                            // Decode the message
                            var base64Bytes = Convert.FromBase64String(base64Message);
                            var jsonMessage = Encoding.UTF8.GetString(base64Bytes);
                            var messageData = JsonSerializer.Deserialize<MessageData>(jsonMessage);

                            if (messageData != null)
                            {
                                activity?.SetTag("message.original.id", messageData.MessageId);
                                activity?.SetTag("message.text", messageData.Message);
                                activity?.SetTag("message.timestamp", messageData.Timestamp.ToString());

                                // Link to parent trace if available
                                if (!string.IsNullOrEmpty(messageData.TraceId) && !string.IsNullOrEmpty(messageData.SpanId))
                                {
                                    activity?.SetTag("parent.trace_id", messageData.TraceId);
                                    activity?.SetTag("parent.span_id", messageData.SpanId);
                                    // Note: For proper trace linking, you'd use ActivityLinks, but for simplicity
                                    // we're just tagging the parent trace/span IDs
                                }

                                _logger.LogInformation(
                                    "Processing message: {MessageId}, Content: {Message}",
                                    messageData.MessageId,
                                    messageData.Message
                                );

                                // Simulate some work
                                await Task.Delay(100, stoppingToken);

                                activity?.SetStatus(ActivityStatusCode.Ok);
                                _logger.LogInformation(
                                    "Successfully processed message: {MessageId}",
                                    messageData.MessageId
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                            activity?.SetTag("error.type", ex.GetType().Name);
                            activity?.SetTag("error.message", ex.Message);
                            _logger.LogError(ex, "Error processing message");
                        }
                    }
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                // No messages available, wait before polling again
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling HTTP queue");
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task PollAzureQueueAsync(CancellationToken stoppingToken)
    {
        var messages = await _queueClient!.ReceiveMessagesAsync(
            maxMessages: 1,
            cancellationToken: stoppingToken
        );

        foreach (var message in messages.Value)
        {
            using var activity = ActivitySource.StartActivity("ProcessMessage");
            activity?.SetTag("message.id", message.MessageId);
            activity?.SetTag("message.dequeue.count", message.DequeueCount);

            try
            {
                // Decode the message
                var base64Bytes = Convert.FromBase64String(message.MessageText);
                var jsonMessage = Encoding.UTF8.GetString(base64Bytes);
                var messageData = JsonSerializer.Deserialize<MessageData>(jsonMessage);

                if (messageData != null)
                {
                    activity?.SetTag("message.original.id", messageData.MessageId);
                    activity?.SetTag("message.text", messageData.Message);
                    activity?.SetTag("message.timestamp", messageData.Timestamp.ToString());

                    _logger.LogInformation(
                        "Processing message: {MessageId}, Content: {Message}",
                        messageData.MessageId,
                        messageData.Message
                    );

                    await Task.Delay(100, stoppingToken);

                    await _queueClient.DeleteMessageAsync(
                        message.MessageId,
                        message.PopReceipt,
                        stoppingToken
                    );

                    activity?.SetStatus(ActivityStatusCode.Ok);
                    _logger.LogInformation(
                        "Successfully processed and deleted message: {MessageId}",
                        messageData.MessageId
                    );
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.SetTag("error.type", ex.GetType().Name);
                activity?.SetTag("error.message", ex.Message);
                _logger.LogError(ex, "Error processing message: {MessageId}", message.MessageId);

                await _queueClient.DeleteMessageAsync(
                    message.MessageId,
                    message.PopReceipt,
                    stoppingToken
                );
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
    }
}

public class MessageData
{
    public string MessageId { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime Timestamp { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
}
