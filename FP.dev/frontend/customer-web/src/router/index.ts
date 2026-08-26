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
      path: '/cart',
      name: 'cart',
      component: () => import('../pages/CartPage.vue'),
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
  if (session.status === 'loading') {
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
