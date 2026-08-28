import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, ref, h } from 'vue'

const mockCreateBuildShare = vi.fn()
const mockRevokeBuildShare = vi.fn()

vi.mock('./api', () => ({
  listBuildLists: vi.fn(),
  getBuildList: vi.fn(),
  createBuildList: vi.fn(),
  updateBuildList: vi.fn(),
  deleteBuildList: vi.fn(),
  createBuildShare: (...args: unknown[]) => mockCreateBuildShare(...args),
  revokeBuildShare: (...args: unknown[]) => mockRevokeBuildShare(...args),
  getSharedBuild: vi.fn(),
  addBuildToCart: vi.fn(),
  checkCompatibility: vi.fn(),
}))

beforeEach(() => {
  vi.clearAllMocks()
})

/**
 * 組長 PR #35 round-5 review, P3: 直接以 `invalidateQueries` spy 驗證 query key，而不是從 API
 * 呼叫參數間接推論——後者無法分辨「送出 A 但 invalidate B」這個 bug，因為 API 參數本來就會是 A。
 *
 * 這裡刻意模擬真實情境：composable 綁定的是一個「會變動的」getter（等同 route 參數），送出 mutation
 * 之後把它換成另一個 id，再讓 mutation 完成。修正前 `onSuccess` 會重新讀 getter 拿到新 id；修正後
 * 只認 mutation variables。
 */
async function mountWithShareComposables() {
  const { useCreateBuildShare, useRevokeBuildShare } = await import('./useBuilds')
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue(undefined)
  const currentId = ref('build-A')

  const harness = defineComponent({
    setup(_, { expose }) {
      const create = useCreateBuildShare(() => currentId.value)
      const revoke = useRevokeBuildShare(() => currentId.value)
      expose({ create, revoke })
      return () => h('div')
    },
  })

  const wrapper = mount(harness, {
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })

  return { wrapper, invalidateSpy, currentId }
}

describe('useCreateBuildShare / useRevokeBuildShare — invalidation targets the submitted id', () => {
  it('invalidates the detail query of the build the share was actually created for, not the one now bound', async () => {
    let resolveCreate: ((value: unknown) => void) | undefined
    mockCreateBuildShare.mockImplementation(() => new Promise((resolve) => { resolveCreate = resolve }))

    const { wrapper, invalidateSpy, currentId } = await mountWithShareComposables()
    const vm = wrapper.vm as unknown as { create: { mutateAsync: (id?: string) => Promise<unknown> } }

    const pending = vm.create.mutateAsync('build-A')
    await vi.waitFor(() => expect(mockCreateBuildShare).toHaveBeenCalledWith('build-A'))

    // 使用者切到另一份清單：composable 綁定的 getter 現在會回傳 build-B。
    currentId.value = 'build-B'

    resolveCreate?.({ token: 't', url: 'https://example.test/s/t', expiresAtUtc: new Date().toISOString() })
    await pending

    expect(invalidateSpy).toHaveBeenCalledTimes(1)
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['build-lists', 'detail', 'build-A'] })
  })

  it('invalidates the detail query of the build the share was actually revoked for, not the one now bound', async () => {
    let resolveRevoke: ((value: unknown) => void) | undefined
    mockRevokeBuildShare.mockImplementation(() => new Promise((resolve) => { resolveRevoke = resolve }))

    const { wrapper, invalidateSpy, currentId } = await mountWithShareComposables()
    const vm = wrapper.vm as unknown as { revoke: { mutateAsync: (id?: string) => Promise<unknown> } }

    const pending = vm.revoke.mutateAsync('build-A')
    await vi.waitFor(() => expect(mockRevokeBuildShare).toHaveBeenCalledWith('build-A'))

    currentId.value = 'build-B'

    resolveRevoke?.(undefined)
    await pending

    expect(invalidateSpy).toHaveBeenCalledTimes(1)
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['build-lists', 'detail', 'build-A'] })
  })

  it('falls back to the bound getter when no explicit id is passed', async () => {
    mockCreateBuildShare.mockResolvedValue({ token: 't', url: 'u', expiresAtUtc: new Date().toISOString() })

    const { wrapper, invalidateSpy } = await mountWithShareComposables()
    const vm = wrapper.vm as unknown as { create: { mutateAsync: (id?: string) => Promise<unknown> } }

    await vm.create.mutateAsync()

    expect(mockCreateBuildShare).toHaveBeenCalledWith('build-A')
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['build-lists', 'detail', 'build-A'] })
  })
})
