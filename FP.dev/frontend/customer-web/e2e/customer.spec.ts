import { expect, test } from './fixtures.js'
import { deriveGuestOrderVerificationCode } from './guestOrderAccessCode.js'

// UC-AUTH-02: real login/logout against the seeded member account. Email verification and
// forgot/reset password (UC-AUTH-01, UC-AUTH-03) are not covered here — as of this branch, an
// externally-driven Browser E2E process has no way to recover the verification/reset token (the
// non-prod EmailSender discards the message body entirely, and the token itself is never stored
// server-side); see WP-H02 log for the open question raised with alex about adding an E2E-only
// capture endpoint.
test('a member can log in with the seeded account and log out', async ({ page, loginAsMember }) => {
  await loginAsMember()

  const logoutButton = page.getByRole('button', { name: '登出' })
  await expect(logoutButton).toBeVisible()

  await logoutButton.click()
  await expect(page.getByRole('link', { name: '登入／註冊' })).toBeVisible()
})

// UC-AUTH-02: five wrong passwords must lock the account for 15 minutes, and the lockout must
// block even the *correct* password on the next attempt (Identity checks lockout before verifying
// the password — see MemberLoginGateway.ValidateCredentialsAsync). Using a throwaway
// self-registered account (rather than the shared seeded member) keeps this from locking an
// account other tests rely on; lockout applies before the email-verification check runs, so the
// account never needs to be verified for this scenario.
test('five wrong passwords lock a member account, blocking even the correct password', async ({ page }) => {
  const email = `e2e-lockout-${Date.now()}@example.test`
  const correctPassword = 'CorrectHorseBattery9!'
  const wrongPassword = 'DefinitelyWrongPassword9!'
  const invalidCredentialsMessage = page.getByText('Email 或密碼錯誤，請再試一次。')

  await page.goto('/register')
  await page.getByLabel('電子郵件').fill(email)
  await page.getByLabel('密碼', { exact: true }).fill(correctPassword)
  await page.getByLabel('確認密碼').fill(correctPassword)
  await page.getByLabel('姓名').fill('E2E Lockout Member')
  await page.getByLabel('我同意服務條款與隱私權政策').check()
  await page.getByRole('button', { name: '立即註冊' }).click()
  await expect(page.getByRole('heading', { name: '請完成 Email 驗證' })).toBeVisible()

  await page.goto('/login')
  for (let attempt = 0; attempt < 5; attempt += 1) {
    await page.getByRole('textbox', { name: '電子郵件' }).fill(email)
    await page.getByRole('textbox', { name: '密碼', exact: true }).fill(wrongPassword)
    await page.getByRole('button', { name: '登入' }).click()
    await expect(invalidCredentialsMessage).toBeVisible()
  }

  await page.getByRole('textbox', { name: '電子郵件' }).fill(email)
  await page.getByRole('textbox', { name: '密碼', exact: true }).fill(correctPassword)
  await page.getByRole('button', { name: '登入' }).click()
  await expect(invalidCredentialsMessage).toBeVisible()
  await expect(page).toHaveURL(/\/login$/)
})

test('a shopper can open the seeded catalog and view product details', async ({ page, api, seed }) => {
  const productResponse = await api.get(`/api/v1/products/${seed.productPublicId}`)
  expect(productResponse.ok(), 'The deterministic catalog seed must exist').toBe(true)

  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'DoSelect 懂選' })).toBeVisible()
  await page.getByRole('link', { name: /瀏覽全部商品/ }).click()

  await expect(page).toHaveURL(/\/products$/)
  await expect(page.getByRole('heading', { level: 1, name: '商品搜尋' })).toBeVisible()

  const seededProduct = page.getByRole('heading', {
    level: 3,
    name: '懂選開發用顯示卡',
    exact: true,
  })
  await expect(seededProduct).toBeVisible()
  await seededProduct.click()

  await expect(page).toHaveURL(new RegExp(`/products/${seed.productPublicId}$`))
  await expect(page.getByRole('heading', { level: 1, name: '懂選開發用顯示卡' })).toBeVisible()
  await expect(page.getByText('NT$19,900')).toBeVisible()
  await expect(page.getByText('現貨供應')).toBeVisible()
})

