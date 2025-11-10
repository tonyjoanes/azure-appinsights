using System.IO;
using Aspire.Hosting;
using DotNetEnv;

var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
var envFilePath = Path.Combine(projectRoot, ".env");
if (!File.Exists(envFilePath))
{
    envFilePath = Path.Combine(solutionRoot, ".env");
}
if (File.Exists(envFilePath))
{
    Env.Load(envFilePath);
}

var builder = DistributedApplication.CreateBuilder(args);

// Parameters
var azureMonitorConnectionStringValue =
    Environment.GetEnvironmentVariable("AZURE_MONITOR_CONNECTION_STRING") ?? string.Empty;
var azureMonitorConnectionString = builder.AddParameter(
    "azure-monitor-connection-string",
    secret: true,
    value: azureMonitorConnectionStringValue
);

// RabbitMQ broker
var rabbitMq = builder
    .AddContainer("rabbitmq", "rabbitmq:3-management-alpine")
    .WithEndpoint(name: "amqp", port: 5672, targetPort: 5672, scheme: "amqp")
    .WithEndpoint(name: "management", port: 15672, targetPort: 15672)
    .WithEnvironment("RABBITMQ_DEFAULT_USER", "guest")
    .WithEnvironment("RABBITMQ_DEFAULT_PASS", "guest");
var rabbitMqAmqp = rabbitMq.GetEndpoint("amqp");

// Jaeger for local trace inspection
var jaeger = builder
    .AddContainer("jaeger", "jaegertracing/all-in-one:1.57")
    .WithEndpoint(name: "ui", port: 16686, targetPort: 16686)
    .WithEndpoint(name: "collector-grpc", port: 14317, targetPort: 4317)
    .WithEndpoint(name: "collector-http", port: 14318, targetPort: 4318)
    .WithEnvironment("COLLECTOR_OTLP_ENABLED", "true");

// OpenTelemetry collector
var collectorConfigPath = Path.GetFullPath(
    Path.Combine(solutionRoot, "otel-collector-config.yaml")
);
var otelCollector = builder
    .AddContainer("otel-collector", "otel/opentelemetry-collector-contrib:latest")
    .WithArgs("--config=/etc/otel-collector-config.yaml")
    .WithBindMount(collectorConfigPath, "/etc/otel-collector-config.yaml", isReadOnly: true)
    .WithEnvironment("AZURE_MONITOR_CONNECTION_STRING", azureMonitorConnectionString)
    .WithEndpoint(name: "otlp-grpc", port: 4317, targetPort: 4317)
    .WithEndpoint(name: "otlp-http", port: 4318, targetPort: 4318);

// Producer API (ASP.NET Core)
var producer = builder
    .AddProject<Projects.Producer>("producer-api")
    .WithEnvironment("RABBITMQ_URI", rabbitMqAmqp)
    .WithEnvironment("RABBITMQ_USERNAME", "guest")
    .WithEnvironment("RABBITMQ_PASSWORD", "guest")
    .WithEnvironment("QUEUE_NAME", "otel-demo-queue")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
    .WithEnvironment("AZURE_MONITOR_CONNECTION_STRING", azureMonitorConnectionString)
    .WithHttpEndpoint(name: "producer-http", port: 5001);

// Consumer worker
var consumer = builder
    .AddProject<Projects.Consumer>("consumer")
    .WithEnvironment("RABBITMQ_URI", rabbitMqAmqp)
    .WithEnvironment("RABBITMQ_USERNAME", "guest")
    .WithEnvironment("RABBITMQ_PASSWORD", "guest")
    .WithEnvironment("QUEUE_NAME", "otel-demo-queue")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4317")
    .WithEnvironment("AZURE_MONITOR_CONNECTION_STRING", azureMonitorConnectionString);

// Frontend (Vite dev server)
var frontend = builder
    .AddNpmApp("frontend", "../frontend", "dev")
    .WithEnvironment("VITE_OTLP_ENDPOINT", "http://localhost:4318")
    .WithEnvironment("VITE_API_URL", producer.GetEndpoint("producer-http"))
    .WithHttpEndpoint(name: "web", port: 5173, targetPort: 5173, isProxied: false);

builder.Build().Run();
