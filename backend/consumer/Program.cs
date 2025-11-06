using Consumer;
using Microsoft.Extensions.Http;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Check if we should use in-memory queue (via HTTP polling)
var useInMemoryQueue = Environment.GetEnvironmentVariable("USE_IN_MEMORY_QUEUE") != "false";
var producerApiUrl =
    Environment.GetEnvironmentVariable("PRODUCER_API_URL") ?? "http://producer-api:5000";

if (useInMemoryQueue)
{
    // Use HTTP client to poll producer API for messages
    builder.Services.AddHttpClient(
        "ProducerApi",
        client =>
        {
            client.BaseAddress = new Uri(producerApiUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        }
    );
    // Don't register QueueClient - it's optional in the constructor
    Console.WriteLine($"[INFO] Using in-memory queue via HTTP polling from {producerApiUrl}");
}
else
{
    // Configure Azure Storage Queue
    var defaultConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCzY4EwN/jaiwduXtrKWLoYIC3A7jqJA==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;";

    var storageConnectionString =
        builder.Configuration.GetConnectionString("AzureStorage")
        ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
        ?? defaultConnectionString;

    // Replace localhost/127.0.0.1 with azurite service name for Docker networking
    if (
        storageConnectionString.Contains("127.0.0.1")
        || storageConnectionString.Contains("localhost")
    )
    {
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrEmpty(otlpEndpoint) && otlpEndpoint.Contains("otel-collector"))
        {
            storageConnectionString = storageConnectionString
                .Replace("127.0.0.1", "azurite")
                .Replace("localhost", "azurite");
        }
    }

    var queueName =
        builder.Configuration["QueueName"]
        ?? Environment.GetEnvironmentVariable("QUEUE_NAME")
        ?? "otel-demo-queue";

    builder.Services.AddSingleton<Azure.Storage.Queues.QueueClient?>(
        sp => new Azure.Storage.Queues.QueueClient(storageConnectionString, queueName)
    );
    // Register null IHttpClientFactory since we're using Azure Queue
    builder.Services.AddSingleton<IHttpClientFactory?>(sp => null);
}

// Configure OpenTelemetry
var serviceName = "consumer-service";
var serviceVersion = "1.0.0";

builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(serviceName: serviceName, serviceVersion: serviceVersion)
    )
    .WithTracing(tracing =>
        tracing
            .AddHttpClientInstrumentation()
            .AddSource(serviceName)
            .AddOtlpExporter(options =>
            {
                // Use service name in Docker, localhost when running locally
                var otlpEndpoint =
                    Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                    ?? "http://localhost:4317";
                options.Endpoint = new Uri(otlpEndpoint);
            })
    );

// Register the worker service
builder.Services.AddHostedService<QueueProcessorWorker>();

var host = builder.Build();
host.Run();
