using System.Diagnostics;
using Azure.Storage;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Producer.Services;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowFrontend",
        policy =>
        {
            policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    );
});

// Configuration helpers
var isDocker =
    Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")?.Contains("otel-collector")
    == true;
var defaultConnectionString =
    "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCzY4EwN/jaiwduXtrKWLoYIC3A7jqJA==;BlobEndpoint=http://azurite:10000/devstoreaccount1;QueueEndpoint=http://azurite:10001/devstoreaccount1;TableEndpoint=http://azurite:10002/devstoreaccount1;";

var connectionString =
    builder.Configuration.GetConnectionString("AzureStorage")
    ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? defaultConnectionString;

// Ensure connection string is never null or empty
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = defaultConnectionString;
}

// Normalize connection string for Docker
if (isDocker && !string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = connectionString
        .Replace("127.0.0.1", "azurite")
        .Replace("localhost", "azurite");
}

var queueName =
    builder.Configuration["QueueName"]
    ?? Environment.GetEnvironmentVariable("QUEUE_NAME")
    ?? "otel-demo-queue";

// Capture in local variables for closure
var finalConnectionString = connectionString ?? defaultConnectionString;
var finalQueueName = queueName;

// Logging helper
if (builder.Environment.IsDevelopment())
{
    var masked = finalConnectionString;
    if (masked.Contains("AccountKey="))
    {
        var keyStart = masked.IndexOf("AccountKey=") + 11;
        var keyEnd = masked.IndexOf(';', keyStart);
        if (keyEnd == -1)
            keyEnd = masked.Length;
        masked = masked.Substring(0, keyStart) + "***MASKED***" + masked.Substring(keyEnd);
    }
    Console.WriteLine($"[DEBUG] Environment: Docker={isDocker}, Queue={finalQueueName}");
    Console.WriteLine($"[DEBUG] Connection: {masked}");
    Console.WriteLine($"[DEBUG] Connection string length: {finalConnectionString?.Length ?? 0}");
}

// Use in-memory queue for development (Azurite connectivity issues in Docker)
// For production, use a real Azure Storage account
var useInMemoryQueue = Environment.GetEnvironmentVariable("USE_IN_MEMORY_QUEUE") != "false";

if (useInMemoryQueue)
{
    Console.WriteLine($"[INFO] Using in-memory queue for development");
    builder.Services.AddSingleton<InMemoryQueueService>();
}
else
{
    // QueueClient factory - only used if not using in-memory queue
    builder.Services.AddSingleton(sp =>
    {
        Console.WriteLine($"[DEBUG] Creating QueueClient at {DateTime.UtcNow:O}");

        QueueClient client;

        if (isDocker)
        {
            var accountName = "devstoreaccount1";
            var accountKey =
                "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCzY4EwN/jaiwduXtrKWLoYIC3A7jqJA==";
            var queueUri = new Uri($"http://azurite:10001/{accountName}/{finalQueueName}");

            var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };

            client = new QueueClient(
                queueUri,
                new StorageSharedKeyCredential(accountName, accountKey),
                options
            );
            Console.WriteLine($"[DEBUG] Using StorageSharedKeyCredential with URI: {queueUri}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(finalConnectionString))
            {
                throw new InvalidOperationException(
                    "Connection string is null or empty when creating QueueClient"
                );
            }

            var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
            client = new QueueClient(finalConnectionString, finalQueueName, options);
            Console.WriteLine($"[DEBUG] Using connection string");
        }

        return client;
    });
}

// OpenTelemetry
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(serviceName: "producer-api", serviceVersion: "1.0.0")
    )
    .WithTracing(tracing =>
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("producer-api")
            .AddOtlpExporter(options =>
            {
                var endpoint =
                    Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                    ?? "http://localhost:4317";
                options.Endpoint = new Uri(endpoint);
            })
    );

var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
