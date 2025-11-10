using System;
using System.Threading;
using Consumer;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// RabbitMQ Configuration
var rabbitmqUri = Environment.GetEnvironmentVariable("RABBITMQ_URI");
var rabbitmqHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var rabbitmqPort = int.Parse(Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672");
var rabbitmqUsername = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME") ?? "guest";
var rabbitmqPassword = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
var queueName = Environment.GetEnvironmentVariable("QUEUE_NAME") ?? "otel-demo-queue";

// Register RabbitMQ Connection Factory
builder.Services.AddSingleton<IConnectionFactory>(sp =>
{
    var factory = new ConnectionFactory
    {
        UserName = rabbitmqUsername,
        Password = rabbitmqPassword,
        DispatchConsumersAsync = true,
    };

    if (
        !string.IsNullOrWhiteSpace(rabbitmqUri)
        && Uri.TryCreate(rabbitmqUri, UriKind.Absolute, out var uri)
    )
    {
        factory.Uri = uri;
    }
    else
    {
        factory.HostName = rabbitmqHost;
        factory.Port = rabbitmqPort;
    }

    return factory;
});

// Register RabbitMQ Connection
builder.Services.AddSingleton<IConnection>(sp =>
{
    var factory = sp.GetRequiredService<IConnectionFactory>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("RabbitMQ");

    const int maxRetries = 5;
    var attempt = 0;

    while (true)
    {
        try
        {
            return factory.CreateConnection();
        }
        catch (BrokerUnreachableException ex) when (attempt < maxRetries)
        {
            attempt++;
            logger.LogWarning(
                ex,
                "Failed to connect to RabbitMQ (attempt {Attempt}/{Max}). Retrying in 2 seconds...",
                attempt,
                maxRetries
            );
            Thread.Sleep(TimeSpan.FromSeconds(2));
        }
    }
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

    // Use ILoggerFactory to avoid DI issues with IModel interface
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("RabbitMQ");
    logger.LogInformation("RabbitMQ queue '{QueueName}' declared", queueName);
    return channel;
});

// Configure OpenTelemetry
const string serviceName = "consumer-service";
const string serviceVersion = "1.0.0";

var openTelemetryBuilder = builder.Services.AddOpenTelemetry();
openTelemetryBuilder.ConfigureResource(resource =>
{
    resource.AddService(serviceName: serviceName, serviceVersion: serviceVersion);
});
openTelemetryBuilder.WithTracing(tracing => tracing.AddSource(serviceName));

// Register the worker service
builder.Services.AddHostedService<QueueProcessorWorker>();

var host = builder.Build();
host.Run();