test('a member can consent to AI support and fall back to a human case when AI is disabled', async ({
  page,
  loginAsMember,
}) => {
  await loginAsMember()
  await page.goto('/support')

  await expect(page.getByRole('heading', { level: 1, name: 'AI 客服' })).toBeVisible()
  const consentCheckbox = page.getByRole('checkbox', {
    name: '我已閱讀並同意上述外部 AI 處理方式',
  })
  const remainingUsage = page.getByText(/今日剩餘：/)
  await expect(consentCheckbox.or(remainingUsage)).toBeVisible()
  if (await consentCheckbox.isVisible()) {
    await consentCheckbox.check()
    await page.getByRole('button', { name: '同意並開始使用' }).click()
  }

  await expect(remainingUsage).toBeVisible()
  await page.getByRole('textbox', { name: '你的問題' }).fill('請說明退貨流程')
  await page.getByRole('button', { name: '送出問題' }).click()

  await expect(page.getByRole('link', { name: '建立人工客服案件' })).toBeVisible()
  await page.getByRole('button', { name: '撤回 AI 同意' }).click()
  await expect(consentCheckbox).toBeVisible()
})

test('a member can submit a review for a completed-order item', async ({ page }) => {
  await page.route('**/api/v1/auth/session', async route => route.fulfill({
    contentType: 'application/json',
    json: {
      isAuthenticated: true,
      user: {
        publicId: '11111111-1111-1111-1111-111111111111',
        displayName: 'S-02 Member',
        emailMasked: 'm***r@example.test',
        emailVerified: true,
        locale: 'zh-TW',
      },
    },
  }))
  await page.route('**/api/v1/security/antiforgery-token', async route => route.fulfill({
    contentType: 'application/json',
    json: { requestToken: 's02-browser-antiforgery-token' },
  }))
  await page.route('**/api/v1/reviews/eligible-order-items', async route => route.fulfill({
    contentType: 'application/json',
    json: [{
      orderItemPublicId: '44444444-4444-4444-4444-444444444444',
      productPublicId: '22222222-2222-2222-2222-222222222222',
      skuCode: 'CHAIR-BLK',
      productName: '人體工學椅',
      skuName: '黑色',
      completedAtUtc: '2026-08-28T04:00:00Z',
      reviewPublicId: null,
      reviewStatus: null,
    }],
  }))
  await page.route('**/api/v1/reviews/mine', async route => route.fulfill({
    contentType: 'application/json',
    json: [],
  }))
  await page.route('**/api/v1/reviews', async route => {
    expect(route.request().postDataJSON()).toEqual({
      orderItemPublicId: '44444444-4444-4444-4444-444444444444',
      rating: 5,
      title: '久坐也舒服',
      content: '實際使用一週，腰靠支撐很穩。',
      submit: true,
    })
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      json: {
        publicId: '33333333-3333-3333-3333-333333333333',
        orderItemPublicId: '44444444-4444-4444-4444-444444444444',
        productPublicId: '22222222-2222-2222-2222-222222222222',
        productName: '人體工學椅',
        skuName: '黑色',
        rating: 5,
        title: '久坐也舒服',
        content: '實際使用一週，腰靠支撐很穩。',
        status: 'pendingReview',
        rejectionReason: null,
        createdAtUtc: '2026-08-29T04:00:00Z',
        updatedAtUtc: '2026-08-29T04:00:00Z',
        rowVersion: 'AAAAAAAAB9E=',
        images: [],
      },
    })
  })

  await page.goto('/account/reviews')
  await expect(page.getByRole('heading', { level: 1, name: '我的商品評價' })).toBeVisible()
  await page.getByLabel('已購買品項').selectOption('44444444-4444-4444-4444-444444444444')
  await page.getByLabel('標題（選填）').fill('久坐也舒服')
  await page.getByLabel('內容').fill('實際使用一週，腰靠支撐很穩。')
  await page.getByRole('button', { name: '送出審核' }).click()
  await expect(page.getByText('評價已送出審核。')).toBeVisible()
})

test('a public shopper can use AI search safely when the provider is disabled', async ({ page }) => {
  await page.goto('/ai-search')

  await expect(page.getByRole('heading', { level: 1, name: '說出需求，不必先學會所有規格' }))
    .toBeVisible()
  await page.getByRole('textbox', { name: '你想找什麼？' }).fill('懂選開發用顯示卡')
  await page.getByRole('button', { name: '開始懂選' }).click()

  await expect(page.getByRole('heading', { name: 'AI 暫時無法使用，已改用一般搜尋' }))
    .toBeVisible()
  await expect(page.getByText('不代表 AI 推薦或相容性保證')).toBeVisible()
  await expect(page.getByRole('heading', { level: 3, name: '懂選開發用顯示卡', exact: true }))
    .toBeVisible()
})

