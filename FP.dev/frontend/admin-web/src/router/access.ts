/** Navigation and route guards share the same roles; the API remains authoritative. */
export const adminRouteRoles: Record<string, string[]> = {
  '/inventory/reconciliation-cases': ['InventoryManager', 'SuperAdmin'],
  '/orders': ['OrderManager', 'SuperAdmin'],
  '/orders/:publicId': ['OrderManager', 'SuperAdmin'],
  '/support': ['CustomerService', 'CustomerServiceSupervisor', 'SuperAdmin'],
  '/support/tickets/:ticketId': ['CustomerService', 'CustomerServiceSupervisor', 'SuperAdmin'],
  '/catalog/lookups': ['CatalogManager', 'SuperAdmin'],
  '/products': ['CatalogManager', 'SuperAdmin'],
  '/products/import': ['CatalogManager', 'SuperAdmin'],
  '/products/new': ['CatalogManager', 'SuperAdmin'],
  '/products/:productId': ['CatalogManager', 'SuperAdmin'],
  '/coupons': ['FinanceManager', 'MarketingAnalyst', 'SuperAdmin'],
  '/returns': ['OrderManager', 'SuperAdmin'],
  '/refunds': ['FinanceManager', 'SuperAdmin'],
  '/refunds/:refundId': ['FinanceManager', 'SuperAdmin'],
  '/invoices': ['FinanceManager', 'SuperAdmin'],
  '/invoices/:invoiceId': ['FinanceManager', 'SuperAdmin'],
  '/ai/usage': ['FinanceManager', 'CustomerServiceSupervisor', 'MarketingAnalyst', 'SuperAdmin'],
  '/reviews': ['CustomerService', 'CustomerServiceSupervisor', 'SuperAdmin'],
  '/reports/:reportKey': ['FinanceManager', 'MarketingAnalyst', 'SuperAdmin'],
  '/returns/:returnId': ['OrderManager', 'SuperAdmin'],
  '/catalog/compatibility': ['CatalogManager', 'SuperAdmin'],
  '/catalog/specifications': ['CatalogManager', 'SuperAdmin'],
  '/inventory': ['InventoryManager', 'SuperAdmin'],
  '/shipping/stores': ['OrderManager', 'CatalogManager', 'SuperAdmin'],
  '/shipping/package-limits': ['OrderManager', 'SuperAdmin'],
  '/inventory/imports': ['InventoryManager', 'SuperAdmin'],
  '/shipping/batches': ['OrderManager', 'SuperAdmin'],
  '/inventory/reservations': ['InventoryManager', 'SuperAdmin'],
}

export function canAccessAdminPage(path: string, roles: readonly string[], authenticated: boolean): boolean {
  if (!authenticated) return false
  const key = Object.keys(adminRouteRoles).find(pattern =>
    new RegExp('^' + pattern.replace(/:[^/]+/g, '[^/]+') + '$').test(path))
  if (key) return adminRouteRoles[key]!.some(role => roles.includes(role))
  return path === '/' || path === '/cases' || path === '/security/totp-rebind'
}
