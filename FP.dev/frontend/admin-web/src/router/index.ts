import { createRouter, createWebHistory } from 'vue-router'
import { HttpStatusPage } from '@doselect/web-shared/components'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: () => import('../pages/HomePage.vue'),
    },
    {
      path: '/support',
      name: 'support-sla-queue',
      component: () => import('../pages/support/SupportSlaQueuePage.vue'),
    },
    {
      path: '/support/tickets/:ticketId',
      name: 'support-ticket-detail',
      component: () => import('../pages/support/SupportTicketDetailPage.vue'),
    },
    {
      // Confirmed Route contract (M功能桌面UI與Route規格.md A-08): one page manages
      // brand／category／tag together, not three separate routes.
      path: '/catalog/lookups',
      name: 'catalog-lookups',
      component: () => import('../pages/CatalogLookupsPage.vue'),
    },
    {
      path: '/products',
      name: 'products',
      component: () => import('../pages/ProductsPage.vue'),
    },
    {
      path: '/products/new',
      name: 'product-new',
      component: () => import('../pages/ProductEditPage.vue'),
    },
    {
      // Confirmed Route contract (M功能桌面UI與Route規格.md A-06): /admin/products/:productId,
      // not /admin/products/:id/edit.
      path: '/products/:productId',
      name: 'product-edit',
      component: () => import('../pages/ProductEditPage.vue'),
      props: true,
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

export default router
