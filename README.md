# OpenTelemetry end-to-end demo: React -> Producer API -> Queue -> Consumer

This example demonstrates an OTEL-based tracing flow:
- React frontend (instrumented) calls Producer API
- Producer API enqueues a message on a queue
- Consumer service polls the queue and processes the message
- All apps export traces via OTLP to a local OpenTelemetry Collector
- Collector exports traces to Jaeger (local) and optionally Azure Monitor / Application Insights (using your connection string)

Prerequisites
- Docker & Docker Compose
- .NET 7+ SDK (or .NET 8, adjust the target frameworks if you change)
- Node 16+
- (optional) Azure account and Application Insights resource

High-level:
1. Copy `.env.example` to `.env` and set `AZURE_MONITOR_CONNECTION_STRING` to your App Insights connection string if you want Azure export.
2. Start Docker Compose to bring up the OTEL Collector, Jaeger UI and Azurite:
   docker compose up -d
3. Run the Producer API:
   cd backend/producer
   dotnet run
4. Run the Consumer service:
   cd backend/consumer
   dotnet run
5. Start the frontend:
   cd frontend
   npm install
   npm run dev
6. Open the frontend (default Vite dev server URL), click the button — traces will be emitted to the collector.
7. View traces:
   - Jaeger UI: http://localhost:16686
   - Azure portal: Application Insights -> Transactions (if you set the AZURE_MONITOR_CONNECTION_STRING and allowed outbound network access)

Design notes
- All instrumentation in apps exports with OTLP to the local collector at http://localhost:4318 (HTTP) and grpc at 4317 where appropriate.
- Collector uses the `azuremonitor` exporter when `AZURE_MONITOR_CONNECTION_STRING` is set and also sends to Jaeger for local visualization.
- The queue is configurable:
  - Default recommended for local runs: Azurite (Docker Compose included). Use the Azurite storage connection string in the example `.env`.
  - For cloud runs: set `AZURE_STORAGE_CONNECTION_STRING` to your Azure Storage account connection string and the same queue name will be used.
- The consumer is implemented as a background worker that polls the queue and creates traces for message processing.

Security / production considerations
- For production use Application Insights SDK exporters or authenticate the Collector to Azure properly.
- Adjust sampling, resource attributes, and exporter configuration to match production requirements.