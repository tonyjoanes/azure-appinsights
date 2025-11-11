# Setup and Running Instructions

## Prerequisites
- Docker Desktop running (Aspire uses it for RabbitMQ, Jaeger, and the OTEL collector)
- .NET 8 SDK installed (`dotnet --version` ≥ 8.0.100)
- Aspire workload (`dotnet workload install aspire`)
- Node.js 18+ (required by the Vite dev server Aspire launches)
- (Optional) Azure account with an Application Insights resource

## Step 1: (Optional) Provide your Application Insights connection string

To light up the richer Azure portal experience (Requests/Dependencies) we now ship with the Azure Monitor exporter. Provide the connection string before starting Aspire:

1. Go to [Azure Portal](https://portal.azure.com)
2. Open your **Application Insights** resource
3. Copy the full **Connection string** from the Overview blade
4. Export it as an environment variable (or set it in your shell profile):

   ```bash
   set AZURE_MONITOR_CONNECTION_STRING=InstrumentationKey=...
   # macOS/Linux: export AZURE_MONITOR_CONNECTION_STRING=...
   ```

If you skip this step, traces stay local (Jaeger still works and Azure upload is simply skipped).

## Step 2: Start the Aspire app host

From the repository root run:

```bash
dotnet run --project AzureAppInsights.AppHost
```

This single command coordinates everything:
- RabbitMQ broker + management UI (http://localhost:15672)
- OpenTelemetry Collector proxies (OTLP gRPC 4317 / HTTP 4318)
- Jaeger all-in-one (http://localhost:16686)
- Producer API (http://localhost:5001)
- Consumer worker
- React frontend (http://localhost:5173)

The Aspire dashboard launches at https://localhost:17022 (Follow the login link printed to the console). It shows per-resource health, logs, and environment variables.

## Step 3: Interact with the system

1. Open http://localhost:5173 and click **Send Message**
2. Observe traces in Jaeger (`react-frontend`, `producer-api`, `consumer-service`)
3. If you supplied the Azure connection string, open Application Insights → Transaction Search to see the same trace with full request/dependency classification

## Viewing Logs & Telemetry

- **Aspire dashboard**: https://localhost:17022 (requires the one-time login link from the console). The **Structured logs** tab now shows the JSON console output (scopes + UTC timestamps) for each resource.
- **Jaeger UI**: http://localhost:16686 (local trace explorer)
- **RabbitMQ UI**: http://localhost:15672 (guest/guest)
- **Application Insights**: use Transaction Search or KQL (traces/dependencies) for the cloud view

## Troubleshooting

- Docker must be running before you start the app host
- If ports are already used, stop the conflicting process or change the host ports in `AzureAppInsights.AppHost/AppHost.cs`
- Use the Aspire dashboard to tail logs for `consumer`, `producer-api`, and the collector
- Azure traces can take a minute to arrive—refresh the Transaction Search blade or run the KQL helper from `USER_JOURNEY_EXAMPLE.md`

## Stopping services

Press `Ctrl+C` in the terminal running `dotnet run`. Aspire gracefully shuts down the Node app, .NET services, and supporting containers.

