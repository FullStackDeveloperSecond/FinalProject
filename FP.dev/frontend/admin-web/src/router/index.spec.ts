import { describe, expect, it } from 'vitest'
import router from './index'

describe('admin router foundation', () => {
  it.each([
    ['/support', 'support-sla-queue'],
    ['/support/tickets/018f2e6a-0000-7000-8000-000000000001', 'support-ticket-detail'],
  ])('registers the admin support route %s', (path, name) => {
    const resolved = router.resolve(path)

    expect(resolved.name).toBe(name)
    expect(resolved.matched).toHaveLength(1)
  })

  it('catches unknown routes', async () => {
    await router.push('/missing-page')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('not-found')
  })
})
