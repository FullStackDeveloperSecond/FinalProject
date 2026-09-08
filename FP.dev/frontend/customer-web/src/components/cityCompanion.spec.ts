import { beforeEach, expect, it, vi } from 'vitest'
import { clearRecentProducts, loadRecentProducts, recentProducts, rememberProduct, celebrateCart, companionMessage } from './cityCompanion'
beforeEach(() => { localStorage.clear(); clearRecentProducts() })
it('remembers four products, moves repeat visits to the front, restores and clears', () => {
 for (let i = 0; i < 5; i++) rememberProduct({ id: `product-${i}`, name: `Product ${i}` })
 rememberProduct({ id: 'product-2', name: 'Product 2' })
 expect(recentProducts.value.map(item => item.id)).toEqual(['product-2', 'product-4', 'product-3', 'product-1'])
 recentProducts.value = []
 loadRecentProducts()
 expect(recentProducts.value).toHaveLength(4)
 clearRecentProducts(); loadRecentProducts()
 expect(recentProducts.value).toHaveLength(0)
})
it('ignores invalid stored destinations and malformed storage', () => {
 localStorage.setItem('doselect-city-recent-products', JSON.stringify([{id:'../../account', name:'bad'}, {id:'good', name:'Valid'}]))
 loadRecentProducts()
 expect(recentProducts.value).toEqual([{id:'good', name:'Valid'}])
 localStorage.setItem('doselect-city-recent-products', '{')
 loadRecentProducts()
 expect(recentProducts.value).toEqual([])
})
it('expires success feedback and restarts its timeout on the next success', async () => {
 vi.useFakeTimers()
 try {
  celebrateCart(); await vi.advanceTimersByTimeAsync(4000); celebrateCart()
  await vi.advanceTimersByTimeAsync(2000); expect(companionMessage.value).not.toBe('')
  await vi.advanceTimersByTimeAsync(3000); expect(companionMessage.value).toBe('')
 } finally { vi.useRealTimers() }
})
