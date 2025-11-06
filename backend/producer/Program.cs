using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

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

    // Declare queue
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
app.MapControllers();

app.Run();
