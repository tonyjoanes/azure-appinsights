# User Journey Tracking Example

This document explains how user journey tracking is implemented in this OpenTelemetry demo.

## Overview

User journeys represent complete user interactions that span multiple services. In this demo, a user journey starts when a user clicks the "Send Message" button in the frontend and completes when the consumer service finishes processing the message.

## Implementation

### 1. Frontend - Journey Start

In `frontend/src/App.jsx`, a user journey span is created when the user clicks the button:

```javascript
// Create a user journey span
const journeySpan = tracer.startSpan('UserJourney:SendMessage', {
  kind: 0, // SpanKind.INTERNAL
})

// Mark this as a user journey with custom attributes
journeySpan.setAttribute('user.journey.name', 'SendMessage')
journeySpan.setAttribute('user.journey.id', journeyId)
journeySpan.setAttribute('user.journey.step', 'started')
journeySpan.setAttribute('user.action', 'button_click')

// Add an event to mark the journey start
journeySpan.addEvent('journey.started', {
  timestamp: Date.now(),
  journeyId: journeyId,
})
```

**Key Attributes:**
- `user.journey.name`: Identifies the type of journey
- `user.journey.id`: Unique identifier for this specific journey
- `user.journey.step`: Current step in the journey
- `user.action`: The user action that triggered the journey

### 2. Producer API - Journey Progress

In `backend/producer/Controllers/MessagesController.cs`, the journey context is automatically propagated via HTTP headers. We add attributes to track progress:

```csharp
// Add attributes to link this operation to the user journey
activity?.SetTag("user.journey.step", "message_enqueued");
activity?.AddEvent(new ActivityEvent("journey.message_enqueued"));
```

### 3. Consumer - Journey Completion

In `backend/consumer/QueueProcessorWorker.cs`, the journey is marked as completed:

```csharp
// Mark the user journey as completed
activity?.SetTag("user.journey.step", "completed");
activity?.SetTag("user.journey.status", "success");
activity?.AddEvent(new ActivityEvent("journey.completed"));

// Add duration metric
activity?.SetTag("journey.duration_ms", 
    (DateTime.UtcNow - messageData.Timestamp).TotalMilliseconds);
```

## Journey Flow

```
Frontend (Journey Start)
  ├─ journey.started event
  ├─ user.journey.step = "started"
  └─ API call (trace context propagated)
      │
      ↓
Producer API
  ├─ user.journey.step = "message_enqueued"
  ├─ journey.message_enqueued event
  └─ Message to queue (trace context in headers)
      │
      ↓
Consumer
  ├─ user.journey.step = "completed"
  ├─ user.journey.status = "success"
  ├─ journey.completed event
  └─ journey.duration_ms attribute
```

## Benefits in Azure AppInsights

### 1. **Journey Analytics**
Query journeys by name, status, or step:
```kusto
traces
| where customDimensions["user.journey.name"] == "SendMessage"
| summarize count() by tostring(customDimensions["user.journey.step"])
```

### 2. **Completion Rates**
Calculate success rates:
```kusto
traces
| where customDimensions["user.journey.name"] == "SendMessage"
| summarize 
    total = count(),
    completed = countif(customDimensions["user.journey.status"] == "success")
| extend completionRate = (completed * 100.0) / total
```

### 3. **Journey Duration Analysis**
Analyze how long journeys take:
```kusto
traces
| where customDimensions["user.journey.name"] == "SendMessage"
| where customDimensions["journey.duration_ms"] != ""
| extend duration = todouble(customDimensions["journey.duration_ms"])
| summarize 
    avgDuration = avg(duration),
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99)
```

### 4. **Journey Step Analysis**
Identify where journeys fail:
```kusto
traces
| where customDimensions["user.journey.name"] == "SendMessage"
| where customDimensions["user.journey.status"] == "error"
| summarize count() by tostring(customDimensions["user.journey.step"])
```

### 5. **User Behavior Patterns**
Track user actions and sessions:
```kusto
traces
| where customDimensions["user.journey.name"] == "SendMessage"
| summarize 
    journeys = count(),
    uniqueUsers = dcount(customDimensions["user.session.id"])
    by bin(timestamp, 1h)
```

## Custom Attributes Reference

| Attribute | Description | Example |
|-----------|-------------|---------|
| `user.journey.name` | Name of the journey type | "SendMessage" |
| `user.journey.id` | Unique journey identifier | "journey-1234567890-abc123" |
| `user.journey.step` | Current step in journey | "started", "api_completed", "completed" |
| `user.journey.status` | Journey status | "success", "error" |
| `user.journey.error` | Error message (if failed) | "Failed to deserialize message" |
| `user.action` | User action that triggered journey | "button_click" |
| `user.session.id` | User session identifier | "session-123" |
| `journey.duration_ms` | Total journey duration in milliseconds | 150.5 |

## Events Reference

| Event | Description | When Fired |
|-------|-------------|------------|
| `journey.started` | Journey begins | Frontend button click |
| `journey.api_completed` | API call successful | After message enqueued |
| `journey.message_enqueued` | Message added to queue | Producer API |
| `journey.completed` | Journey finished successfully | Consumer processing complete |
| `journey.failed` | Journey failed | Any error in the flow |

## Best Practices

1. **Use Consistent Naming**: Use consistent `user.journey.name` values to group related journeys
2. **Track Key Steps**: Add `user.journey.step` attributes at each major milestone
3. **Include Context**: Add relevant business context (user ID, session ID, etc.)
4. **Handle Errors**: Always mark journeys as failed with error details
5. **Measure Duration**: Track journey duration to identify performance issues

## Extending the Example

To add more journey types:

1. **Create a new journey span** in the frontend with a different `user.journey.name`
2. **Add step markers** at key points in your flow
3. **Mark completion** in the final service
4. **Query in AppInsights** using the journey name

Example:
```javascript
// New journey type
const journeySpan = tracer.startSpan('UserJourney:Checkout', {
  kind: 0,
})
journeySpan.setAttribute('user.journey.name', 'Checkout')
// ... rest of implementation
```

## See Also

- [Azure Application Insights - User Flows](https://learn.microsoft.com/en-us/azure/azure-monitor/app/user-flows)
- [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/)
- [AppInsights KQL Queries](https://learn.microsoft.com/en-us/azure/data-explorer/kusto/query/)

