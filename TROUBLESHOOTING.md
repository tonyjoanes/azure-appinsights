# Troubleshooting: Consumer Service Not Appearing in Jaeger

If you're not seeing `consumer-service` in Jaeger, follow these steps:

## 1. Verify the Aspire App Host is Running

Start (or restart) the distributed app host:
```bash
dotnet run --project AzureAppInsights.AppHost
```

With the host running you can open the Aspire dashboard at http://localhost:15000 to inspect resource health and logs. In the resource tree, the `consumer` node should be **Running**. Typical startup logs include:
- "Consumer service started. Waiting for messages from RabbitMQ..."
- "Consumer registered for queue 'otel-demo-queue'"
- When messages arrive: "Message received from RabbitMQ queue"

## 2. Verify Consumer is Processing Messages

After sending a message from the frontend, tail the consumer logs from the Aspire dashboard (Logs tab) or run the worker directly in a separate terminal:
```bash
dotnet run --project backend/consumer/Consumer.csproj
```

Look for log lines such as:
- "Message received from RabbitMQ queue"
- "Extracted trace context - Valid: True, TraceId: ..."
- "Created activity 'otel-demo-queue receive' with TraceId: ..., SpanId: ..."
- "Processing message: {MessageId}, Content: {Message}"
- "Successfully processed message: {MessageId}. User journey completed."

## 3. Check Activity Creation

If you see this warning in logs:
```
Failed to create activity - ActivitySource 'consumer-service' may not be registered with OpenTelemetry
```

This means the ActivitySource isn't registered. Verify in `backend/consumer/Program.cs`:
```csharp
.AddSource("consumer-service")  // Must match ActivitySource name
```

## 4. Check Trace Context Propagation

If you see:
```
No valid parent context found in message headers, creating root activity
```

This means trace context isn't being propagated from producer to consumer. Check:
- Producer is injecting trace context into RabbitMQ headers (see `MessagesController.cs`)
- Consumer is extracting trace context from headers (see `QueueProcessorWorker.cs`)
- Headers are being passed correctly through RabbitMQ

## 5. Verify OpenTelemetry Configuration

Aspire injects the required environment variables automatically. Confirm the following values are present in the consumer logs:
- `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`
- Service name `consumer-service`
- ActivitySource registered via `.AddSource("consumer-service")`

## 6. Check Jaeger Service List

In Jaeger UI (http://localhost:16686):
1. Go to "Search" tab
2. Check the "Service" dropdown - you should see:
   - `react-frontend`
   - `producer-api`
   - `consumer-service` ← Should appear here

## 7. Search for Consumer Traces

In Jaeger UI:
1. Select service: `consumer-service`
2. Click "Find Traces"
3. You should see traces with span name: `otel-demo-queue receive`

## 8. Check Complete Trace Flow

When you send a message, the complete trace should show:
1. `UserJourney:SendMessage` (react-frontend)
2. `SendMessageButtonClick` (react-frontend)
3. HTTP request to `/api/messages` (react-frontend)
4. `POST /api/messages` (producer-api)
5. `otel-demo-queue send` (producer-api)
6. `otel-demo-queue receive` (consumer-service) ← This should appear

## 9. Common Issues

### Issue: Consumer not receiving messages
**Solution**: Check the RabbitMQ container logs (resource name `rabbitmq`):
```bash
docker logs $(docker ps --filter name=rabbitmq --quiet)
```

### Issue: Activity is null
**Solution**: Ensure ActivitySource name matches in:
- `QueueProcessorWorker.cs`: `new ActivitySource("consumer-service")`
- `Program.cs`: `.AddSource("consumer-service")`

### Issue: Traces appear but consumer-service not in service list
**Solution**: This is normal - the service name comes from the resource attributes. Check the trace details to see if consumer spans are there.

### Issue: Consumer spans not connected to producer spans
**Solution**: Check trace context propagation:
- Verify producer injects context: `Propagator.Inject(...)`
- Verify consumer extracts context: `Propagator.Extract(...)`
- Check that both use the same propagator: `Propagators.DefaultTextMapPropagator`

## 10. Debug Steps

Add more logging:
1. Check consumer logs for activity creation messages
2. Verify trace IDs match between producer and consumer
3. Check that messages are being consumed (not stuck in queue)

## Still Not Working?

1. Stop the app host (`Ctrl+C`) and restart it:
   ```bash
   dotnet run --project AzureAppInsights.AppHost
   ```

2. Send a test message and immediately check the consumer logs via the Aspire dashboard or by running `dotnet run --project backend/consumer/Consumer.csproj`.

3. Inspect the OpenTelemetry collector container logs:
   ```bash
   docker logs $(docker ps --filter name=otel-collector --quiet)
   ```

4. If necessary, run the consumer outside Aspire to isolate issues:
   ```bash
   dotnet run --project backend/consumer/Consumer.csproj
   ```

