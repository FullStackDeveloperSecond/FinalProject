import { ApiError } from '@doselect/web-shared/api'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockPush = vi.fn()
const currentRoute = { value: { name: 'home' as string | undefined, fullPath: '/' } }

vi.mock('../router', () => ({
  default: { push: mockPush, currentRoute },
}))

const { handleGlobalApiError } = await import('./client')

describe('handleGlobalApiError', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockPush.mockClear()
    currentRoute.value = { name: 'home', fullPath: '/' }
  })

  // ⚠ alex review 第三輪 P2#4：登入頁（本 PR 新增）現在已經存在，「no /admin/login page exists
  // yet」這個前提已經失效——401 必須清掉本地 Session 快取並導回登入頁，帶上目前所在的站內
  // 路徑，讓登入／MFA 完成後可以導回原本要去的頁面，而不是像原本那樣完全不處理。
  it('clears the session and redirects to /login with the current path on a 401', () => {
    currentRoute.value = { name: 'products', fullPath: '/products/123' }

    handleGlobalApiError(new ApiError('Unauthorized', { status: 401, code: 'authentication_required' }))

    expect(mockPush).toHaveBeenCalledWith({ name: 'login', query: { redirect: '/products/123' } })
  })

  it('does not redirect again when already on the login page', () => {
    currentRoute.value = { name: 'login', fullPath: '/login' }

    handleGlobalApiError(new ApiError('Unauthorized', { status: 401, code: 'authentication_required' }))

    expect(mockPush).not.toHaveBeenCalled()
  })

  it('redirects to /forbidden on a 403', () => {
    handleGlobalApiError(new ApiError('Forbidden', { status: 403, code: 'authorization_forbidden' }))

    expect(mockPush).toHaveBeenCalledWith('/forbidden')
  })

  it('does not redirect for other statuses (e.g. a 409 concurrency conflict handled inline)', () => {
    handleGlobalApiError(new ApiError('Conflict', { status: 409, code: 'concurrency_conflict' }))

    expect(mockPush).not.toHaveBeenCalled()
  })
})
