import { createHmac } from 'node:crypto'
import { expect, test } from './fixtures.js'

const base32Alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'

function decodeBase32(value: string): Buffer {
  const normalized = value.toUpperCase().replace(/[=\s-]/g, '')
  let bits = 0
  let buffer = 0
  const bytes: number[] = []

  for (const character of normalized) {
    const index = base32Alphabet.indexOf(character)
    if (index < 0) {
      throw new Error('The TOTP enrollment secret contains an invalid Base32 character.')
    }

    buffer = (buffer << 5) | index
    bits += 5
    if (bits >= 8) {
      bits -= 8
      bytes.push((buffer >>> bits) & 0xff)
    }
  }

  return Buffer.from(bytes)
}

function currentTotp(secret: string): string {
  const counter = Buffer.alloc(8)
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)))
  const digest = createHmac('sha1', decodeBase32(secret)).update(counter).digest()
  const offset = digest[digest.length - 1]! & 0x0f
  const binary = digest.readUInt32BE(offset) & 0x7fffffff
  return (binary % 1_000_000).toString().padStart(6, '0')
}

function differentTotp(validCode: string): string {
  return ((Number(validCode) + 1) % 1_000_000).toString().padStart(6, '0')
}

test('a seeded administrator can enroll TOTP, reject a wrong code, and sign in again', async ({
  page,
  seed,
}) => {
  if (!seed.adminPassword) {
    throw new Error('Seed__AdminPassword is required for an administrator E2E journey.')
  }

  await page.goto('./')
  await page.getByRole('textbox', { name: '電子郵件' }).fill(seed.adminEmail)
  await page.getByLabel('密碼').fill(seed.adminPassword)
  await page.getByRole('button', { name: '登入' }).click()

  await expect(page).toHaveURL((url) => url.pathname === '/admin/login/enroll')
  await expect(page.getByRole('heading', { level: 1, name: '綁定兩步驟驗證' })).toBeVisible()
  const secret = (await page.locator('.totp-secret code').textContent())?.trim()
  expect(secret, 'The enrollment page must expose a manual TOTP secret for the operator').toBeTruthy()

  await page.getByLabel('請輸入 App 顯示的 6 位數驗證碼以確認綁定')
    .fill(currentTotp(secret!))
  await page.getByRole('button', { name: '確認綁定' }).click()

  await expect(page.getByRole('heading', { level: 1, name: '請保存您的備援碼' })).toBeVisible()
  await page.getByRole('checkbox', { name: '我已抄下並妥善保存這些備援碼' }).check()
  await page.getByRole('button', { name: '完成，進入後台' }).click()

  await expect(page).toHaveURL(/\/admin\/$/)
  await expect(page.getByRole('heading', { level: 1, name: '管理後台基礎環境已就緒' })).toBeVisible()
  await expect(page.getByText('DoSelect 開發管理員', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '登出' }).click()

  await expect(page).toHaveURL(/\/admin\/login$/)
  await page.getByRole('textbox', { name: '電子郵件' }).fill(seed.adminEmail)
  await page.getByLabel('密碼').fill(seed.adminPassword)
  await page.getByRole('button', { name: '登入' }).click()

  await expect(page).toHaveURL(/\/admin\/login\/verify$/)
  const validCode = currentTotp(secret!)
  await page.getByLabel('驗證碼').fill(differentTotp(validCode))
  await page.getByRole('button', { name: '驗證', exact: true }).click()
  await expect(page.getByRole('alert')).toHaveText('驗證碼不正確，請重新輸入。')

  await page.getByLabel('驗證碼').fill(currentTotp(secret!))
  await page.getByRole('button', { name: '驗證', exact: true }).click()
  await expect(page).toHaveURL(/\/admin\/$/)
  await expect(page.getByText('DoSelect 開發管理員', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: '登出' }).click()
  await expect(page).toHaveURL(/\/admin\/login$/)
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
