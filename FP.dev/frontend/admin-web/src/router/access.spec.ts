import { describe, expect, it } from 'vitest'
import { canAccessAdminPage } from './access'

describe('admin navigation access', () => {
  it.each([
    ['CustomerService', '/support', '/products'],
    ['CatalogManager', '/products/import', '/inventory'],
    ['InventoryManager', '/inventory/imports', '/shipping/batches'],
    ['FinanceManager', '/refunds', '/orders'],
    ['OrderManager', '/orders', '/refunds'],
    ['MarketingAnalyst', '/reports/sales-overview', '/invoices'],
  ])('shows %s only the appropriate entries', (role, allowed, denied) => {
    expect(canAccessAdminPage(allowed, [role], true)).toBe(true)
    expect(canAccessAdminPage(denied, [role], true)).toBe(false)
    expect(canAccessAdminPage(allowed, [role], false)).toBe(false)
  })

  it('supports multi-role users, guarded details, and rejects unknown paths', () => {
    expect(canAccessAdminPage('/refunds/abc', ['CustomerService', 'FinanceManager'], true)).toBe(true)
    expect(canAccessAdminPage('/orders/abc', ['SuperAdmin'], true)).toBe(true)
    expect(canAccessAdminPage('/unknown', ['SuperAdmin'], true)).toBe(false)
    expect(canAccessAdminPage('/products', [], true)).toBe(false)
  })
})
