import { createRouter, createWebHistory } from 'vue-router'
import { HttpStatusPage } from '@doselect/web-shared/components'
import { useSessionStore } from '../stores/session'

declare module 'vue-router' {
  interface RouteMeta {
    requiresAuth?: boolean
    guestOnly?: boolean
  }
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../pages/HomePage.vue'),
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('../pages/RegisterPage.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('../pages/LoginPage.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/verify-email',
      name: 'verify-email',
      component: () => import('../pages/VerifyEmailPage.vue'),
    },
    {
      path: '/forgot-password',
      name: 'forgot-password',
      component: () => import('../pages/ForgotPasswordPage.vue'),
      meta: { guestOnly: true },
    },
    {
      path: '/reset-password',
      name: 'reset-password',
      component: () => import('../pages/ResetPasswordPage.vue'),
    },
    {
      path: '/support',
      name: 'support-home',
      component: () => import('../pages/support/SupportHomePage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/support/tickets',
      name: 'support-ticket-list',
      component: () => import('../pages/support/SupportTicketListPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/support/tickets/new',
      name: 'support-ticket-new',
      component: () => import('../pages/support/SupportTicketNewPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/support/tickets/:ticketId',
      name: 'support-ticket-detail',
      component: () => import('../pages/support/SupportTicketDetailPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/products',
      name: 'products',
      component: () => import('../pages/ProductsPage.vue'),
    },
    {
      path: '/ai-search',
      name: 'ai-product-search',
      component: () => import('../pages/AiProductSearchPage.vue'),
    },
    {
      path: '/products/:productId',
      name: 'product-detail',
      component: () => import('../pages/ProductDetailPage.vue'),
      props: true,
    },
    {
      path: '/orders/:orderId',
      name: 'order-detail',
      component: () => import('../features/orders/OrderDetailPage.vue'),
    },
    {
      path: '/orders/:orderId/payment',
      name: 'order-payment',
      component: () => import('../features/payments/PaymentPage.vue'),
    },
    {
      path: '/guest-orders/access',
      name: 'guest-order-access',
      component: () => import('../features/orders/GuestOrderAccessPage.vue'),
    },
    {
      path: '/guest-orders/verify',
      name: 'guest-order-verify',
      component: () => import('../features/orders/GuestOrderVerifyPage.vue'),
    },
    {
      path: '/orders/:orderId/returns/new',
      name: 'return-new',
      component: () => import('../pages/returns/ReturnNewPage.vue'),
    },
    {
      path: '/returns/:returnId',
      name: 'return-detail',
      component: () => import('../pages/returns/ReturnDetailPage.vue'),
    },
    {
      path: '/account/reviews',
      name: 'my-reviews',
      component: () => import('../pages/reviews/MyReviewsPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/cart',
      name: 'cart',
      component: () => import('../pages/CartPage.vue'),
    },
    {
      // 組長 PR #35 round-3 review, P2-3: a saved build-list is inherently a member resource (it
      // lives on the account, not a guest-local draft) — the list and its detail page must require
      // login the same as /account/orders or /account/addresses do. /builds/new (a guest draft
      // that's only saved to an account once the shopper logs in — see NewBuildPage.vue's own
      // pending-resume flow) and /builds/shared/:shareToken (an intentionally public read-only
      // link) are correctly left open below.
      path: '/account/builds',
      name: 'build-lists',
      component: () => import('../pages/BuildListsPage.vue'),
      meta: { requiresAuth: true },
    },
    {
      path: '/builds/new',
      name: 'build-new',
      component: () => import('../pages/NewBuildPage.vue'),
    },
    {
      path: '/builds/shared/:shareToken',
      name: 'build-shared',
      component: () => import('../pages/SharedBuildPage.vue'),
      props: true,
    },
    {
      path: '/builds/:buildId',
      name: 'build-detail',
      component: () => import('../pages/BuildDetailPage.vue'),
      props: true,
      meta: { requiresAuth: true },
    },
    {
      path: '/unauthorized',
      name: 'unauthorized',
      component: HttpStatusPage,
      props: { status: 401, homeHref: '/' },
    },
    {
      path: '/forbidden',
      name: 'forbidden',
      component: HttpStatusPage,
      props: { status: 403, homeHref: '/' },
    },
    {
      path: '/error',
      name: 'server-error',
      component: HttpStatusPage,
      props: { status: 500, homeHref: '/' },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: HttpStatusPage,
      props: { status: 404, homeHref: '/' },
    },
  ],
})

router.beforeEach(async (to) => {
  if (!to.meta.requiresAuth && !to.meta.guestOnly) {
    return true
  }

  const session = useSessionStore()
  // 組長 PR #29 round 7 review, P1: 'error' (a failed refresh — see session.ts's isIdentityConfirmed
  // remarks) is now a distinct state from a confirmed 'anonymous'. Retry the refresh here as well
  // as for 'loading', so one transient Session API failure doesn't bounce a member who is actually
  // still signed in straight to /login on their next navigation. Still fails closed: if the retry
  // also fails, isAuthenticated stays false and the redirect below happens anyway.
  if (!session.isIdentityConfirmed) {
    await session.refresh()
  }

  if (to.meta.requiresAuth && !session.isAuthenticated) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.guestOnly && session.isAuthenticated) {
    return { name: 'home' }
  }

  return true
})

export default router
