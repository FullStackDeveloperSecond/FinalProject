import { describe, expect, it } from 'vitest'
import router from './index'

describe('admin router foundation', () => {
  it('catches unknown routes', async () => {
    await router.push('/missing-page')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('not-found')
  })
})
