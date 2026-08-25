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

  /** PR #24 review: confirmed Route contract (M功能桌面UI與Route規格.md A-06) is /admin/products/:productId, not /admin/products/:id/edit. */
  it('resolves /products/:productId to product-edit with the id as the productId prop', async () => {
    await router.push('/products/p1')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('product-edit')
    expect(router.currentRoute.value.params.productId).toBe('p1')
  })

  /** /products/new must still resolve to the create route, not be swallowed by the dynamic :productId segment. */
  it('resolves /products/new to product-new, not product-edit', async () => {
    await router.push('/products/new')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('product-new')
  })

  /** PR #24 review: confirmed Route contract (M功能桌面UI與Route規格.md A-08) is one combined /admin/catalog/lookups page, not separate /brands, /categories, /tags routes. */
  it('resolves /catalog/lookups to the combined lookups page', async () => {
    await router.push('/catalog/lookups')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('catalog-lookups')
  })

  it('no longer has standalone /brands, /categories, or /tags routes', async () => {
    await router.push('/brands')
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('not-found')

    await router.push('/categories')
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('not-found')

    await router.push('/tags')
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('not-found')
  })
})
