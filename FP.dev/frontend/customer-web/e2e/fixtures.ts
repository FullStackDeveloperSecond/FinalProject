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
    // H-R03 的 admin-chromium 測試在 CI 對整套測試共用一顆資料庫，若沿用上面 adminEmail
    // 這顆「唯一」管理員帳號，誰先完成 TOTP 綁定，其餘測試看到的畫面就會從
    // /admin/login/enroll 變成 /admin/login/verify（alex PR #117 review P1）。這兩個帳號
    // 由後端種子（MinimalDevelopmentDataSeeder.EnsurePreEnrolledAdminAsync）直接種成
    // 「已知秘鑰、已完成綁定」狀態，H-R03 兩支測試各用一個、完全跳過 enroll 流程。
    adminHr03PrimaryEmail: string
    adminHr03SecondaryEmail: string
    adminHr03TotpSecret: string
    memberEmail: string
    memberPassword: string
    productPublicId: string
    skuPublicId: string
    coreTransactionGuestCartKey: string
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
      adminHr03PrimaryEmail: 'admin-h-r03-primary@doselect.local',
      adminHr03SecondaryEmail: 'admin-h-r03-secondary@doselect.local',
      // 跟 adminPassword 同一套慣例（AUTO-DEC-006）：不寫死在原始碼，值只由
      // scripts/test-customer-e2e.ps1 以 Seed__AdminHr03TotpSecret 環境變數注入，跟後端種子
      // （EnsurePreEnrolledAdminAsync）讀的是同一把秘鑰。
      adminHr03TotpSecret: process.env.Seed__AdminHr03TotpSecret ?? '',
      memberEmail: 'member@doselect.local',
      memberPassword: process.env.Seed__MemberPassword ?? '',
      productPublicId: '5940b1db-3c83-4db0-b285-9777616d11b1',
      skuPublicId: '719dfd4a-77f0-4887-b3bf-239263d4ee1f',
      coreTransactionGuestCartKey: 'e2e-core-transaction-guest-cart-key-0001',
    })
  },
})

export { expect } from '@playwright/test'
