import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, expectTypeOf, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import type { useCreateBuildShare, useRevokeBuildShare } from './useBuilds'

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
 * 組長 PR #35 round-5 review, P3: assert the invalidated query key with an `invalidateQueries` spy
 * rather than inferring it from the API call arguments — the latter cannot tell "submitted A but
 * invalidated B" apart, because the API argument is A either way.
 *
 * Round 4's composables bound a `MaybeRefOrGetter` and re-read it in `onSuccess`, so the scenario
 * worth covering was "navigate away mid-flight". The id is now a required mutation variable and
 * nothing is bound at all, so that scenario is structurally impossible. What remains reachable —
 * and what these tests cover — is two overlapping mutations completing out of order: each must
 * invalidate the id it was itself given. That also rules out the shared-mutable-variable shape
 * 組長 warned about, which would let the later call overwrite the earlier one's target.
 */
async function mountWithShareComposables() {
  const { useCreateBuildShare, useRevokeBuildShare } = await import('./useBuilds')
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue(undefined)

  let create!: ReturnType<typeof useCreateBuildShare>
  let revoke!: ReturnType<typeof useRevokeBuildShare>

  const harness = defineComponent({
    setup() {
      create = useCreateBuildShare()
      revoke = useRevokeBuildShare()
      return () => h('div')
    },
  })

  const wrapper = mount(harness, {
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })

  return { wrapper, invalidateSpy, create, revoke }
}

/** Hands back one deferred promise per call, keyed by the id the mutation was invoked with. */
function deferByArgument(mock: ReturnType<typeof vi.fn>) {
  const resolvers = new Map<string, (value: unknown) => void>()
  mock.mockImplementation((publicId: string) =>
    new Promise((resolve) => { resolvers.set(publicId, resolve) }))
  return {
    resolve(publicId: string, value: unknown) {
      const resolver = resolvers.get(publicId)
      if (!resolver) {
        throw new Error(`no pending call for ${publicId}`)
      }
      resolver(value)
    },
  }
}

describe('useCreateBuildShare / useRevokeBuildShare — invalidation targets the submitted id', () => {
  it('invalidates each build the share was actually created for when two calls settle out of order', async () => {
    const deferred = deferByArgument(mockCreateBuildShare)

    const { invalidateSpy, create } = await mountWithShareComposables()

    const pendingA = create.mutateAsync('build-A')
    await vi.waitFor(() => expect(mockCreateBuildShare).toHaveBeenCalledWith('build-A'))
    const pendingB = create.mutateAsync('build-B')
    await vi.waitFor(() => expect(mockCreateBuildShare).toHaveBeenCalledWith('build-B'))

    // The later call finishes first; the earlier one must still invalidate its own build.
    deferred.resolve('build-B', { token: 'tb', url: 'https://example.test/s/tb', expiresAtUtc: new Date().toISOString() })
    await pendingB
    deferred.resolve('build-A', { token: 'ta', url: 'https://example.test/s/ta', expiresAtUtc: new Date().toISOString() })
    await pendingA

    expect(invalidateSpy).toHaveBeenCalledTimes(2)
    expect(invalidateSpy).toHaveBeenNthCalledWith(1, { queryKey: ['build-lists', 'detail', 'build-B'] })
    expect(invalidateSpy).toHaveBeenNthCalledWith(2, { queryKey: ['build-lists', 'detail', 'build-A'] })
  })

  it('invalidates each build the share was actually revoked for when two calls settle out of order', async () => {
    const deferred = deferByArgument(mockRevokeBuildShare)

    const { invalidateSpy, revoke } = await mountWithShareComposables()

    const pendingA = revoke.mutateAsync('build-A')
    await vi.waitFor(() => expect(mockRevokeBuildShare).toHaveBeenCalledWith('build-A'))
    const pendingB = revoke.mutateAsync('build-B')
    await vi.waitFor(() => expect(mockRevokeBuildShare).toHaveBeenCalledWith('build-B'))

    deferred.resolve('build-B', undefined)
    await pendingB
    deferred.resolve('build-A', undefined)
    await pendingA

    expect(invalidateSpy).toHaveBeenCalledTimes(2)
    expect(invalidateSpy).toHaveBeenNthCalledWith(1, { queryKey: ['build-lists', 'detail', 'build-B'] })
    expect(invalidateSpy).toHaveBeenNthCalledWith(2, { queryKey: ['build-lists', 'detail', 'build-A'] })
  })

  /**
   * 組長 PR #35 round-5 review, P3: the no-argument fallback is gone, so the guard is that it can no
   * longer be written. A no-argument call would still *run* (the mutation fn would simply forward
   * `undefined`), so there is nothing meaningful to assert at runtime — the contract is the type.
   * `expectTypeOf` fails at compile time, and `src/**\/*.ts` is inside `tsconfig.app.json`'s
   * `include`, so `npm run typecheck` (`vue-tsc -b`) enforces this alongside `vitest run`.
   */
  it('requires an explicit id — the mutation variable is a plain string, not an optional one', () => {
    expectTypeOf<Parameters<ReturnType<typeof useCreateBuildShare>['mutateAsync']>[0]>()
      .toEqualTypeOf<string>()
    expectTypeOf<Parameters<ReturnType<typeof useRevokeBuildShare>['mutateAsync']>[0]>()
      .toEqualTypeOf<string>()
  })
})
