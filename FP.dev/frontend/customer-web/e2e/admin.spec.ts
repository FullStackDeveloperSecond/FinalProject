import { expect, test } from './fixtures.js'
import { generateTotpCode } from './totp.js'

// M-01B: real Browser E2E against the seeded admin account, which starts with no TOTP secret
// enrolled (MinimalDevelopmentDataSeeder never calls SetTwoFactorEnabledAsync), so a fresh
// isolated E2E database always takes the enrollment path on first login. Everything below runs as
// one continuous journey — enroll → a second device's returning-admin TOTP verify → rebind
// (which revokes that other device's session) → lockout.
//
// AdminAuthController guards totp/verify, recovery-codes/use, totp/enroll/confirm and
// totp/rebind/begin|confirm with a shared 5-per-15-minutes account+IP challenge rate limit
// (AdminChallengeRateLimiter, RateLimitOptions.AdminChallengePermitLimit) — empirically, a 5th
// consuming call in one run gets rejected (admin_challenge_rate_limited), so this journey is
// deliberately kept to 4 such calls (enroll-confirm, one totp/verify, rebind/begin,
// rebind/confirm) and does not separately exercise recovery-code redemption or a second
// primary-session TOTP verify — both already share the same verify/confirm code paths this
// journey does cover. Recovery codes are still asserted to be issued by enrollment.
test('an admin enrolls TOTP, has another device verify with it, rebinds (revoking that device), and gets locked out', async ({
  page,
  seed,
  browser,
}) => {
  test.setTimeout(120_000)

  async function loginWithPassword(target: typeof page): Promise<void> {
    // A leading-slash goto('/login') resolves against the origin and drops the app's /admin/ base
    // path (see the other tests in this file) — must use the './' relative form instead.
    await target.goto('./login')
    await target.getByLabel('電子郵件').fill(seed.adminEmail)
    await target.getByLabel('密碼', { exact: true }).fill(seed.adminPassword)
    await target.getByRole('button', { name: '登入' }).click()
  }

  // 1. First login: no TOTP enrolled yet, so the challenge routes to the enrollment screen.
  await loginWithPassword(page)
  await expect(page).toHaveURL(/\/login\/enroll$/)
  await expect(page.getByRole('heading', { name: '綁定兩步驟驗證' })).toBeVisible()

  let secretKey = await page.locator('.totp-secret code').innerText()
  await page.getByLabel('請輸入 App 顯示的 6 位數驗證碼以確認綁定').fill(generateTotpCode(secretKey))
  await page.getByRole('button', { name: '確認綁定' }).click()

  await expect(page.getByRole('heading', { name: '請保存您的備援碼' })).toBeVisible()
  const recoveryCodes = await page.locator('.recovery-code-list li').allTextContents()
  expect(recoveryCodes.length).toBeGreaterThan(0)
  await page.getByLabel('我已抄下並妥善保存這些備援碼').check()
  await page.getByRole('button', { name: '完成，進入後台' }).click()
  await expect(page).toHaveURL(/\/$/)

  // 2. A second, independent "other device" session: same account, its own browser context/cookie,
  // authenticated via the returning-admin TOTP verify path (not enrollment, since it's already
  // enrolled from step 1).
  const otherDeviceContext = await browser.newContext()
  const otherDevicePage = await otherDeviceContext.newPage()
  try {
    await loginWithPassword(otherDevicePage)
    await expect(otherDevicePage).toHaveURL(/\/login\/verify$/)
    await expect(otherDevicePage.getByRole('heading', { name: '兩步驟驗證' })).toBeVisible()
    await otherDevicePage.getByLabel('驗證碼').fill(generateTotpCode(secretKey))
    await otherDevicePage.getByRole('button', { name: '驗證', exact: true }).click()
    await expect(otherDevicePage).toHaveURL(/\/$/)

    // 3. Rebind TOTP on the primary session (still authenticated since step 1) — this must revoke
    // every other device's session per TotpRebindPage/useAdminAuthStore.confirmRebind.
    await page.goto('./security/totp-rebind')
    // getByLabel would also match the step-up method radio button sharing this same label text —
    // scope to the textbox role to get only the fillable step-up code input.
    await page.getByRole('textbox', { name: '目前的 6 位數驗證碼' }).fill(generateTotpCode(secretKey))
    await page.getByRole('button', { name: '驗證並開始重新綁定' }).click()

    const newSecretKey = await page.locator('.totp-secret code').innerText()
    await page.getByLabel('請輸入新裝置 App 顯示的 6 位數驗證碼以確認').fill(generateTotpCode(newSecretKey))
    await page.getByRole('button', { name: '確認重新綁定' }).click()
    await expect(page.getByRole('heading', { name: '請保存您的新備援碼' })).toBeVisible()
    await page.getByLabel('我已抄下並妥善保存這些備援碼').check()
    await page.getByRole('button', { name: '完成' }).click()
    await expect(page).toHaveURL(/\/$/)
    secretKey = newSecretKey

    // 4. The other device's old cookie must now be rejected — its next navigation bounces to login.
    await otherDevicePage.goto('./')
    await expect(otherDevicePage).toHaveURL(/\/login/)
  } finally {
    await otherDeviceContext.close()
  }

  // 5. Five wrong passwords lock the account; the shared 5-attempt threshold (differentiated only
  // by lockout duration per AccountType) means the 5th wrong attempt itself already reports the
  // lockout, and a 6th attempt with the *correct* password is blocked identically. This uses
  // Identity's AccessFailedCount lockout, a separate mechanism from the challenge rate limiter
  // above, so it isn't constrained by that budget.
  await page.getByRole('button', { name: '登出' }).click()
  await expect(page).toHaveURL(/\/login$/)

  // AdminLoginUseCase.ExecuteAsync deliberately reports plain invalid_credentials for a locked
  // account too (see its comment: revealing account_locked from the public login response would
  // let an attacker enumerate which admin emails exist by brute-forcing until the message changes).
  // The real lockout only ever surfaces via the central audit log, never via this endpoint's
  // response — so, exactly like the member lockout test above, every attempt below expects the
  // same generic message, including the final one using the *correct* password.
  const invalidCredentials = page.getByText('帳號或密碼錯誤。')

  for (let attempt = 0; attempt < 5; attempt += 1) {
    await page.getByLabel('電子郵件').fill(seed.adminEmail)
    await page.getByLabel('密碼', { exact: true }).fill('DefinitelyWrongPassword9!')
    await page.getByRole('button', { name: '登入' }).click()
    await expect(invalidCredentials).toBeVisible()
  }

  await page.getByLabel('電子郵件').fill(seed.adminEmail)
  await page.getByLabel('密碼', { exact: true }).fill(seed.adminPassword)
  await page.getByRole('button', { name: '登入' }).click()
  await expect(invalidCredentials).toBeVisible()
  await expect(page).toHaveURL(/\/login$/)
})

