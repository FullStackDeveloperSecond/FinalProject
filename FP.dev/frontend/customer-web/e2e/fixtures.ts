import { expect, request, test as base } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'

type CleanupAction = () => Promise<void>

type DoSelectFixtures = {
  api: APIRequestContext
  loginAsMember: () => Promise<void>
  registerCleanup: (action: CleanupAction) => void
  seed: {
    adminEmail: string
    adminPassword: string
    memberEmail: string
    memberPassword: string
    productPublicId: string
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
    })
  },
})

export { expect } from '@playwright/test'
