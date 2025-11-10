# OpenTelemetry End-to-End Demo: React → Producer API → RabbitMQ → Consumer

This example demonstrates a complete OpenTelemetry (OTEL) tracing flow across a distributed system:
- **React frontend** (instrumented with OTEL) calls Producer API
- **Producer API** (ASP.NET Core) enqueues a message on RabbitMQ with trace context propagation
- **Consumer service** (.NET Worker) processes messages from RabbitMQ, continuing the trace
- All services export traces via **OTLP** to a local OpenTelemetry Collector
- Collector exports traces to **Jaeger** (local visualization) and **Azure Application Insights** (cloud)

## Architecture

```
Frontend (React) 
    ↓ HTTP (trace context propagated)
Producer API (ASP.NET Core)
    ↓ RabbitMQ (trace context in headers)
Consumer Service (.NET Worker)
    ↓
OpenTelemetry Collector
    ↓
    ├─→ Jaeger (localhost:16686)
    └─→ Azure Application Insights
```

## Prerequisites

- Docker Desktop (the demo uses containers for RabbitMQ, Jaeger, and the OTEL collector)
- .NET 8 SDK (8.0.100 or later)
- Aspire templates (install once via `dotnet new install Aspire.ProjectTemplates`)
- Node.js 18+ (used by the Vite dev server that Aspire launches)
- (Optional) Azure account and Application Insights resource

## Quick Start

1. **Set up Azure Application Insights (optional)**
   - Create an Application Insights resource in Azure Portal
   - Copy the **Connection String** (not just the Instrumentation Key)
   - Create a `.env` file in the project root:
     ```
     AZURE_MONITOR_CONNECTION_STRING=InstrumentationKey=xxx;IngestionEndpoint=https://xxx.in.applicationinsights.azure.com/
     ```

2. **Start the Aspire app host**
   ```bash
   dotnet run --project AzureAppInsights.AppHost
   ```
   Aspire orchestrates:
   - RabbitMQ (amqp:5672, management UI: http://localhost:15672)
   - Jaeger all-in-one (UI: http://localhost:16686)
   - OpenTelemetry Collector (OTLP gRPC: 4317, OTLP HTTP: 4318)
   - Producer API (addressed inside the Aspire network as `http://producer-api:5000`)
   - Consumer worker
   - React frontend (http://localhost:5173)

3. **Access the application**
   - Frontend: http://localhost:5173
   - Jaeger UI: http://localhost:16686
   - RabbitMQ Management: http://localhost:15672 (guest/guest)

4. **Send a message**
   - Click "Send Message" in the frontend
   - View the complete trace in Jaeger UI (services: `react-frontend`, `producer-api`, `consumer-service`)
   - Check Azure Application Insights portal for traces (if configured)

> Need to run the services individually without Aspire? Launch the producer, consumer, collector, RabbitMQ, and frontend exactly as before—the codebase still supports direct execution. Aspire simply automates that wiring.

## Key Features Demonstrated

### 1. **Trace Context Propagation**
   - Frontend → API: Automatic via HTTP headers (W3C Trace Context)
   - API → Queue: Manual injection into RabbitMQ message headers
   - Queue → Consumer: Manual extraction from RabbitMQ message headers

### 2. **OpenTelemetry Semantic Conventions**
   - Messaging attributes (`messaging.system`, `messaging.destination`, etc.)
   - Span kinds (Producer, Consumer)
   - Error handling with proper status codes

### 3. **Multiple Exporters**
   - Local development: Jaeger for immediate visualization
   - Production: Azure Application Insights for cloud monitoring

### 4. **Structured Logging**
   - All services use `ILogger` with structured logging
   - Logs are correlated with traces via trace context

### 5. **User Journey Tracking**
   - Complete user journeys are tracked from frontend to consumer
   - Journey attributes (`user.journey.name`, `user.journey.step`, etc.) help AppInsights group and analyze user behavior
   - Journey events mark key milestones: `journey.started`, `journey.api_completed`, `journey.completed`
   - This enables powerful analytics in AppInsights like:
     - Journey completion rates
     - Average journey duration
     - Journey step analysis
     - User behavior patterns

## Configuration

### Environment Variables

- `AZURE_MONITOR_CONNECTION_STRING`: Full connection string from Azure Portal
- `QUEUE_NAME`: RabbitMQ queue name (default: `otel-demo-queue`)
- `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`: RabbitMQ connection
- `OTEL_EXPORTER_OTLP_ENDPOINT`: OTLP collector endpoint (default: `http://localhost:4317`)

### Frontend Environment Variables

- `VITE_API_URL`: Producer API URL (default: `http://localhost:5000`)
- `VITE_OTLP_ENDPOINT`: OTLP HTTP endpoint for traces (default: `http://localhost:4318/v1/traces`)

## Code Structure

- `frontend/`: React app with OTEL instrumentation
- `backend/producer/`: ASP.NET Core API that publishes messages
- `backend/consumer/`: .NET Worker service that consumes messages
- `AzureAppInsights.ServiceDefaults/`: Shared Aspire defaults (service discovery, OTEL setup, health checks)
- `AzureAppInsights.AppHost/`: Aspire app host that orchestrates every component
- `AzureAppInsights.sln`: Solution tying the pieces together
- `otel-collector-config.yaml`: OpenTelemetry Collector configuration shared with the Aspire container

## Viewing Traces

### Jaeger UI (Local)
1. Open http://localhost:16686
2. Select service: `react-frontend`, `producer-api`, or `consumer-service`
3. Click "Find Traces"
4. Click on a trace to see the complete flow

### Azure Application Insights
1. Go to Azure Portal → Your Application Insights resource
2. Navigate to **Transaction search** or **Application map**
3. View traces with full context and performance metrics

## Security / Production Considerations

- For production, use proper authentication for Azure Monitor exporter
- Adjust sampling rates to control telemetry volume
- Use secure connection strings (Key Vault, managed identity)
- Configure resource attributes for proper service identification
- Consider using Azure Monitor Exporter directly instead of OTLP for better integration