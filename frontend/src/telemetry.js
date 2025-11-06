import { WebTracerProvider } from '@opentelemetry/sdk-trace-web';
import { Resource } from '@opentelemetry/resources';
import { SemanticResourceAttributes } from '@opentelemetry/semantic-conventions';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { UserInteractionInstrumentation } from '@opentelemetry/instrumentation-user-interaction';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { BatchSpanProcessor } from '@opentelemetry/sdk-trace-base';

let provider = null;

// Initialize OpenTelemetry SDK with error handling
try {
  // Configure exporter
  // Use environment variable or default to localhost (for Docker, this will be set via Vite)
  const otlpUrl = import.meta.env.VITE_OTLP_ENDPOINT || 'http://localhost:4318/v1/traces';
  const exporter = new OTLPTraceExporter({
    url: otlpUrl,
    headers: {},
  });

  // Initialize provider with span processor in constructor (required for v2.x)
  provider = new WebTracerProvider({
    resource: new Resource({
      [SemanticResourceAttributes.SERVICE_NAME]: 'react-frontend',
      [SemanticResourceAttributes.SERVICE_VERSION]: '1.0.0',
    }),
    spanProcessors: [new BatchSpanProcessor(exporter)],
  });

  // Register instrumentations
  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new UserInteractionInstrumentation(),
      new FetchInstrumentation({
        // Propagate trace context in fetch requests
        propagateTraceContext: true,
      }),
    ],
  });

  // Register and start the provider
  provider.register();

  console.log('OpenTelemetry SDK initialized for React frontend');
} catch (error) {
  console.error('Failed to initialize OpenTelemetry SDK:', error);
  // Continue without telemetry - don't break the app
}

// Export for manual tracing if needed
export { provider };