test('an anonymous administrator is routed to the login page', async ({ page }) => {
  await page.goto('./')

  await expect(page).toHaveURL(/\/admin\/login\?redirect=\/$/)
  await expect(page.getByRole('heading', { level: 1, name: '管理員登入' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '電子郵件' })).toBeVisible()
  await expect(page.getByLabel('密碼')).toBeVisible()
  await expect(page.getByRole('button', { name: '登入' })).toBeVisible()
})

test('a finance administrator can see the reconciled after-sales totals', async ({ page }) => {
  await page.route('**/api/v1/admin/auth/session', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      json: {
        isAuthenticated: true,
        user: {
          publicId: '0199256e-8c40-7000-8000-000000000004',
          displayName: 'INT-04 Finance Admin',
          emailMasked: 'i***4@example.test',
          emailVerified: true,
          locale: 'zh-TW',
          roles: ['FinanceManager'],
        },
        expiresAtUtc: '2026-08-29T12:00:00Z',
        requiresTwoFactor: false,
      },
    })
  })

  await page.route('**/api/v1/admin/reports/sales-overview?*', async (route) => {
    await route.fulfill({
      contentType: 'application/json',
      json: {
        reportKey: 'sales-overview',
        title: '銷售總覽',
        timeBasis: 'Payment.PaidAtUtc / Refund.SucceededAtUtc',
        timeZone: 'Asia/Taipei',
        from: '2026-08-25',
        to: '2026-09-01',
        generatedAtUtc: '2026-08-29T04:00:00Z',
        asOfUtc: '2026-08-29T04:00:00Z',
        summary: [
          { metricKey: 'paid_amount', value: 1060, unit: 'currency' },
          { metricKey: 'refund_amount', value: 500, unit: 'currency' },
          { metricKey: 'net_revenue', value: 560, unit: 'currency' },
        ],
        series: [
          {
            bucket: '2026-08-27',
            metrics: [{ metricKey: 'net_revenue', value: 560, unit: 'currency' }],
          },
        ],
        rows: {
          items: [{
            rowType: 'sales-overview',
            bucket: '2026-08-27',
            netRevenue: 560,
            orderCount: 1,
            averageOrderValue: 1060,
            refundAmount: 500,
            refundAmountRate: 500 / 1060,
            cancelledOrderCount: 0,
            cancellationRate: 0,
          }],
          nextCursor: null,
          hasMore: false,
        },
      },
    })
  })

  await page.goto('./reports/sales-overview')

  await expect(page).toHaveURL(/\/admin\/reports\/sales-overview$/)
  await expect(page.getByRole('heading', { level: 1, name: '銷售總覽' })).toBeVisible()
  await expect(page.getByRole('navigation', { name: '營運報表' }).getByRole('link', { name: '毛利分析' })).toBeVisible()
  await expect(page.locator('article').filter({ hasText: '淨營收' }).getByText('NT$560')).toBeVisible()
  await expect(page.locator('article').filter({ hasText: '退款金額' }).getByText('NT$500')).toBeVisible()
  await expect(page.getByRole('row', { name: /2026-08-27 NT\$560 1 NT\$1,060 NT\$500/ })).toBeVisible()
})

