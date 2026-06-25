const API_BASE = '/api'

export interface FetchOptions extends RequestInit {
  headers?: Record<string, string>
}

export async function fetchJson<T>(path: string, options?: FetchOptions): Promise<T> {
  const url = `${API_BASE}${path}`
  const headers = {
    'Content-Type': 'application/json',
    ...options?.headers,
  }

  const response = await fetch(url, { ...options, headers })
  if (!response.ok) {
    throw new Error(`API Error: ${response.status} ${response.statusText}`)
  }

  return response.json() as Promise<T>
}

export async function fetchStream<T>(
  path: string,
  onData: (data: T) => void,
  options?: FetchOptions & { signal?: AbortSignal },
  onError?: (error: Error) => void,
): Promise<void> {
  const url = `${API_BASE}${path}`
  const headers = {
    'Content-Type': 'application/json',
    ...options?.headers,
  }

  const response = await fetch(url, { ...options, headers })
  if (!response.ok) {
    throw new Error(`API Error: ${response.status} ${response.statusText}`)
  }

  if (!response.body) {
    throw new Error('Response body is null')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() || ''

      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const jsonStr = line.slice(6)
          try {
            const data = JSON.parse(jsonStr) as T
            onData(data)
          } catch (e) {
            onError?.(new Error(`Failed to parse JSON: ${jsonStr}`))
          }
        }
      }
    }

    if (buffer.startsWith('data: ')) {
      const jsonStr = buffer.slice(6)
      try {
        const data = JSON.parse(jsonStr) as T
        onData(data)
      } catch (e) {
        onError?.(new Error(`Failed to parse JSON: ${jsonStr}`))
      }
    }
  } catch (error) {
    onError?.(error instanceof Error ? error : new Error(String(error)))
  } finally {
    reader.releaseLock()
  }
}