// WP-H03 / UC-GUEST-ORDER-01: real Guest Access → Order detail → cancel journey. createGuestOrder
// drives the real Cart → Checkout HTTP APIs (no Checkout UI exists yet, that's a separate work
// package) to produce a real pendingPayment order; the six-digit access code is derived the same
// way the backend derives it (GuestOrderAccessHasher.DeriveVerificationCode) rather than read from
// an email, using the fixed pepper the E2E environment is configured with.
test('a guest can access, view, and cancel their order via the real order-access journey', async ({
  page,
  createGuestOrder,
  seed,
}) => {
  const order = await createGuestOrder()

  await page.goto('/guest-orders/access')
  await page.getByLabel('訂單編號').fill(order.orderNumber)
  await page.getByLabel('訂單 Email').fill(order.email)
  await page.getByRole('button', { name: '寄送驗證碼' }).click()

  await expect(page).toHaveURL(/\/guest-orders\/verify\?requestPublicId=/)
  const requestPublicId = new URL(page.url()).searchParams.get('requestPublicId')
  expect(requestPublicId, 'the verify page must carry the requestPublicId from the access request').toBeTruthy()
  const code = deriveGuestOrderVerificationCode(requestPublicId!, 1, seed.guestOrderAccessPepper)

  await page.getByLabel('六位數驗證碼').fill(code)
  await page.getByRole('button', { name: '驗證並查看訂單' }).click()

  await expect(page).toHaveURL(new RegExp(`/orders/${order.orderPublicId}$`))
  await expect(page.getByRole('heading', { name: `訂單 ${order.orderNumber}` })).toBeVisible()
  await expect(page.getByText('狀態：等待付款', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: '申請取消訂單' }).click()
  await page.getByLabel('取消原因').selectOption('changed_mind')
  await page.getByRole('button', { name: '確認取消訂單' }).click()

  await expect(page.getByText('狀態：已取消')).toBeVisible()
})

// UC-GUEST-ORDER-01: "Given Token 指向訂單 A，When 嘗試查看或操作訂單 B，Then API 拒絕存取" — the
// verified guest-access cookie is scoped to the one order it was issued for. The API denies this
// as a 404 (OrderDetailPage's 'not-found' state), not a 401/403 — a deliberate choice that avoids
// confirming order B even exists to someone who isn't scoped to it.
test('a guest order access token for order A cannot view order B (Owner/Guest Scope)', async ({
  page,
  createGuestOrder,
  seed,
}) => {
  const orderA = await createGuestOrder()
  const orderB = await createGuestOrder()

  await page.goto('/guest-orders/access')
  await page.getByLabel('訂單編號').fill(orderA.orderNumber)
  await page.getByLabel('訂單 Email').fill(orderA.email)
  await page.getByRole('button', { name: '寄送驗證碼' }).click()

  await expect(page).toHaveURL(/\/guest-orders\/verify\?requestPublicId=/)
  const requestPublicId = new URL(page.url()).searchParams.get('requestPublicId')
  const code = deriveGuestOrderVerificationCode(requestPublicId!, 1, seed.guestOrderAccessPepper)
  await page.getByLabel('六位數驗證碼').fill(code)
  await page.getByRole('button', { name: '驗證並查看訂單' }).click()
  await expect(page).toHaveURL(new RegExp(`/orders/${orderA.orderPublicId}$`))

  await page.goto(`/orders/${orderB.orderPublicId}`)
  await expect(page.getByRole('heading', { name: '找不到頁面' })).toBeVisible()
})

// UC-GUEST-ORDER-01: "回應不得洩漏訂單是否存在" — a bogus order number/email pair must still reach
// the verify step (the backend creates an equivalent decoy request), not a distinguishable error.
test('a guest order access request never reveals whether the order/email combination exists', async ({ page }) => {
  await page.goto('/guest-orders/access')
  await page.getByLabel('訂單編號').fill('NOTAREALORDER123')
  await page.getByLabel('訂單 Email').fill('nobody@example.test')
  await page.getByRole('button', { name: '寄送驗證碼' }).click()

  await expect(page).toHaveURL(/\/guest-orders\/verify\?requestPublicId=/)
})
