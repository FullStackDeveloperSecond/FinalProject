const correlationIdPattern = /^[A-Za-z0-9._-]{1,64}$/

export function createCorrelationId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID().replaceAll('-', '')
  }

  const bytes = new Uint8Array(16)
  globalThis.crypto.getRandomValues(bytes)
  return Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('')
}

export function isValidCorrelationId(value: string): boolean {
  return correlationIdPattern.test(value)
}
