import { createRouter, createWebHistory } from 'vue-router'
import { HttpStatusPage } from '@doselect/web-shared/components'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'
import { isOperationalReportKey } from '../features/operationalReports/types'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    guestOnly?: boolean
    requiresChallenge?: boolean
    requiredRoles?: string[]
  }
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../pages/HomePage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('../features/auth/pages/LoginPage.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/login/verify',
      name: 'login-verify',
      component: () => import('../features/auth/pages/TotpVerifyPage.vue'),
      meta: { guestOnly: true, requiresChallenge: true },
    },
    {
      path: '/login/enroll',
      name: 'login-enroll',
      component: () => import('../features/auth/pages/TotpEnrollPage.vue'),
      meta: { guestOnly: true, requiresChallenge: true },
    },
    {
      path: '/security/totp-rebind',
      name: 'totp-rebind',
      component: () => import('../features/auth/pages/TotpRebindPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/support',
      name: 'support-sla-queue',
      component: () => import('../pages/support/SupportSlaQueuePage.vue'),
      meta: {
        requiresAuth: true,
        requiredRoles: ['CustomerService', 'CustomerServiceSupervisor', 'SuperAdmin'],
      },
    },
    {
      path: '/support/tickets/:ticketId',
      name: 'support-ticket-detail',
      component: () => import('../pages/support/SupportTicketDetailPage.vue'),
      meta: {
        requiresAuth: true,
        requiredRoles: ['CustomerService', 'CustomerServiceSupervisor', 'SuperAdmin'],
      },
    },
    {
      // A-24 案件工作台 (M功能桌面UI與Route規格.md): documented as /admin/cases — this app is
      // already mounted under the /admin base path (see createWebHistory(BASE_URL) above), so
      // the route is defined here without that prefix, matching every other route in this file.
      path: '/cases',
      name: 'case-workbench',
      component: () => import('../pages/case-workbench/CaseWorkbenchPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      // Confirmed Route contract (M功能桌面UI與Route規格.md A-08): one page manages
      // brand／category／tag together, not three separate routes.
      path: '/catalog/lookups',
      name: 'catalog-lookups',
      component: () => import('../pages/CatalogLookupsPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['CatalogManager', 'SuperAdmin'] },
    },
    {
      path: '/products',
      name: 'products',
      component: () => import('../pages/ProductsPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['CatalogManager', 'SuperAdmin'] },
    },
    {
      // M功能桌面UI與Route規格.md A-07。Policy 對齊後端的 CatalogImport.*（CatalogManager／
      // SuperAdmin）。放在 /products/:productId 之前，否則 'import' 會被當成 productId。
      path: '/products/import',
      name: 'product-import',
      component: () => import('../pages/ProductImportPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['CatalogManager', 'SuperAdmin'] },
    },
    {
      path: '/products/new',
      name: 'product-new',
      component: () => import('../pages/ProductEditPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['CatalogManager', 'SuperAdmin'] },
    },
    {
      // Confirmed Route contract (M功能桌面UI與Route規格.md A-06): /admin/products/:productId,
      // not /admin/products/:id/edit.
      path: '/products/:productId',
      name: 'product-edit',
      component: () => import('../pages/ProductEditPage.vue'),
      props: true,
      meta: { requiresAuth: true, requiredRoles: ['CatalogManager', 'SuperAdmin'] },
    },
    {
      // A-23：Coupon.Manage（FinanceManager／MarketingAnalyst／SuperAdmin）。
      // 與 Invoice.Manage 只差多允許 MarketingAnalyst（DEC-P284）。
      path: '/coupons',
      name: 'admin-coupons',
      component: () => import('../pages/coupons/AdminCouponsPage.vue'),
      meta: {
        requiresAuth: true,
        requiredRoles: ['FinanceManager', 'MarketingAnalyst', 'SuperAdmin'],
      },
    },
    {
      path: '/returns',
      name: 'admin-return-queue',
      component: () => import('../pages/returns/AdminReturnQueuePage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['OrderManager', 'SuperAdmin'] },
    },
    {
      // A-21：退款清單。後端 Refund.Execute policy 同時要求角色與 MFA；
      // 前端角色 guard 只做導覽體驗，不取代後端授權。
      path: '/refunds',
      name: 'admin-refund-list',
      component: () => import('../pages/refunds/AdminRefundsPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['FinanceManager', 'SuperAdmin'] },
    },
    {
      // A-22：可信分攤、核准上限與具冪等鍵的退款執行。
      path: '/refunds/:refundId',
      name: 'admin-refund-detail',
      component: () => import('../pages/refunds/AdminRefundDetailPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['FinanceManager', 'SuperAdmin'] },
    },
    {
      path: '/invoices',
      name: 'admin-invoice-list',
      component: () => import('../pages/invoices/AdminInvoicesPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['FinanceManager', 'SuperAdmin'] },
    },
    {
      path: '/invoices/:invoiceId',
      name: 'admin-invoice-detail',
      component: () => import('../pages/invoices/AdminInvoiceDetailPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['FinanceManager', 'SuperAdmin'] },
    },
    {
      path: '/ai/usage',
      name: 'ai-usage',
      component: () => import('../pages/AiUsagePage.vue'),
      meta: {
        requiresAuth: true,
        requiredRoles: ['FinanceManager', 'CustomerServiceSupervisor', 'MarketingAnalyst', 'SuperAdmin'],
      },
    },
    {
      path: '/reviews',
      name: 'admin-review-queue',
      component: () => import('../pages/reviews/AdminReviewQueuePage.vue'),
      meta: {
        requiresAuth: true,
        requiredRoles: ['CustomerService', 'CustomerServiceSupervisor', 'SuperAdmin'],
      },
    },
    {
      path: '/reports/:reportKey',
      name: 'operational-report',
      component: () => import('../pages/OperationalReportPage.vue'),
      beforeEnter: (to) => isOperationalReportKey(to.params.reportKey)
        ? true
        : { name: 'not-found' },
      meta: {
        requiresAuth: true,
        requiredRoles: ['FinanceManager', 'MarketingAnalyst', 'SuperAdmin'],
      },
    },
    {
      path: '/returns/:returnId',
      name: 'admin-return-detail',
      component: () => import('../pages/returns/AdminReturnDetailPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['OrderManager', 'SuperAdmin'] },
    },
    {
      // 前台目前沒有登入流程（屬於 haru/feature/admin-membermanage 分支範圍，尚未合併），
      // 所以這裡先不掛 meta.requiresAuth／router guard；401/403 由頁面自己處理（比照
      // customer-web OrderDetailPage.vue 現有模式）。待該分支合併後可補上 Guard。
      path: '/orders',
      name: 'admin-order-list',
      component: () => import('../features/orders/pages/OrderListPage.vue'),
    },
    {
      path: '/orders/:publicId',
      name: 'admin-order-detail',
      component: () => import('../features/orders/pages/OrderDetailPage.vue'),
    },
    {
      // 組長 PR #35 review, item 6: official route is /admin/catalog/compatibility, not
      // /admin/compatibility — base: '/admin/' in vite.config.ts means this entry only needs
      // the /catalog/compatibility part, matching the existing /catalog/lookups sibling route.
      //
      // 組長 PR #35 round-3 review, P2-3: this route was missing route meta entirely — it never
      // required login at all, and CompatibilityRulesPage.vue's activation toggle is a
      // SuperAdmin-only backend operation (相容性規則後台設計.md: "規則整體啟停只允許
      // SuperAdmin") that any logged-in CatalogManager could still see and click on screen, even
      // though the backend Policy would ultimately reject it. Guarded the same way
      // /catalog/lookups already is; the page itself also hides the activation controls from a
      // non-SuperAdmin below (defense in depth, not a substitute for the backend Policy either).
      path: '/catalog/compatibility',
      name: 'compatibility-rules',
      component: () => import('../pages/CompatibilityRulesPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['CatalogManager', 'SuperAdmin'] },
    },
    {
      // M功能桌面UI與Route規格.md A-09 `/admin/catalog/specifications`；base: '/admin/' 已在
      // vite.config.ts 設定，所以這裡只寫 /catalog/specifications，與同層的 lookups、
      // compatibility 一致。Policy 與後端 specification-definitions 端點相同。
      path: '/catalog/specifications',
      name: 'specification-definitions',
      component: () => import('../pages/SpecificationDefinitionsPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['CatalogManager', 'SuperAdmin'] },
    },
    {
      // M功能桌面UI與Route規格.md A-11
      //
      // 組長 PR #35 round-3 review, P2-3 的同一個缺口：這兩條路由原本完全沒有 meta，而 guard 的
      // 第一行是「三個 meta 旗標都沒有就直接放行」，等於未登入也能打開後台庫存頁——它們是這份
      // router 裡唯一沒有 meta 的功能性路由。後端 AdminInventoryController 掛的是
      // [Authorize(Policy = InventoryManager)]，對應 InventoryManager 與 SuperAdmin
      // （SecurityServiceCollectionExtensions 的 AddAdminPolicy），前端比照對齊，避免使用者
      // 看得到頁面、按下去才被後端擋掉。前端 Guard 是體驗與最小揭露，不是後端 Policy 的替代品。
      path: '/inventory',
      name: 'inventory',
      component: () => import('../pages/InventoryPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['InventoryManager', 'SuperAdmin'] },
    },
    {
      // M功能桌面UI與Route規格.md A-12
      // M功能桌面UI與Route規格.md A-17／A-18。後端的 ShippingRead 允許 OrderManager／
      // CatalogManager／SuperAdmin 檢視，ShippingManage 的寫入只給 OrderManager／SuperAdmin
      // （UC-ADM-STORE-01：「CatalogManager 只有檢視權限」），所以門市頁的 route 放檢視角色、
      // 頁面內再隱藏寫入控制項；包裹限制整頁都是 Order Manage，route 就只放寫入角色。
      path: '/shipping/stores',
      name: 'shipping-stores',
      component: () => import('../pages/ShippingStoresPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['OrderManager', 'CatalogManager', 'SuperAdmin'] },
    },
    {
      path: '/shipping/package-limits',
      name: 'shipping-package-limits',
      component: () => import('../pages/ShippingPackageLimitsPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['OrderManager', 'SuperAdmin'] },
    },
    {
      // M功能桌面UI與Route規格.md A-13。後端是 InventoryAdjust.*（InventoryManager／SuperAdmin）。
      path: '/inventory/imports',
      name: 'inventory-imports',
      component: () => import('../pages/InventoryImportPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['InventoryManager', 'SuperAdmin'] },
    },
    {
      path: '/inventory/reservations',
      name: 'inventory-reservations',
      component: () => import('../pages/InventoryReservationsPage.vue'),
      meta: { requiresAuth: true, requiredRoles: ['InventoryManager', 'SuperAdmin'] },
    },
    {
      path: '/unauthorized',
      name: 'unauthorized',
      component: HttpStatusPage,
      props: { status: 401, homeHref: '/admin/' },
    },
    {
      path: '/forbidden',
      name: 'forbidden',
      component: HttpStatusPage,
      props: { status: 403, homeHref: '/admin/' },
    },
    {
      path: '/error',
      name: 'server-error',
      component: HttpStatusPage,
      props: { status: 500, homeHref: '/admin/' },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: HttpStatusPage,
      props: { status: 404, homeHref: '/admin/' },
    },
  ],
})

// 這是本 App 第一個 router guard：未登入導向 /login，已登入卻造訪登入頁則導回首頁。
// 前端這裡的判斷只是體驗優化，真正把關在後端 Policy（DoSelectPolicies.*）。
router.beforeEach(async (to) => {
  if (!to.meta.requiresAuth && !to.meta.guestOnly && !to.meta.requiresChallenge) {
    return true
  }

  const auth = useAdminAuthStore()
  if (auth.session === null) {
    await auth.fetchSession()
  }

  if (to.meta.requiresAuth && !auth.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.requiredRoles && auth.isAuthenticated) {
    const roles = auth.currentUser?.roles ?? []
    if (!to.meta.requiredRoles.some((role) => roles.includes(role))) {
      return { name: 'forbidden' }
    }
  }

  if (to.meta.guestOnly && auth.isAuthenticated && to.name !== 'login-enroll') {
    return { name: 'home' }
  }

  // challengePublicId 只存在 Pinia 記憶體（全站沒有任何 persistence plugin）。重新整理
  // /login/verify 或 /login/enroll 會讓它遺失；雖然 .DoSelect.AdminChallenge Cookie 本身
  // reload 後仍有效，但對前端 JS 是 httponly 不可讀，沒有辦法還原這個值。與其讓使用者卡在
  // 一個沒有 challenge 可用的死頁面（原本的 bug），直接導回登入頁重新開始。保留原本帶著的
  // redirect（如果有）——不這樣做，從深層連結進來、半路遺失 challenge 重新走一次登入的人，
  // 完成後會被導回首頁而不是原本要去的頁面（alex review 第三輪 P2#4）。
  if (to.meta.requiresChallenge && auth.challenge === null) {
    return { name: 'login', query: to.query.redirect ? { redirect: to.query.redirect } : {} }
  }

  return true
})

export default router
