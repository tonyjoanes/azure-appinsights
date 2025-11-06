using Consumer;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);

// RabbitMQ Configuration
var rabbitmqHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var rabbitmqPort = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672");
var rabbitmqUsername = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
var rabbitmqPassword = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
var queueName = Environment.GetEnvironmentVariable("QUEUE_NAME") ?? "otel-demo-queue";

// Register RabbitMQ Connection Factory
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    return new ConnectionFactory
    {
        HostName = rabbitmqHost,
        Port = rabbitmqPort,
        UserName = rabbitmqUsername,
        Password = rabbitmqPassword,
        DispatchConsumersAsync = true,
    };
});

// Register RabbitMQ Connection
builder.Services.AddSingleton<IConnection>(sp =>
{
    var factory = sp.GetRequiredService<IConnectionFactory>();
    return factory.CreateConnection();
});

// Register RabbitMQ Channel
builder.Services.AddSingleton<IModel>(sp =>
{
    var connection = sp.GetRequiredService<IConnection>();
    var channel = connection.CreateModel();

    // Declare queue (same as producer)
    channel.QueueDeclare(
        queue: queueName,
        durable: false,
        exclusive: false,
        autoDelete: false,
        arguments: null
    );

    Console.WriteLine($"[INFO] RabbitMQ queue '{queueName}' declared");
    return channel;
});

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
