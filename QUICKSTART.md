# Quick Start Guide

## 🚀 Quick Setup (3 Steps!)

### 1. Grab your Azure Application Insights connection string (optional)

1. Go to https://portal.azure.com
2. Open your **Application Insights** resource (or create one)
3. Copy the full **Connection string** from the Overview blade (`InstrumentationKey=...;IngestionEndpoint=...`)

When you launch the demo you can provide it via an environment variable:

```bash
set AZURE_MONITOR_CONNECTION_STRING=InstrumentationKey=...
# macOS/Linux: export AZURE_MONITOR_CONNECTION_STRING=...
```

If you skip this step the collector will still export to Jaeger locally; Azure export is simply disabled.

### 2. Start the Aspire app host 🎉

```bash
dotnet run --project AzureAppInsights.AppHost
```

This one command launches:
- ✅ RabbitMQ (queue + management UI)
- ✅ OpenTelemetry Collector (OTLP gRPC + HTTP)
- ✅ Jaeger all-in-one (local trace viewer)
- ✅ Producer API (ASP.NET Core)
- ✅ Consumer worker (.NET)
- ✅ React frontend (Vite dev server)

An Aspire dashboard appears at http://localhost:15000 where you can watch health and logs in real time.

### 3. Test the end-to-end flow

1. Open http://localhost:5173 in your browser
2. Click **Send Message**
3. View traces:
   - **Local:** http://localhost:16686 (Jaeger UI)
   - **Azure:** Portal → Application Insights → Transaction Search (if you supplied a connection string)

### Stop Everything

Press `Ctrl+C` in the terminal running the app host. Aspire stops every process and container it started.

## 📊 Viewing Traces & Logs

- **Aspire dashboard:** http://localhost:15000 (health, logs, and environment variables)
- **Jaeger UI:** http://localhost:16686 (select `react-frontend`, `producer-api`, or `consumer-service`)
- **RabbitMQ UI:** http://localhost:15672 (guest/guest)

## ⚠️ Troubleshooting

- Make sure Docker Desktop is running (Aspire uses it for RabbitMQ, Jaeger, and the OTEL collector)
- If ports clash, stop whatever else is using 5173/16686/15672/4317/4318
- Use the Aspire dashboard to drill into logs for each resource
- Traces can take a minute to appear in Azure—refresh the Transaction Search blade if needed

