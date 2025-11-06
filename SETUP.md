# Setup and Running Instructions

## Prerequisites
- Docker & Docker Compose installed and running
- .NET 8 SDK installed
- Node.js 16+ installed
- (Optional) Azure account with Application Insights resource

## Step 1: Get Your Azure Application Insights Connection String

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to your **Application Insights** resource (or create one if you don't have it)
3. Go to **Overview** → Click on **Connection String** (or go to **Configure** → **Connection Strings**)
4. Copy the connection string. It looks like:
   ```
   InstrumentationKey=xxxx-xxxx-xxxx-xxxx;IngestionEndpoint=https://xxxx.in.applicationinsights.azure.com/
   ```

## Step 2: Configure Environment Variables

Create a `.env` file in the root directory with the following content:

```env
# Azure Application Insights Connection String (optional - leave empty if not using Azure)
AZURE_MONITOR_CONNECTION_STRING=InstrumentationKey=xxxx-xxxx-xxxx-xxxx;IngestionEndpoint=https://xxxx.in.applicationinsights.azure.com/

# Azure Storage Connection String (for local development with Azurite)
AZURE_STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCzY4EwN/jaiwduXtrKWLoYIC3A7jqJA==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;

# Queue name
QUEUE_NAME=otel-demo-queue
```

**Note:** 
- Replace the `AZURE_MONITOR_CONNECTION_STRING` with your actual connection string from Step 1
- The `AZURE_STORAGE_CONNECTION_STRING` is for local Azurite (already configured correctly)
- If you don't want to use Azure Application Insights, you can leave `AZURE_MONITOR_CONNECTION_STRING` empty

## Step 3: Start Docker Services

Open a terminal in the project root and run:

```bash
docker compose up -d
```

This starts:
- **OpenTelemetry Collector** (ports 4317, 4318) - collects traces from all services
- **Jaeger UI** (port 16686) - view traces locally at http://localhost:16686
- **Azurite** (ports 10000-10002) - local Azure Storage emulator for queues

Verify services are running:
```bash
docker compose ps
```

## Step 4: Start the Producer API

Open a **new terminal** and run:

```bash
cd backend/producer
dotnet restore
dotnet run
```

The API will start on `http://localhost:5000`

## Step 5: Start the Consumer Service

Open **another new terminal** and run:

```bash
cd backend/consumer
dotnet restore
dotnet run
```

The consumer will start polling the queue for messages.

## Step 6: Start the Frontend

Open **another new terminal** and run:

```bash
cd frontend
npm install  # Only needed first time
npm run dev
```

The frontend will start on `http://localhost:5173` (or similar Vite port)

## Step 7: Test the System

1. Open your browser to `http://localhost:5173`
2. Click the **"Send Message"** button
3. You should see a success message

## Step 8: View Traces

### Local (Jaeger UI)
- Open http://localhost:16686
- Select service: `react-frontend`, `producer-api`, or `consumer-service`
- Click "Find Traces"
- You should see the complete trace flow: Frontend → Producer → Queue → Consumer

### Azure Application Insights
1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to your Application Insights resource
3. Go to **Transaction search** or **Application map**
4. You should see traces appearing (may take 1-2 minutes)

## Troubleshooting

### Docker services not starting
- Make sure Docker Desktop is running
- Check ports 4317, 4318, 16686, 10000-10002 are not in use
- Run `docker compose logs` to see errors

### Producer API can't connect to queue
- Make sure Azurite is running: `docker compose ps`
- Check the `AZURE_STORAGE_CONNECTION_STRING` in your `.env` file
- Verify the queue name matches in all services

### Traces not appearing in Azure
- Verify your `AZURE_MONITOR_CONNECTION_STRING` is correct in `.env`
- Check the connection string format (should include InstrumentationKey and IngestionEndpoint)
- Wait 1-2 minutes for traces to appear
- Check `docker compose logs otel-collector` for export errors

### Frontend can't connect to Producer API
- Make sure Producer API is running on port 5000
- Check browser console for CORS errors (if any)
- Verify the API endpoint URL in `frontend/src/App.jsx` matches your Producer API URL

## Stopping Services

To stop all services:
```bash
# Stop Docker services
docker compose down

# Stop .NET services (Ctrl+C in each terminal)
# Stop frontend (Ctrl+C in terminal)
```

