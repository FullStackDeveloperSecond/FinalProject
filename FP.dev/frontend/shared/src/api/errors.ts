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
  const message = resolveUserFacingMessage(problemDetails, status, code)

  return new ApiError(message, {
    status,
    code,
    traceId: problemDetails?.traceId,
    correlationId:
      problemDetails?.correlationId ?? response.headers.get('X-Correlation-ID') ?? undefined,
    fieldErrors: localizeFieldErrors(problemDetails?.errors),
    retryAfter: response.headers.get('Retry-After') ?? undefined,
  })
}

export function createNetworkError(cause: unknown): ApiError {
  return new ApiError('目前無法連線至服務，請確認網路後再試一次。', {
    status: 0,
    code: 'network_error',
    cause,
  })
}

function resolveUserFacingMessage(
  problemDetails: ProblemDetails | undefined,
  status: number,
  code: string,
): string {
  const detail = problemDetails?.detail?.trim()
  if (detail && containsHanText(detail)) {
    return detail
  }

  const messagesByCode: Record<string, string> = {
    ai_service_unavailable: 'AI 服務暫時無法使用，請稍後再試，或改由人工客服協助。',
    authentication_required: '請先登入後再繼續。',
    authorization_forbidden: '你沒有權限執行此操作。',
    request_conflict: '資料狀態已變更，請重新整理後再試一次。',
    resource_not_found: '找不到要求的資料。',
    validation_failed: '請檢查輸入內容後再試一次。',
  }
  if (messagesByCode[code]) {
    return messagesByCode[code]
  }

  const messagesByStatus: Record<number, string> = {
    400: '請檢查輸入內容後再試一次。',
    401: '請先登入後再繼續。',
    403: '你沒有權限執行此操作。',
    404: '找不到要求的資料。',
    405: '目前不支援這項操作。',
    409: '資料狀態已變更，請重新整理後再試一次。',
    413: '上傳內容超過大小限制。',
    415: '不支援這種內容格式。',
    422: '部分資料無法處理，請檢查後再試一次。',
    429: '操作過於頻繁，請稍後再試。',
    500: '系統發生未預期的錯誤，請稍後再試。',
    503: '服務暫時無法使用，請稍後再試。',
  }
  return messagesByStatus[status] ?? '請求失敗，請稍後再試。'
}

function localizeFieldErrors(
  fieldErrors: Record<string, string[]> | undefined,
): Record<string, string[]> | undefined {
  if (!fieldErrors) return undefined

  return Object.fromEntries(Object.entries(fieldErrors).map(([field, messages]) => [
    field,
    messages.map(message => containsHanText(message)
      ? message
      : '輸入內容不符合要求。'),
  ]))
}

function containsHanText(message: string): boolean {
  return /[\u3400-\u9fff]/u.test(message)
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
