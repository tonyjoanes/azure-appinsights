using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Consumer;
using LanguageExt;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using static LanguageExt.Prelude;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var rabbitConfig = RabbitConfig.FromEnvironment();

builder.Services.AddSingleton<IConnectionFactory>(_ => RabbitMqHelpers.CreateFactory(rabbitConfig));

builder.Services.AddSingleton<IConnection>(sp =>
    RabbitMqHelpers.CreateConnectionWithRetry(
        sp.GetRequiredService<IConnectionFactory>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("RabbitMQ")
    )
);

builder.Services.AddSingleton<IModel>(sp =>
    RabbitMqHelpers.CreateChannel(
        sp.GetRequiredService<IConnection>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("RabbitMQ"),
        rabbitConfig.QueueName
    )
);

builder.Services.AddHostedService<QueueProcessorWorker>();

const string serviceName = "consumer-service";
const string serviceVersion = "1.0.0";

var openTelemetryBuilder = builder.Services.AddOpenTelemetry();
openTelemetryBuilder.ConfigureResource(resource =>
{
    resource.AddService(serviceName: serviceName, serviceVersion: serviceVersion);
});
openTelemetryBuilder.WithTracing(tracing => tracing.AddSource(serviceName));

var host = builder.Build();

host.Run();

internal sealed record RabbitConfig(
    string QueueName,
    string Username,
    string Password,
    string Host,
    int Port,
    Option<Uri> Uri
)
{
    private const string DefaultQueue = "otel-demo-queue";
    private const string DefaultUsername = "guest";
    private const string DefaultPassword = "guest";
    private const string DefaultHost = "localhost";
    private const int DefaultPort = 5672;

    public static RabbitConfig FromEnvironment() =>
        new(
            QueueName: Get("QUEUE_NAME").IfNone(DefaultQueue),
            Username: Get("RABBITMQ_USERNAME").IfNone(DefaultUsername),
            Password: Get("RABBITMQ_PASSWORD").IfNone(DefaultPassword),
            Host: Get("RABBITMQ_HOST").IfNone(DefaultHost),
            Port: Get("RABBITMQ_PORT").Bind(ParseInt).IfNone(DefaultPort),
            Uri: Get("RABBITMQ_URI").Bind(ParseUri)
        );

    private static Option<string> Get(string key) =>
        Optional(Environment.GetEnvironmentVariable(key))
            .Map(value => value.Trim())
            .Filter(value => !string.IsNullOrWhiteSpace(value));

    private static Option<int> ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Some(parsed)
            : None;

    private static Option<Uri> ParseUri(string value) =>
        System.Uri.TryCreate(value, UriKind.Absolute, out var uri) ? Some(uri) : None;
}

internal static class RabbitMqHelpers
{
    private const int MaxConnectionRetries = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static ConnectionFactory CreateFactory(RabbitConfig config)
    {
        var factory = new ConnectionFactory
        {
            UserName = config.Username,
            Password = config.Password,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = RetryDelay,
        };

        config.Uri.Match(
            Some: uri =>
            {
                factory.Uri = uri;
                return Unit.Default;
            },
            None: () =>
            {
                factory.HostName = config.Host;
                factory.Port = config.Port;
                return Unit.Default;
            }
        );

        return factory;
    }

    public static IConnection CreateConnectionWithRetry(IConnectionFactory factory, ILogger logger)
    {
        Exception? lastException = null;

        foreach (var attempt in Enumerable.Range(1, MaxConnectionRetries))
        {
            try
            {
                return factory.CreateConnection();
            }
            catch (BrokerUnreachableException ex)
            {
                lastException = ex;

                if (attempt == MaxConnectionRetries)
                {
                    throw;
                }

                logger.LogWarning(
                    ex,
                    "Failed to connect to RabbitMQ (attempt {Attempt}/{Max}). Retrying in {Delay} seconds...",
                    attempt,
                    MaxConnectionRetries,
                    RetryDelay.TotalSeconds
                );

                Thread.Sleep(RetryDelay);
            }
        }

        throw lastException
            ?? new InvalidOperationException("Unable to create a RabbitMQ connection.");
    }

    public static IModel CreateChannel(IConnection connection, ILogger logger, string queueName)
    {
        var channel = connection.CreateModel();

        channel.QueueDeclare(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        logger.LogInformation("RabbitMQ queue '{QueueName}' declared", queueName);

        return channel;
    }
}
