# Quick Start Guide

## 🚀 Quick Setup (3 Steps!)

### 1. Get Azure Application Insights Connection String

1. Go to https://portal.azure.com
2. Find your **Application Insights** resource (or create one)
3. Go to **Overview** → Click **Connection String**
4. Copy the full connection string (looks like: `InstrumentationKey=xxx;IngestionEndpoint=https://xxx...`)

### 2. Create `.env` File

Create a `.env` file in the project root:

```env
AZURE_MONITOR_CONNECTION_STRING=YOUR_CONNECTION_STRING_HERE
AZURE_STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCzY4EwN/jaiwduXtrKWLoYIC3A7jqJA==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;
QUEUE_NAME=otel-demo-queue
```

**Replace `YOUR_CONNECTION_STRING_HERE` with your actual connection string from step 1.**

### 3. Start Everything with Docker Compose! 🎉

```bash
docker compose up --build
```

That's it! This single command starts:
- ✅ Azurite (Azure Storage emulator)
- ✅ Jaeger UI (trace visualization)
- ✅ OpenTelemetry Collector
- ✅ Producer API
- ✅ Consumer Service
- ✅ Frontend

### 4. Test It!

1. Wait for all services to start (may take 1-2 minutes on first run)
2. Open http://localhost:5173 in your browser
3. Click "Send Message"
4. View traces:
   - **Local:** http://localhost:16686 (Jaeger UI)
   - **Azure:** Go to Azure Portal → Your App Insights → Transaction search

### Stop Everything

```bash
docker compose down
```

## 📊 Viewing Traces

### Local (Jaeger)
- URL: http://localhost:16686
- Select any service and click "Find Traces"

### Azure Application Insights
- Go to Azure Portal → Your Application Insights resource
- Navigate to **Transaction search** or **Application map**
- Traces appear within 1-2 minutes

## ⚠️ Troubleshooting

**Docker not starting?**
- Make sure Docker Desktop is running
- Check ports aren't in use: `docker compose ps`

**Can't connect to Azure?**
- Verify your connection string is correct in `.env`
- Check format includes both `InstrumentationKey` and `IngestionEndpoint`
- Wait 1-2 minutes for traces to appear

**Services won't start?**
- Make sure you're in the correct directories
- Run `dotnet restore` if packages are missing
- Check `docker compose logs` for errors

