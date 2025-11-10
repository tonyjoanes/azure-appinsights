import { useState } from 'react'
import './App.css'
import { trace, context } from '@opentelemetry/api'

function App() {
  const [status, setStatus] = useState('idle')
  const [message, setMessage] = useState('')

  const handleButtonClick = async () => {
    setStatus('sending')
    setMessage('')

    // USER JOURNEY EXAMPLE: Create a user journey span that represents the complete user interaction
    // This is a business-level concept that helps track user behavior in AppInsights
    // The journey spans the entire flow: Frontend → API → Queue → Consumer
    const tracer = trace.getTracer('react-frontend', '1.0.0')
    const journeyId = `journey-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
    
    // Create the user journey span - this will be the root span for this user interaction
    const journeySpan = tracer.startSpan('UserJourney:SendMessage', {
      kind: 0, // SpanKind.INTERNAL
    })
    
    // Mark this as a user journey with custom attributes
    // These attributes help AppInsights identify and group user journeys
    journeySpan.setAttribute('user.journey.name', 'SendMessage')
    journeySpan.setAttribute('user.journey.id', journeyId)
    journeySpan.setAttribute('user.journey.step', 'started')
    journeySpan.setAttribute('user.action', 'button_click')
    journeySpan.setAttribute('user.session.id', sessionStorage.getItem('sessionId') || 'anonymous')
    
    // Add an event to mark the journey start
    journeySpan.addEvent('journey.started', {
      timestamp: Date.now(),
      journeyId: journeyId,
    })

    // Create a nested span for the button click operation
    const span = tracer.startSpan('SendMessageButtonClick', {
      kind: 0, // SpanKind.INTERNAL - this wraps the API call
    })

    // Set the journey span as active context so all child spans are part of the journey
    return context.with(trace.setSpan(context.active(), journeySpan), async () => {
      // Set the button click span as active for the immediate operation
      return context.with(trace.setSpan(context.active(), span), async () => {
      try {
        // This fetch call will be automatically instrumented by OpenTelemetry
        // The trace context will be propagated to the Producer API
        // Since we set the span as active above, the fetch span will be a child of SendMessageButtonClick
        // Prefer the environment-provided API URL (set by Aspire). Fallback to localhost for manual runs.
        const apiUrl = import.meta.env.VITE_API_URL
          || ((window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1')
            ? 'http://localhost:5000'
            : 'http://producer-api:5000')
        const response = await fetch(`${apiUrl}/api/messages`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
          },
          body: JSON.stringify({
            message: `Hello from frontend at ${new Date().toISOString()}`,
          }),
        })

        if (!response.ok) {
          throw new Error(`HTTP error! status: ${response.status}`)
        }

        const data = await response.json()
        setStatus('success')
        setMessage(`Message sent! ID: ${data.messageId || 'N/A'}`)
        
        // Mark the button click span as successful
        span.setStatus({ code: 1 }) // OK
        span.setAttribute('message.id', data.messageId || 'N/A')
        span.end()
        
        // Update journey with success status
        // Note: The journey continues through the queue to the consumer
        // The consumer will mark the final completion, but we end the frontend journey span here
        // since we can't wait for async consumer processing
        journeySpan.setAttribute('user.journey.step', 'api_completed')
        journeySpan.setAttribute('message.id', data.messageId || 'N/A')
        journeySpan.addEvent('journey.api_completed', {
          timestamp: Date.now(),
          messageId: data.messageId,
        })
        // End the journey span - the consumer will add its own attributes to the trace
        journeySpan.end()
      } catch (error) {
        setStatus('error')
        setMessage(`Error: ${error.message}`)
        console.error('Failed to send message:', error)
        
        // Mark spans as failed
        span.setStatus({ code: 2, message: error.message }) // ERROR
        span.recordException(error)
        span.end()
        
        // Mark journey as failed
        journeySpan.setStatus({ code: 2, message: error.message })
        journeySpan.setAttribute('user.journey.step', 'failed')
        journeySpan.setAttribute('user.journey.error', error.message)
        journeySpan.addEvent('journey.failed', {
          timestamp: Date.now(),
          error: error.message,
        })
        journeySpan.end() // End journey on error
      }
    })
    })
  }

  return (
    <div className="app">
      <div className="container">
        <h1>OpenTelemetry Demo</h1>
        <p className="subtitle">
          Click the button below to send a message through the system.
          <br />
          The trace will flow: Frontend → Producer API → Queue → Consumer
          <br />
          <strong>User Journey:</strong> A complete user journey is tracked from start to finish,
          <br />
          making it easy to analyze user behavior in Azure AppInsights.
        </p>
        
        <button 
          onClick={handleButtonClick}
          disabled={status === 'sending'}
          className="send-button"
        >
          {status === 'sending' ? 'Sending...' : 'Send Message'}
        </button>

        {message && (
          <div className={`status-message ${status}`}>
            {message}
          </div>
        )}

        <div className="info">
          <h3>View Traces:</h3>
          <ul>
            <li>
              <a href="http://localhost:16686" target="_blank" rel="noopener noreferrer">
                Jaeger UI (Local)
              </a>
            </li>
            <li>
              <a href="https://portal.azure.com" target="_blank" rel="noopener noreferrer">
                Azure Application Insights (Portal)
              </a>
            </li>
          </ul>
        </div>
      </div>
    </div>
  )
}

export default App



