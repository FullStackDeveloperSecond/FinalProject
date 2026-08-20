import { ApiError } from '@doselect/web-shared/api'
import { describe, expect, it, vi } from 'vitest'

const mockPush = vi.fn()

vi.mock('../router', () => ({
  default: { push: mockPush },
}))

const { handleGlobalApiError } = await import('./client')

describe('handleGlobalApiError', () => {
  it('redirects to /unauthorized on a 401', () => {
    handleGlobalApiError(new ApiError('Unauthorized', { status: 401, code: 'authentication_required' }))

    expect(mockPush).toHaveBeenCalledWith('/unauthorized')
  })

  it('redirects to /forbidden on a 403', () => {
    handleGlobalApiError(new ApiError('Forbidden', { status: 403, code: 'authorization_forbidden' }))

    expect(mockPush).toHaveBeenCalledWith('/forbidden')
  })

  it('does not redirect for other statuses (e.g. a 409 concurrency conflict handled inline)', () => {
    mockPush.mockClear()

    handleGlobalApiError(new ApiError('Conflict', { status: 409, code: 'concurrency_conflict' }))

    expect(mockPush).not.toHaveBeenCalled()
  })
})