test('customer service can approve a verified-purchase review from the moderation queue', async ({ page }) => {
  await page.route('**/api/v1/admin/auth/session', async route => route.fulfill({
    contentType: 'application/json',
    json: {
      isAuthenticated: true,
      user: {
        publicId: '0199256e-8c40-7000-8000-000000000005',
        displayName: 'S-02 Customer Service',
        emailMasked: 's***2@example.test',
        emailVerified: true,
        locale: 'zh-TW',
        roles: ['CustomerService'],
      },
      expiresAtUtc: '2026-08-29T12:00:00Z',
      requiresTwoFactor: false,
    },
  }))
  await page.route('**/api/v1/security/antiforgery-token', async route => route.fulfill({
    contentType: 'application/json',
    json: { requestToken: 's02-browser-antiforgery-token' },
  }))
  await page.route('**/api/v1/admin/reviews?status=pendingReview', async route => route.fulfill({
    contentType: 'application/json',
    json: [{
      publicId: '33333333-3333-3333-3333-333333333333',
      productPublicId: '22222222-2222-2222-2222-222222222222',
      productName: '人體工學椅',
      skuName: '黑色',
      rating: 5,
      title: '久坐也舒服',
      content: '實際使用一週，腰靠支撐很穩。',
      status: 'pendingReview',
      rejectionReason: null,
      createdAtUtc: '2026-08-29T04:00:00Z',
      reviewedAtUtc: null,
      rowVersion: 'AAAAAAAAB9E=',
      images: [],
    }],
  }))
  await page.route('**/api/v1/admin/reviews/33333333-3333-3333-3333-333333333333/actions/approve', async route => {
    expect(route.request().postDataJSON()).toEqual({
      reasonCode: 'review_approve',
      note: null,
      rowVersion: 'AAAAAAAAB9E=',
    })
    await route.fulfill({
      contentType: 'application/json',
      json: {
        publicId: '33333333-3333-3333-3333-333333333333',
        productPublicId: '22222222-2222-2222-2222-222222222222',
        productName: '人體工學椅',
        skuName: '黑色',
        rating: 5,
        title: '久坐也舒服',
        content: '實際使用一週，腰靠支撐很穩。',
        status: 'approved',
        rejectionReason: null,
        createdAtUtc: '2026-08-29T04:00:00Z',
        reviewedAtUtc: '2026-08-29T05:00:00Z',
        rowVersion: 'AAAAAAAAB9F=',
        images: [],
      },
    })
  })

  await page.goto('./reviews')
  await expect(page.getByRole('heading', { level: 1, name: '商品評價審核' })).toBeVisible()
  await expect(page.getByText('實際使用一週，腰靠支撐很穩。')).toBeVisible()
  await page.getByRole('button', { name: '核准公開' }).click()
  await expect(page.getByText('審核狀態已更新並留下稽核紀錄。')).toBeVisible()
})
