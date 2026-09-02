import { createHmac, randomUUID } from 'node:crypto'
import type { APIRequestContext } from '@playwright/test'
import { expect, test } from './fixtures.js'

const guestAccessPepper = 'e2e-guest-order-access-pepper-32-bytes'

interface CartSnapshot {
  publicId: string
  rowVersion: string
}

interface OrderSnapshot {
  publicId: string
  orderNumber: string
  orderStatus: string
  rowVersion: string
  items: Array<{ publicId: string }>
  amounts: {
    merchandiseSubtotal: number
    itemDiscountTotal: number
    shippingFee: number
    assemblyFee: number
    grandTotal: number
    paidAmount: number
  }
}

async function getAntiforgeryToken(api: APIRequestContext): Promise<string> {
  const response = await api.get('/api/v1/security/antiforgery-token', {
    headers: { 'X-DoSelect-Client': 'member' },
  })
  expect(response.ok(), 'The E2E setup must obtain a real antiforgery token').toBe(true)
  const body = await response.json() as { requestToken: string }
  return body.requestToken
}

function unsafeHeaders(requestToken: string, extra: Record<string, string> = {}): Record<string, string> {
  return {
    'X-DoSelect-Client': 'member',
    'X-XSRF-TOKEN': requestToken,
    ...extra,
  }
}

async function createGuestOrder(
  api: APIRequestContext,
  skuPublicId: string,
  email: string,
  requestToken: string,
  // Checkout 會在同一交易建立初始 PaymentAttempt，所以這裡選的付款方式就是
  // 顧客之後在付款頁看到的那一筆（alex #87 review 1）。
  paymentMethod: string = 'creditCard',
): Promise<OrderSnapshot> {
  const guestCartKey = `e2e-guest-cart-${randomUUID()}`
  const cartHeaders = unsafeHeaders(requestToken, {
    'X-DoSelect-Guest-Cart-Key': guestCartKey,
  })
  const initialCartResponse = await api.get('/api/v1/cart', { headers: cartHeaders })
  expect(initialCartResponse.ok(), 'The E2E setup must create a real guest cart').toBe(true)
  const initialCart = await initialCartResponse.json() as CartSnapshot

  const addItemResponse = await api.post('/api/v1/cart/items', {
    headers: cartHeaders,
    data: {
      skuPublicId,
      quantity: 1,
      cartRowVersion: initialCart.rowVersion,
    },
  })
  expect(addItemResponse.ok(), 'The seeded SKU must be addable to a real guest cart').toBe(true)
  const cart = await addItemResponse.json() as CartSnapshot

  const policyResponse = await api.get('/api/v1/checkout/policy-versions')
  expect(policyResponse.ok(), 'The Checkout policy versions must be available').toBe(true)
  const policies = await policyResponse.json() as {
    terms: number
    return: number
    privacy: number
  }

  const createResponse = await api.post('/api/v1/orders', {
    headers: unsafeHeaders(requestToken, {
      'X-DoSelect-Guest-Cart-Key': guestCartKey,
      'Idempotency-Key': `e2e-checkout-${randomUUID()}`,
    }),
    data: {
      cartPublicId: cart.publicId,
      cartRowVersion: cart.rowVersion,
      buyer: {
        email,
        name: '訪客測試買家',
        phone: '0912345678',
      },
      shipping: {
        methodCode: 'HomeDelivery',
        address: {
          recipientName: '訪客測試買家',
          phone: '0912345678',
          postalCode: '100',
          city: '台北市',
          district: '中正區',
          addressLine1: '測試路 1 號',
          addressLine2: null,
        },
        storePublicId: null,
        deliveryNote: null,
      },
      paymentMethod,
      couponCode: null,
      invoice: {
        type: 'simulated',
        buyerType: 'personal',
        carrierType: null,
        carrierValue: null,
        companyTaxId: null,
        companyName: null,
      },
      acceptPolicyVersions: {
        terms: policies.terms,
        return: policies.return,
        privacy: policies.privacy,
      },
    },
  })
  const createResponseBody = await createResponse.text()
  expect(
    createResponse.status(),
    `The E2E setup must complete a real guest Checkout. Response: ${createResponseBody}`,
  ).toBe(201)
  return JSON.parse(createResponseBody) as OrderSnapshot
}

