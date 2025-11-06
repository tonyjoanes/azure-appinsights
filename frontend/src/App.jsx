import { useState } from 'react'
import './App.css'
import { trace, context } from '@opentelemetry/api'

function App() {
  const [status, setStatus] = useState('idle')
  const [message, setMessage] = useState('')

  const handleButtonClick = async () => {
    setStatus('sending')
    setMessage('')

    // Create a manual span for the button click
    const tracer = trace.getTracer('react-frontend', '1.0.0')
    const span = tracer.startSpan('SendMessageButtonClick', {
      kind: 0, // SpanKind.INTERNAL - this wraps the entire operation
    })

    // Set the span as active context so child spans (like fetch) will use it as parent
    return context.with(trace.setSpan(context.active(), span), async () => {
      try {
        // This fetch call will be automatically instrumented by OpenTelemetry
        // The trace context will be propagated to the Producer API
        // Since we set the span as active above, the fetch span will be a child of SendMessageButtonClick
        const response = await fetch('http://localhost:5000/api/messages', {
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
        span.setStatus({ code: 1 }) // OK
        span.setAttribute('message.id', data.messageId || 'N/A')
      } catch (error) {
        setStatus('error')
        setMessage(`Error: ${error.message}`)
        console.error('Failed to send message:', error)
        span.setStatus({ code: 2, message: error.message }) // ERROR
        span.recordException(error)
      } finally {
        span.end()
      }
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



