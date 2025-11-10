# Setup and Running Instructions

## Prerequisites
- Docker Desktop running (Aspire uses it for RabbitMQ, Jaeger, and the OTEL collector)
- .NET 8 SDK installed (`dotnet --version` ≥ 8.0.100)
- Aspire templates (`dotnet new install Aspire.ProjectTemplates`)
- Node.js 18+ (required by the Vite dev server Aspire launches)
- (Optional) Azure account with an Application Insights resource

## Step 1: (Optional) Provide your Application Insights connection string

1. Go to [Azure Portal](https://portal.azure.com)
2. Open your **Application Insights** resource
3. Copy the full **Connection string** from the Overview blade
4. Export it as an environment variable (or set it in your shell profile):

   ```bash
   set AZURE_MONITOR_CONNECTION_STRING=InstrumentationKey=...
   # macOS/Linux: export AZURE_MONITOR_CONNECTION_STRING=...
   ```

If you skip this step, the demo still works—traces simply stay local in Jaeger.

## Step 2: Start the Aspire app host

From the repository root run:

```bash
dotnet run --project AzureAppInsights.AppHost
```

This single command coordinates everything:
- RabbitMQ broker + management UI (http://localhost:15672)
- OpenTelemetry Collector (OTLP gRPC 4317 / HTTP 4318)
- Jaeger all-in-one (http://localhost:16686)
- Producer API (reachable inside the Aspire network as `http://producer-api:5000`)
- Consumer worker
- React frontend (http://localhost:5173)

The Aspire dashboard launches at http://localhost:15000 showing health, logs, and environment variables for every resource.

## Step 3: Interact with the system

1. Open http://localhost:5173 and click **Send Message**
2. Observe traces in Jaeger (`react-frontend`, `producer-api`, `consumer-service`)
3. If you configured Azure, open Application Insights → Transaction Search to see the same trace in the cloud

## Viewing Logs & Telemetry

- **Aspire dashboard**: http://localhost:15000 (per-resource logs, configuration, health)
- **Jaeger UI**: http://localhost:16686 (local trace explorer)
- **RabbitMQ UI**: http://localhost:15672 (guest/guest)

## Troubleshooting

- Docker must be running before you start the app host
- If ports are already used, stop the conflicting process or change the endpoint ports in `AzureAppInsights.AppHost/AppHost.cs`
- Use the Aspire dashboard to tail logs for `consumer`, `producer-api`, and the collector
- Azure traces can take a minute to arrive—refresh the Transaction Search blade

## Stopping services

Press `Ctrl+C` in the terminal running `dotnet run`. Aspire gracefully shuts down the Node app, .NET services, and supporting containers.