function deriveGuestVerificationCode(requestPublicId: string, sendNumber = 1): string {
  const normalizedId = requestPublicId.replaceAll('-', '').toLowerCase()
  const digest = createHmac('sha256', guestAccessPepper)
    .update(`verification-code:${normalizedId}:${sendNumber}`)
    .digest()
  return String(digest.readUInt32BE(0) % 1_000_000).padStart(6, '0')
}

test('a seeded member can sign in, open a protected profile, and sign out', async ({
  page,
  loginAsMember,
}) => {
  await loginAsMember()

  await expect(page.getByText('DoSelect 測試會員', { exact: true })).toBeVisible()
  await page.goto('/account')
  await expect(page.getByRole('heading', { level: 1, name: '會員資料' })).toBeVisible()
  await expect(page.getByRole('definition').filter({ hasText: 'DoSelect 測試會員' })).toBeVisible()

  await page.getByRole('button', { name: '登出' }).click()
  await expect(page).toHaveURL(/\/$/)
  await expect(page.getByRole('link', { name: '登入／註冊' })).toBeVisible()

  await page.goto('/account')
  await expect(page).toHaveURL((url) =>
    url.pathname === '/login' && url.searchParams.get('redirect') === '/account')
})

test('a guest can verify, view and cancel only the matching order without cross-order effects', async ({
  page,
  api,
  seed,
}) => {
  const requestToken = await getAntiforgeryToken(api)
  const targetEmail = `target-${randomUUID()}@example.test`
  const otherEmail = `other-${randomUUID()}@example.test`
  const targetOrder = await createGuestOrder(api, seed.skuPublicId, targetEmail, requestToken)
  const otherOrder = await createGuestOrder(api, seed.skuPublicId, otherEmail, requestToken)

  await page.goto('/guest-orders/access')
  await page.getByLabel('訂單編號').fill(targetOrder.orderNumber)
  await page.getByLabel('訂單 Email').fill(targetEmail)
  await page.getByRole('button', { name: '寄送驗證碼' }).click()
  await expect(page).toHaveURL(/\/guest-orders\/verify\?requestPublicId=/)

  const requestPublicId = new URL(page.url()).searchParams.get('requestPublicId')
  expect(requestPublicId, 'Guest access must redirect with its opaque request id').toBeTruthy()
  const correctCode = deriveGuestVerificationCode(requestPublicId!)
  const wrongCode = correctCode === '000000' ? '000001' : '000000'

  await page.getByLabel('六位數驗證碼').fill(wrongCode)
  await page.getByRole('button', { name: '驗證並查看訂單' }).click()
  await expect(page.getByRole('alert')).toContainText('驗證碼無效或已過期')
  expect(
    (await page.context().cookies()).some(cookie => cookie.name === '.DoSelect.GuestOrderAccess'),
    'A wrong code must not issue the order-scoped Cookie',
  ).toBe(false)

  await page.getByLabel('六位數驗證碼').fill(correctCode)
  await page.getByRole('button', { name: '驗證並查看訂單' }).click()
  await expect(page).toHaveURL(new RegExp(`/orders/${targetOrder.publicId}$`))
  await expect(page.getByRole('heading', { level: 1, name: `訂單 ${targetOrder.orderNumber}` }))
    .toBeVisible()
  await expect(page.getByText('狀態：等待付款', { exact: true })).toBeVisible()

  const crossOrderCancelStatus = await page.evaluate(async ({ orderPublicId, rowVersion }) => {
    const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
      credentials: 'include',
      headers: { 'X-DoSelect-Client': 'member' },
    })
    const token = await tokenResponse.json() as { requestToken: string }
    const response = await fetch(`/api/v1/orders/${orderPublicId}/actions/cancel`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-DoSelect-Client': 'member',
        'X-XSRF-TOKEN': token.requestToken,
      },
      body: JSON.stringify({
        reasonCode: 'changed_mind',
        note: 'must be rejected',
        orderRowVersion: rowVersion,
      }),
    })
    return response.status
  }, { orderPublicId: otherOrder.publicId, rowVersion: otherOrder.rowVersion })
  expect(crossOrderCancelStatus, 'An order-scoped Cookie must not cancel another order')
    .toBe(404)

  await page.goto(`/orders/${otherOrder.publicId}`)
  await expect(page.getByRole('heading', { level: 1, name: '找不到頁面' })).toBeVisible()

  await page.goto(`/orders/${targetOrder.publicId}`)
  await expect(page.getByRole('heading', { level: 1, name: `訂單 ${targetOrder.orderNumber}` }))
    .toBeVisible()
  await page.getByRole('button', { name: '申請取消訂單' }).click()
  await page.getByLabel('取消原因').selectOption('changed_mind')
  await page.getByLabel('補充說明（選填）').fill('WP-A02 瀏覽器驗證')
  await page.getByRole('button', { name: '確認取消訂單' }).click()
  await expect(page.getByText('狀態：已取消', { exact: true })).toBeVisible()

  await page.goto('/guest-orders/access')
  await page.getByLabel('訂單編號').fill(otherOrder.orderNumber)
  await page.getByLabel('訂單 Email').fill(otherEmail)
  await page.getByRole('button', { name: '寄送驗證碼' }).click()
  await expect(page).toHaveURL(/\/guest-orders\/verify\?requestPublicId=/)
  const otherRequestPublicId = new URL(page.url()).searchParams.get('requestPublicId')
  expect(otherRequestPublicId).toBeTruthy()
  await page.getByLabel('六位數驗證碼')
    .fill(deriveGuestVerificationCode(otherRequestPublicId!))
  await page.getByRole('button', { name: '驗證並查看訂單' }).click()
  await expect(page).toHaveURL(new RegExp(`/orders/${otherOrder.publicId}$`))

  const unchangedOrder = await page.evaluate(async (orderPublicId) => {
    const response = await fetch(`/api/v1/orders/${orderPublicId}`, { credentials: 'include' })
    if (!response.ok) {
      throw new Error(`Expected the independently verified order to load, got ${response.status}.`)
    }
    return await response.json() as OrderSnapshot
  }, otherOrder.publicId)
  expect(unchangedOrder.orderStatus).toBe(otherOrder.orderStatus)
  expect(unchangedOrder.rowVersion).toBe(otherOrder.rowVersion)
})

