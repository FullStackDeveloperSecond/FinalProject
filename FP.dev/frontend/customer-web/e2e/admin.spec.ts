import { expect, test } from './fixtures.js'

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
