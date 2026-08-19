const problemDetailsContentType = 'application/problem+json'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  code?: string
  traceId?: string
  correlationId?: string
  errors?: Record<string, string[]>
}

interface ApiErrorOptions {
  status: number
  code: string
  detail?: string
  traceId?: string
  correlationId?: string
  fieldErrors?: Record<string, string[]>
  retryAfter?: string
  cause?: unknown
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string
  readonly correlationId?: string
  readonly fieldErrors?: Record<string, string[]>
  readonly retryAfter?: string

  constructor(message: string, options: ApiErrorOptions) {
    super(message, { cause: options.cause })
    this.name = 'ApiError'
    this.status = options.status
    this.code = options.code
    this.traceId = options.traceId
    this.correlationId = options.correlationId
    this.fieldErrors = options.fieldErrors
    this.retryAfter = options.retryAfter
  }
}

export async function createApiError(response: Response): Promise<ApiError> {
  const problemDetails = await readProblemDetails(response)
  const status = problemDetails?.status ?? response.status
  const code = problemDetails?.code ?? defaultCodeForStatus(status)
  const message = problemDetails?.detail
    ?? problemDetails?.title
    ?? (response.statusText || 'Request failed.')

  return new ApiError(message, {
    status,
    code,
    traceId: problemDetails?.traceId,
    correlationId:
      problemDetails?.correlationId ?? response.headers.get('X-Correlation-ID') ?? undefined,
    fieldErrors: problemDetails?.errors,
    retryAfter: response.headers.get('Retry-After') ?? undefined,
  })
}

export function createNetworkError(cause: unknown): ApiError {
  return new ApiError('Unable to reach the service.', {
    status: 0,
    code: 'network_error',
    cause,
  })
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError
}

async function readProblemDetails(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('Content-Type')?.toLowerCase()
  if (!contentType?.includes(problemDetailsContentType)) {
    return undefined
  }

  try {
    const value: unknown = await response.clone().json()
    return isProblemDetails(value) ? value : undefined
  } catch {
    return undefined
  }
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function defaultCodeForStatus(status: number): string {
  const codes: Record<number, string> = {
    400: 'validation_failed',
    401: 'authentication_required',
    403: 'authorization_forbidden',
    404: 'resource_not_found',
    405: 'request_method_not_allowed',
    409: 'request_conflict',
    413: 'request_content_too_large',
    415: 'request_content_type_unsupported',
    422: 'request_content_unprocessable',
    429: 'rate_limit_exceeded',
    500: 'unexpected_error',
    503: 'service_unavailable',
  }

  return codes[status] ?? 'request_failed'
}