test('a shopper can open the seeded catalog and view product details', async ({ page, api, seed }) => {
  const productResponse = await api.get(`/api/v1/products/${seed.productPublicId}`)
  expect(productResponse.ok(), 'The deterministic catalog seed must exist').toBe(true)

  await page.goto('/')

  await expect(page.getByRole('heading', {
    level: 1,
    name: '說出需求，組出適合你的電腦',
  })).toBeVisible()
  await page.getByRole('button', { name: '看全部商品' }).click()

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

test('a guest keeps the checkout payment attempt after reloading the payment page', async ({
  page,
  api,
  seed,
}) => {
  // 交接單 §5.4 / alex Issue #86 D1：重新整理後金額、付款方式、狀態與付款指示都要還在，
  // 而且不能多建一筆 Attempt。
  //
  // Checkout 會在同一交易建立初始 PaymentAttempt，所以這裡直接用 ATM 結帳 ——
  // 那串繳費代碼是使用者要拿去繳費的東西，在恢復功能之前一重新整理就消失，是這個
  // 缺口最具體的後果。付款頁不會再出現建立表單，因為那筆嘗試還在進行中。
  const requestToken = await getAntiforgeryToken(api)
  const email = `payment-restore-${randomUUID()}@example.test`
  const order = await createGuestOrder(api, seed.skuPublicId, email, requestToken, 'atm')

  await page.goto('/guest-orders/access')
  await page.getByLabel('訂單編號').fill(order.orderNumber)
  await page.getByLabel('訂單 Email').fill(email)
  await page.getByRole('button', { name: '寄送驗證碼' }).click()
  await expect(page).toHaveURL(/\/guest-orders\/verify\?requestPublicId=/)
  const requestPublicId = new URL(page.url()).searchParams.get('requestPublicId')
  expect(requestPublicId).toBeTruthy()
  await page.getByLabel('六位數驗證碼').fill(deriveGuestVerificationCode(requestPublicId!))
  await page.getByRole('button', { name: '驗證並查看訂單' }).click()
  await expect(page).toHaveURL(new RegExp(`/orders/${order.publicId}$`))

  await page.goto(`/orders/${order.publicId}/payment`)

  const attemptPanel = page.getByRole('region', { name: '付款嘗試' })
  await expect(attemptPanel).toBeVisible()
  // 進行中的嘗試已經在了，所以不該出現建立表單。
  await expect(page.locator('#payment-method')).toHaveCount(0)
  // 重新整理「前」就要看得到付款方式 —— 沒有這一條，下面的比對在兩邊都不顯示時也會過。
  await expect(attemptPanel).toContainText('ATM 虛擬帳號')
  const beforeReload = (await attemptPanel.textContent())?.trim()
  expect(beforeReload, 'The checkout attempt must render before the reload').toBeTruthy()

  await page.reload()

  await expect(attemptPanel).toBeVisible()
  expect((await attemptPanel.textContent())?.trim()).toBe(beforeReload)
  // D1 要求畫面上仍顯示相同的金額、付款方式、狀態與 Instruction。
  // 只在 API JSON 上斷言 method 是測錯層級 —— 那是伺服器回得出來，不是使用者看得到。
  await expect(attemptPanel).toContainText('ATM 虛擬帳號')
  await expect(attemptPanel).toContainText('等待付款')
  await expect(attemptPanel).toContainText('繳費代碼')

  // 而且沒有多建一筆：最新一筆仍是結帳當下那一筆 ATM 嘗試。
  const latest = await page.evaluate(async (orderPublicId) => {
    const response = await fetch(
      `/api/v1/orders/${orderPublicId}/payment-attempts/latest`,
      { credentials: 'include' },
    )
    if (!response.ok) {
      throw new Error(`Expected the restored attempt to load, got ${response.status}.`)
    }
    return await response.json() as { publicId: string, method: string, status: string }
  }, order.publicId)
  expect(latest.method).toBe('atm')
  expect(latest.status).toBe('awaitingPayment')
})

test('a guest completes the prepared cart through checkout payment and invoice', async ({
  page,
  seed,
}) => {
  test.setTimeout(60_000)
  const email = `core-transaction-${randomUUID()}@example.test`
  await page.addInitScript((guestCartKey) => {
    const browser = globalThis as unknown as {
      localStorage: { setItem: (key: string, value: string) => void }
    }
    browser.localStorage.setItem('doselect.guestCartKey', guestCartKey)
  }, seed.coreTransactionGuestCartKey)

  await page.goto('/cart')
  await expect(page.getByRole('heading', { level: 1, name: '購物車' })).toBeVisible()
  await expect(page.getByText('自訂組裝', { exact: true })).toBeVisible()
  await expect(page.getByRole('list', { name: /組裝品項：/ }).getByRole('listitem')).toHaveCount(8)

  const checkoutButton = page.getByRole('button', { name: '前往結帳' })
  await expect(checkoutButton).toBeEnabled()
  await checkoutButton.click()
  await expect(page).toHaveURL(/\/checkout$/)
  await expect(page.getByRole('heading', { level: 1, name: '結帳' })).toBeVisible()

  await page.getByLabel('Email').fill(email)
  await page.getByLabel('姓名').fill('核心交易訪客')
  await page.getByLabel('電話').fill('0912345678')

  await page.getByLabel('優惠碼（選填）').fill('CREATOR10')
  await page.getByRole('button', { name: '套用優惠券' }).click()
  await expect(page.getByText('已套用 CREATOR10')).toBeVisible()

  await page.getByRole('radio', { name: '組裝電腦宅配' }).check()
  await page.getByLabel('收件人').fill('核心交易訪客')
  await page.getByLabel('收件電話').fill('0912345678')
  await page.getByLabel('郵遞區號').fill('100')
  await page.getByLabel('縣市').fill('台北市')
  await page.getByLabel('行政區').fill('中正區')
  await page.getByLabel('地址', { exact: true }).fill('測試路 1 號')

  await page.getByRole('radio', { name: '信用卡' }).check()
  await page.getByRole('checkbox', { name: /我同意服務條款/ }).check()
  await page.getByRole('checkbox', { name: /我同意退換貨政策/ }).check()
  await page.getByRole('checkbox', { name: /我同意隱私權政策/ }).check()

  const createResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && new URL(response.url()).pathname === '/api/v1/orders')
  await page.getByRole('button', { name: '確認建立訂單' }).click()
  const createResponse = await createResponsePromise
  expect(createResponse.status(), 'The browser Checkout must create the order').toBe(201)
  const order = await createResponse.json() as OrderSnapshot
  const createRequest = createResponse.request()
  const idempotencyKey = createRequest.headers()['idempotency-key']
  const requestBody = createRequest.postDataJSON()
  expect(idempotencyKey, 'The browser Checkout must send its idempotency key').toBeTruthy()

  await expect(page.getByRole('heading', { level: 2, name: '訂單已建立' })).toBeVisible()
  await expect(page.getByText(`訂單編號：${order.orderNumber}`)).toBeVisible()
  await expect(page.getByText('商品小計：').locator('..')).toContainText('NT$45,000')
  await expect(page.getByText('優惠折扣：').locator('..')).toContainText('−NT$2,000')
  await expect(page.getByText('配送費：').locator('..')).toContainText('NT$300')
  await expect(page.getByText('組裝費：').locator('..')).toContainText('NT$300')
  await expect(page.getByText('應付總額：').locator('..')).toContainText('NT$43,600')

  const replay = await page.evaluate(async ({ body, key, guestCartKey }) => {
    const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
      credentials: 'include',
      headers: { 'X-DoSelect-Client': 'member' },
    })
    const token = await tokenResponse.json() as { requestToken: string }
    const response = await fetch('/api/v1/orders', {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-DoSelect-Client': 'member',
        'X-XSRF-TOKEN': token.requestToken,
        'X-DoSelect-Guest-Cart-Key': guestCartKey,
        'Idempotency-Key': key,
      },
      body: JSON.stringify(body),
    })
    return { status: response.status, body: await response.json() as OrderSnapshot }
  }, {
    body: requestBody,
    key: idempotencyKey!,
    guestCartKey: seed.coreTransactionGuestCartKey,
  })
  expect(replay.status).toBe(201)
  expect(replay.body.publicId).toBe(order.publicId)
  expect(replay.body.orderNumber).toBe(order.orderNumber)

  await page.getByRole('link', { name: '驗證訂單後繼續付款' }).click()
  await page.getByLabel('訂單編號').fill(order.orderNumber)
  await page.getByLabel('訂單 Email').fill(email)
  await page.getByRole('button', { name: '寄送驗證碼' }).click()
  await expect(page).toHaveURL(/\/guest-orders\/verify\?requestPublicId=/)
  const requestPublicId = new URL(page.url()).searchParams.get('requestPublicId')
  expect(requestPublicId).toBeTruthy()
  await page.getByLabel('六位數驗證碼').fill(deriveGuestVerificationCode(requestPublicId!))
  await page.getByRole('button', { name: '驗證並查看訂單' }).click()
  await expect(page).toHaveURL(new RegExp(`/orders/${order.publicId}$`))

  const persistedOrder = await page.evaluate(async (orderPublicId) => {
    const response = await fetch(`/api/v1/orders/${orderPublicId}`, { credentials: 'include' })
    if (!response.ok) {
      throw new Error(`Expected the verified order to load, got ${response.status}.`)
    }
    return await response.json() as OrderSnapshot
  }, order.publicId)
  expect(persistedOrder.items).toHaveLength(9)
  expect(persistedOrder.amounts).toEqual(expect.objectContaining({
    merchandiseSubtotal: 45000,
    itemDiscountTotal: 2000,
    shippingFee: 300,
    assemblyFee: 300,
    grandTotal: 43600,
    paidAmount: 0,
  }))

  await page.getByRole('link', { name: '前往付款' }).click()
  await expect(page.getByRole('region', { name: '付款嘗試' })).toContainText('信用卡')
  await page.getByRole('button', { name: '模擬付款成功' }).click()
  await expect(page.getByText('付款已完成', { exact: true })).toBeVisible()

  await expect.poll(async () => {
    return await page.evaluate(async (orderPublicId) => {
      const response = await fetch(`/api/v1/orders/${orderPublicId}/invoice`, {
        credentials: 'include',
      })
      return response.status
    }, order.publicId)
  }, {
    message: 'The payment outbox must create the simulated invoice',
    timeout: 20_000,
    intervals: [500, 1_000, 2_000],
  }).toBe(200)

  await page.getByRole('link', { name: '← 回訂單詳情' }).click()
  await expect(page.getByText('付款狀態：已付款', { exact: true })).toBeVisible()
  await expect(page.getByText(/DEMO-NOT-A-TAX-INVOICE/)).toBeVisible()
  await expect(page.getByText('狀態：已開立', { exact: true })).toBeVisible()
})
