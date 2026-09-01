import { randomBytes, randomUUID } from 'node:crypto'
import { expect, request, test as base } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'

type CleanupAction = () => Promise<void>

type GuestOrder = {
  orderPublicId: string
  orderNumber: string
  email: string
}

type DoSelectFixtures = {
  api: APIRequestContext
  loginAsMember: () => Promise<void>
  registerCleanup: (action: CleanupAction) => void
  createGuestOrder: (overrides?: { email?: string }) => Promise<GuestOrder>
  seed: {
    adminEmail: string
    adminPassword: string
    memberEmail: string
    memberPassword: string
    productPublicId: string
    skuPublicId: string
    guestOrderAccessPepper: string
  }
}

export const test = base.extend<DoSelectFixtures>({
  // Playwright requires fixture dependency destructuring even when this fixture has no dependency.
  // eslint-disable-next-line no-empty-pattern
  api: async ({}, use, testInfo) => {
    const correlationId = `e2e-${testInfo.testId}`.replace(/[^a-zA-Z0-9._-]/g, '-').slice(0, 64)
    const api = await request.newContext({
      baseURL: process.env.E2E_API_BASE_URL ?? 'http://127.0.0.1:5126',
      extraHTTPHeaders: {
        'X-Correlation-ID': correlationId,
      },
    })

    const readiness = await api.get('/health/ready')
    expect(readiness.ok(), 'API readiness must pass before an E2E journey starts').toBe(true)

    await use(api)
    await api.dispose()
  },
  loginAsMember: async ({ page, seed }, use) => {
    await use(async () => {
      if (!seed.memberPassword) {
        throw new Error('Seed__MemberPassword is required for an authenticated member E2E journey.')
      }

      await page.goto('/login')
      await page.getByRole('textbox', { name: '電子郵件' }).fill(seed.memberEmail)
      await page.getByRole('textbox', { name: '密碼', exact: true }).fill(seed.memberPassword)
      await page.getByRole('button', { name: '登入' }).click()
      await expect(page).toHaveURL(/\/$/)
    })
  },
  // eslint-disable-next-line no-empty-pattern
  registerCleanup: async ({}, use) => {
    const actions: CleanupAction[] = []
    await use((action) => actions.push(action))

    const failures: unknown[] = []
    for (const action of actions.reverse()) {
      try {
        await action()
      } catch (error) {
        failures.push(error)
      }
    }

    if (failures.length > 0) {
      throw new AggregateError(failures, 'One or more E2E cleanup actions failed.')
    }
  },
  // eslint-disable-next-line no-empty-pattern
  seed: async ({}, use) => {
    await use({
      adminEmail: 'admin@doselect.local',
      adminPassword: process.env.Seed__AdminPassword ?? '',
      memberEmail: 'member@doselect.local',
      memberPassword: process.env.Seed__MemberPassword ?? '',
      productPublicId: '5940b1db-3c83-4db0-b285-9777616d11b1',
      skuPublicId: '719dfd4a-77f0-4887-b3bf-239263d4ee1f',
      guestOrderAccessPepper: process.env.GuestOrderAccess__Pepper ?? '',
    })
  },
  // Drives the real Cart → Checkout HTTP APIs as a guest (no Checkout UI exists yet — that's a
  // separate, later work package) to produce a real, pendingPayment (so `cancel` is available),
  // cancellable order for the Guest Order Access → Order detail journey to exercise. Every
  // non-GET call goes through the same GlobalAntiforgeryFilter as the browser does; the guest
  // cart identity is a client-generated `X-DoSelect-Guest-Cart-Key` header (32+ chars), reused
  // across every call in this sequence.
  createGuestOrder: async ({ api, seed }, use) => {
    await use(async (overrides) => {
      const email = overrides?.email ?? `e2e-guest-${randomUUID()}@example.test`
      const guestCartKey = randomBytes(24).toString('hex')

      const antiforgery = await api.get('/api/v1/security/antiforgery-token', {
        headers: { 'X-DoSelect-Client': 'member' },
      })
      expect(antiforgery.ok(), 'Fetching an antiforgery token must succeed').toBe(true)
      const { requestToken } = await antiforgery.json() as { requestToken: string }

      const writeHeaders = {
        'X-XSRF-TOKEN': requestToken,
        'X-DoSelect-Client': 'member',
        'X-DoSelect-Guest-Cart-Key': guestCartKey,
      }

      const addItem = await api.post('/api/v1/cart/items', {
        headers: writeHeaders,
        data: { skuPublicId: seed.skuPublicId, quantity: 1, cartRowVersion: null },
      })
      expect(addItem.ok(), 'Adding the demo SKU to a guest cart must succeed').toBe(true)
      const cart = await addItem.json() as { publicId: string, rowVersion: string }

      const policyVersions = await api.get('/api/v1/checkout/policy-versions')
      expect(policyVersions.ok(), 'Fetching current checkout policy versions must succeed').toBe(true)
      const policies = await policyVersions.json() as { terms: number, return: number, privacy: number }

      const stores = await api.get('/api/v1/convenience-stores', {
        params: { providerCode: '7-11', pageNumber: '1', pageSize: '1' },
      })
      expect(stores.ok(), 'Listing seeded convenience stores must succeed').toBe(true)
      const storesBody = await stores.json() as { items: Array<{ publicId: string }> }
      const storePublicId = storesBody.items[0]?.publicId
      expect(storePublicId, 'At least one demo convenience store must be seeded').toBeTruthy()

      const checkout = await api.post('/api/v1/orders', {
        headers: { ...writeHeaders, 'Idempotency-Key': `e2e-checkout-${randomUUID()}` },
        data: {
          cartPublicId: cart.publicId,
          cartRowVersion: cart.rowVersion,
          buyer: { email, name: 'E2E Guest', phone: '0912345678' },
          shipping: { methodCode: 'StorePickup', address: null, storePublicId, deliveryNote: null },
          paymentMethod: 'creditCard',
          couponCode: null,
          invoice: {
            type: 'simulated',
            buyerType: 'personal',
            carrierType: null,
            carrierValue: null,
            companyTaxId: null,
            companyName: null,
          },
          acceptPolicyVersions: policies,
        },
      })
      expect(checkout.ok(), `Guest checkout must succeed: ${checkout.status()} ${await checkout.text()}`).toBe(true)
      const order = await checkout.json() as { publicId: string, orderNumber: string }

      return { orderPublicId: order.publicId, orderNumber: order.orderNumber, email }
    })
  },
})

export { expect } from '@playwright/test'
