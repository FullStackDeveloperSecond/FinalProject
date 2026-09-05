import { createHmac, randomUUID } from 'node:crypto'
import type { APIRequestContext, Page } from '@playwright/test'
import { captureVisualEvidence } from './visualEvidence.js'
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

interface CartSnapshot {
  publicId: string
  rowVersion: string
}

interface CustomerOrderSnapshot {
  publicId: string
  orderNumber: string
  orderStatus: string
  paymentStatus: string
  fulfillmentStatus: string
  paidAtUtc?: string | null
  rowVersion: string
  amounts: {
    grandTotal: number
    paidAmount: number
  }
}

interface AdminOrderSnapshot {
  publicId: string
  orderStatus: string
  paymentStatus: string
  fulfillmentStatus: string
  paidAtUtc?: string | null
  amounts: {
    grandTotal: number
    paidAmount: number
  }
  shipment: {
    publicId: string
    status: string
    rowVersion: string
    history: Array<{ toStatus: string }>
  }
}

interface PaymentAttemptSnapshot {
  publicId: string
  method: string
  status: string
}

interface InvoiceSnapshot {
  publicId: string
  orderPublicId: string
  invoiceNumber: string
  status: string
  grossAmount: number
}

interface ShipmentCommandCapture {
  order: AdminOrderSnapshot
  idempotencyKey: string
  requestBody: unknown
}

async function getMemberAntiforgeryToken(api: APIRequestContext): Promise<string> {
  const response = await api.get('/api/v1/security/antiforgery-token', {
    headers: { 'X-DoSelect-Client': 'member' },
  })
  expect(response.ok(), 'The COD E2E setup must obtain a real antiforgery token').toBe(true)
  return ((await response.json()) as { requestToken: string }).requestToken
}

function unsafeMemberHeaders(
  requestToken: string,
  extra: Record<string, string> = {},
): Record<string, string> {
  return {
    'X-DoSelect-Client': 'member',
    'X-XSRF-TOKEN': requestToken,
    ...extra,
  }
}

async function firstConvenienceStorePublicId(api: APIRequestContext): Promise<string> {
  const response = await api.get(
    '/api/v1/convenience-stores?providerCode=7-11&pageNumber=1&pageSize=1',
  )
  expect(response.ok(), 'The minimal seed must expose a convenience store').toBe(true)
  const body = await response.json() as { items: Array<{ publicId: string }> }
  expect(body.items).toHaveLength(1)
  return body.items[0]!.publicId
}

async function codEligibleSkuPublicId(api: APIRequestContext): Promise<string> {
  const response = await api.get('/api/v1/products?q=DEV-COMPAT-CPU-001&pageSize=1')
  expect(response.ok(), 'The minimal seed must expose a COD-eligible SKU').toBe(true)
  const body = await response.json() as {
    items: Array<{ defaultSkuPublicId: string, skuCode: string }>
  }
  const sku = body.items.find(item => item.skuCode === 'DEV-COMPAT-CPU-001')
  expect(sku, 'The COD journey must use the explicit non-prepayment seed SKU').toBeTruthy()
  return sku!.defaultSkuPublicId
}

async function createGuestCodOrder(
  api: APIRequestContext,
  skuPublicId: string,
  email: string,
  requestToken: string,
  destination: { methodCode: 'HomeDelivery' } | { methodCode: 'StorePickup', storePublicId: string },
): Promise<CustomerOrderSnapshot> {
  const guestCartKey = `e2e-cod-cart-${randomUUID()}`
  const cartHeaders = unsafeMemberHeaders(requestToken, {
    'X-DoSelect-Guest-Cart-Key': guestCartKey,
  })
  const initialCartResponse = await api.get('/api/v1/cart', { headers: cartHeaders })
  expect(initialCartResponse.ok(), 'The COD E2E setup must create a guest cart').toBe(true)
  const initialCart = await initialCartResponse.json() as CartSnapshot

  const addItemResponse = await api.post('/api/v1/cart/items', {
    headers: cartHeaders,
    data: {
      skuPublicId,
      quantity: 1,
      cartRowVersion: initialCart.rowVersion,
    },
  })
  expect(addItemResponse.ok(), 'The seeded SKU must be eligible for a COD cart').toBe(true)
  const cart = await addItemResponse.json() as CartSnapshot

  const policyResponse = await api.get('/api/v1/checkout/policy-versions')
  expect(policyResponse.ok(), 'Checkout policy versions must be available').toBe(true)
  const policies = await policyResponse.json() as {
    terms: number
    return: number
    privacy: number
  }

  const isHomeDelivery = destination.methodCode === 'HomeDelivery'
  const createResponse = await api.post('/api/v1/orders', {
    headers: unsafeMemberHeaders(requestToken, {
      'X-DoSelect-Guest-Cart-Key': guestCartKey,
      'Idempotency-Key': `e2e-cod-checkout-${randomUUID()}`,
    }),
    data: {
      cartPublicId: cart.publicId,
      cartRowVersion: cart.rowVersion,
      buyer: {
        email,
        name: 'COD 測試買家',
        phone: '0912345678',
      },
      shipping: {
        methodCode: destination.methodCode,
        address: isHomeDelivery
          ? {
              recipientName: 'COD 測試買家',
              phone: '0912345678',
              postalCode: '100',
              city: '台北市',
              district: '中正區',
              addressLine1: '測試路 1 號',
              addressLine2: null,
            }
          : null,
        storePublicId: isHomeDelivery ? null : destination.storePublicId,
        deliveryNote: null,
      },
      paymentMethod: 'cashOnDelivery',
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
  const responseBody = await createResponse.text()
  expect(
    createResponse.status(),
    `The real Checkout endpoint must create the COD order. Response: ${responseBody}`,
  ).toBe(201)

  const order = JSON.parse(responseBody) as CustomerOrderSnapshot
  expect(order.orderStatus).toBe('confirmed')
  expect(order.paymentStatus).toBe('awaitingPayment')
  expect(order.amounts.paidAmount).toBe(0)
  expect(order.paidAtUtc).toBeNull()
  return order
}

async function grantGuestOrderAccess(
  page: Page,
  order: CustomerOrderSnapshot,
  email: string,
): Promise<void> {
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
}

function deriveGuestVerificationCode(requestPublicId: string, sendNumber = 1): string {
  const normalizedId = requestPublicId.replaceAll('-', '').toLowerCase()
  const digest = createHmac('sha256', 'e2e-guest-order-access-pepper-32-bytes')
    .update(`verification-code:${normalizedId}:${sendNumber}`)
    .digest()
  return String(digest.readUInt32BE(0) % 1_000_000).padStart(6, '0')
}

async function shipOrdersThroughAdminUi(
  page: Page,
  orders: readonly CustomerOrderSnapshot[],
): Promise<void> {
  // Keep the authenticated Pinia session established by the real TOTP flow. The existing
  // /orders route does not have auth metadata, so a hard reload there does not restore the
  // session before role-gated batch controls render.
  await page.getByRole('link', { name: '前往訂單管理' }).click()
  await expect(page).toHaveURL(/\/admin\/orders$/)
  await expect(page.getByRole('heading', { level: 1, name: '訂單管理' })).toBeVisible()

  for (const order of orders) {
    await page.getByRole('checkbox', {
      name: `勾選訂單 ${order.orderNumber} 進行批次出貨`,
    }).check()
  }
  await page.getByRole('button', { name: '批次出貨', exact: true }).click()
  await page.getByRole('button', { name: /送出批次/ }).click()
  await expect(page.getByText(`共 ${orders.length} 筆，成功 ${orders.length} 筆、失敗 0 筆。`))
    .toBeVisible()

  await page.getByRole('button', { name: '開始新的批次' }).click()
  await page.getByRole('link', { name: '回訂單管理' }).click()
  for (const order of orders) {
    await page.getByRole('checkbox', {
      name: `勾選訂單 ${order.orderNumber} 進行批次出貨`,
    }).check()
  }
  await page.getByRole('button', { name: '批次出貨', exact: true }).click()
  await page.getByRole('radio', { name: '標記已出貨（扣庫存）' }).check()
  await page.getByRole('button', { name: /送出批次/ }).click()
  await expect(page.getByText(`共 ${orders.length} 筆，成功 ${orders.length} 筆、失敗 0 筆。`))
    .toBeVisible()
}

async function executeShipmentActionThroughAdminUi(
  page: Page,
  buttonLabel: string,
  action: string,
): Promise<ShipmentCommandCapture> {
  await page.getByRole('button', { name: buttonLabel, exact: true }).click()
  const responsePromise = page.waitForResponse(response =>
    response.request().method() === 'POST'
    && new URL(response.url()).pathname.endsWith(`/actions/${action}`))
  await page.getByRole('form', { name: '物流狀態命令' })
    .getByRole('button', { name: '確認更新' })
    .click()
  const response = await responsePromise
  const responseText = await response.text()
  expect(
    response.status(),
    `The admin shipment action '${action}' must succeed. Response: ${responseText}`,
  ).toBe(200)
  const request = response.request()
  const idempotencyKey = request.headers()['idempotency-key']
  expect(idempotencyKey, 'The admin UI must send an Idempotency-Key').toBeTruthy()
  return {
    order: JSON.parse(responseText) as AdminOrderSnapshot,
    idempotencyKey: idempotencyKey!,
    requestBody: request.postDataJSON(),
  }
}

async function readCustomerInvoice(
  page: Page,
  orderPublicId: string,
): Promise<{ status: number, body?: InvoiceSnapshot }> {
  return await page.evaluate(async (publicId) => {
    const response = await fetch(`/api/v1/orders/${publicId}/invoice`, {
      credentials: 'include',
    })
    return {
      status: response.status,
      body: response.ok ? await response.json() as InvoiceSnapshot : undefined,
    }
  }, orderPublicId)
}

test('a seeded administrator can enroll TOTP, reject a wrong code, and sign in again; H-R02 fulfills COD home delivery and store pickup exactly once', async ({
  page,
  api,
  seed,
  browser,
}) => {
  test.setTimeout(180_000)
  if (!seed.adminPassword) {
    throw new Error('Seed__AdminPassword is required for an administrator E2E journey.')
  }

  const requestToken = await getMemberAntiforgeryToken(api)
  const storePublicId = await firstConvenienceStorePublicId(api)
  const skuPublicId = await codEligibleSkuPublicId(api)
  const homeEmail = `cod-home-${randomUUID()}@example.test`
  const storeEmail = `cod-store-${randomUUID()}@example.test`
  const homeOrder = await createGuestCodOrder(
    api,
    skuPublicId,
    homeEmail,
    requestToken,
    { methodCode: 'HomeDelivery' },
  )
  const storeOrder = await createGuestCodOrder(
    api,
    skuPublicId,
    storeEmail,
    requestToken,
    { methodCode: 'StorePickup', storePublicId },
  )

  await page.goto('./')
  await page.getByRole('textbox', { name: '電子郵件' }).fill(seed.adminEmail)
  await page.getByLabel('密碼').fill(seed.adminPassword)
  await page.getByRole('button', { name: '登入' }).click()

  await expect(page).toHaveURL((url) => url.pathname === '/admin/login/enroll')
  await expect(page.getByRole('heading', { level: 1, name: '綁定兩步驟驗證' })).toBeVisible()
  await captureVisualEvidence(page, 'real-admin-totp-enroll')
  const secret = (await page.locator('.totp-secret code').textContent())?.trim()
  expect(secret, 'The enrollment page must expose a manual TOTP secret for the operator').toBeTruthy()

  await page.getByLabel('請輸入 App 顯示的 6 位數驗證碼以確認綁定')
    .fill(currentTotp(secret!))
  await page.getByRole('button', { name: '確認綁定' }).click()

  await expect(page.getByRole('heading', { level: 1, name: '請保存您的備援碼' })).toBeVisible()
  await captureVisualEvidence(page, 'real-admin-recovery-codes-redacted')
  await page.getByRole('checkbox', { name: '我已抄下並妥善保存這些備援碼' }).check()
  await page.getByRole('button', { name: '完成，進入後台' }).click()

  await expect(page).toHaveURL(/\/admin\/$/)
  await expect(page.getByRole('heading', { level: 1, name: '管理工作台' })).toBeVisible()
  await expect(page.getByText('DoSelect 開發管理員', { exact: true })).toBeVisible()
  await captureVisualEvidence(page, 'real-admin-dashboard')
  for (const [url, label] of [['products', 'products'], ['inventory', 'inventory'], ['support', 'support-queue'], ['cases', 'case-workbench'], ['returns', 'returns'], ['refunds', 'refunds'], ['security/totp-rebind', 'totp-rebind']]) {
    await page.goto('./' + url)
    await expect(page.locator('main h1').first()).toBeVisible()
    await captureVisualEvidence(page, 'real-admin-' + label)
  }
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
  await captureVisualEvidence(page, 'real-admin-totp-invalid')

  await page.getByLabel('驗證碼').fill(currentTotp(secret!))
  await page.getByRole('button', { name: '驗證', exact: true }).click()
  await expect(page).toHaveURL(/\/admin\/$/)
  await expect(page.getByText('DoSelect 開發管理員', { exact: true })).toBeVisible()

  const customerContext = await browser.newContext({ baseURL: 'http://127.0.0.1:5173' })
  const customerPage = await customerContext.newPage()

  await grantGuestOrderAccess(customerPage, homeOrder, homeEmail)
  await customerPage.goto(`/orders/${homeOrder.publicId}/payment`)
  const codAttemptPanel = customerPage.getByRole('region', { name: '付款嘗試' })
  await expect(codAttemptPanel).toContainText('貨到付款')
  await expect(codAttemptPanel).toContainText('等待付款')
  await expect(customerPage.locator('[data-test="complete-payment"]')).toHaveCount(0)
  await expect(customerPage.getByText('貨到付款會在完成配送或取貨時入帳，不使用前台模擬付款完成。'))
    .toBeVisible()

  const homeAttempt = await customerPage.evaluate(async (orderPublicId) => {
    const response = await fetch(
      `/api/v1/orders/${orderPublicId}/payment-attempts/latest`,
      { credentials: 'include' },
    )
    if (!response.ok) {
      throw new Error(`Expected the COD attempt to load, got ${response.status}.`)
    }
    return await response.json() as PaymentAttemptSnapshot
  }, homeOrder.publicId)
  expect(homeAttempt.method).toBe('cashOnDelivery')
  expect(homeAttempt.status).toBe('awaitingPayment')

  const prematureCompletion = await customerPage.evaluate(async (attemptPublicId) => {
    const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
      credentials: 'include',
      headers: { 'X-DoSelect-Client': 'member' },
    })
    const token = await tokenResponse.json() as { requestToken: string }
    const response = await fetch(`/api/v1/simulated-payments/${attemptPublicId}/actions/complete`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-DoSelect-Client': 'member',
        'X-XSRF-TOKEN': token.requestToken,
      },
      body: JSON.stringify({ outcome: 'succeeded', simulationKey: crypto.randomUUID() }),
    })
    const body = await response.json() as { code?: string }
    return { status: response.status, code: body.code }
  }, homeAttempt.publicId)
  expect(prematureCompletion).toEqual({ status: 409, code: 'payment_state_conflict' })

  await customerPage.goto(`/orders/${homeOrder.publicId}`)
  await expect(customerPage.getByText('付款狀態：等待付款', { exact: true })).toBeVisible()
  await expect(customerPage.getByText('已付款：NT$ 0', { exact: true })).toBeVisible()
  await expect(customerPage.getByRole('heading', { name: '模擬發票' })).toHaveCount(0)

  await shipOrdersThroughAdminUi(page, [homeOrder, storeOrder])

  await page.goto(`./orders/${homeOrder.publicId}`)
  await expect(page.getByRole('heading', { level: 1, name: `訂單 ${homeOrder.orderNumber}` }))
    .toBeVisible()
  const homeAdminBefore = await page.evaluate(async (orderPublicId) => {
    const response = await fetch(`/api/v1/admin/orders/${orderPublicId}`, { credentials: 'include' })
    if (!response.ok) {
      throw new Error(`Expected the admin order to load, got ${response.status}.`)
    }
    return await response.json() as AdminOrderSnapshot
  }, homeOrder.publicId)
  expect(homeAdminBefore.shipment.status).toBe('Shipped')
  expect(homeAdminBefore.paymentStatus).toBe('AwaitingPayment')

  const anonymousTransition = await api.post(
    `/api/v1/admin/shipments/${homeAdminBefore.shipment.publicId}/actions/in-transit`,
    {
      headers: { 'Idempotency-Key': `anonymous-${randomUUID()}` },
      data: { shipmentRowVersion: homeAdminBefore.shipment.rowVersion },
    },
  )
  expect(anonymousTransition.status(), 'An anonymous actor must not advance a shipment').toBe(401)
  await page.reload()
  await expect(page.getByRole('region', { name: '物流' })).toContainText('已出貨')

  const homeInTransit = await executeShipmentActionThroughAdminUi(page, '配送中', 'in-transit')
  expect(homeInTransit.order.paymentStatus).toBe('AwaitingPayment')
  expect(homeInTransit.order.amounts.paidAmount).toBe(0)
  expect(homeInTransit.order.paidAtUtc).toBeNull()
  expect((await readCustomerInvoice(customerPage, homeOrder.publicId)).status).toBe(404)

  const homeDelivered = await executeShipmentActionThroughAdminUi(page, '宅配送達', 'delivered')
  expect(homeDelivered.order.fulfillmentStatus).toBe('Delivered')
  expect(homeDelivered.order.paymentStatus).toBe('Paid')
  expect(homeDelivered.order.orderStatus).toBe('Completed')
  expect(homeDelivered.order.amounts.paidAmount).toBe(homeDelivered.order.amounts.grandTotal)
  expect(homeDelivered.order.paidAtUtc).toBeTruthy()
  await expect(page.getByText('Paid', { exact: true })).toBeVisible()
  await expect(page.getByText('已付金額', { exact: true }).locator('..'))
    .toContainText(`NT$ ${homeDelivered.order.amounts.grandTotal}`)

  const homeReplay = await page.evaluate(async ({
    shipmentPublicId,
    requestBody,
    idempotencyKey,
  }) => {
    const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
      credentials: 'include',
      headers: { 'X-DoSelect-Client': 'admin' },
    })
    const token = await tokenResponse.json() as { requestToken: string }
    const response = await fetch(
      `/api/v1/admin/shipments/${shipmentPublicId}/actions/delivered`,
      {
        method: 'POST',
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'X-DoSelect-Client': 'admin',
          'X-XSRF-TOKEN': token.requestToken,
        },
        body: JSON.stringify(requestBody),
      },
    )
    return {
      status: response.status,
      body: await response.json() as AdminOrderSnapshot,
    }
  }, {
    shipmentPublicId: homeDelivered.order.shipment.publicId,
    requestBody: homeDelivered.requestBody,
    idempotencyKey: homeDelivered.idempotencyKey,
  })
  expect(homeReplay.status).toBe(200)
  expect(homeReplay.body.paymentStatus).toBe('Paid')
  expect(homeReplay.body.amounts.paidAmount).toBe(homeDelivered.order.amounts.paidAmount)
  expect(homeReplay.body.paidAtUtc).toBe(homeDelivered.order.paidAtUtc)
  expect(homeReplay.body.shipment.rowVersion).toBe(homeDelivered.order.shipment.rowVersion)
  expect(homeReplay.body.shipment.history).toHaveLength(homeDelivered.order.shipment.history.length)

  let homeInvoice: InvoiceSnapshot | undefined
  await expect.poll(async () => {
    const result = await readCustomerInvoice(customerPage, homeOrder.publicId)
    homeInvoice = result.body
    return result.status
  }, {
    message: 'Delivered COD must create one invoice through the outbox consumer',
    timeout: 20_000,
    intervals: [500, 1_000, 2_000],
  }).toBe(200)
  expect(homeInvoice?.orderPublicId).toBe(homeOrder.publicId)
  expect(homeInvoice?.status).toBe('issued')
  expect(homeInvoice?.grossAmount).toBe(homeOrder.amounts.grandTotal)
  const homeInvoiceAfterReplay = await readCustomerInvoice(customerPage, homeOrder.publicId)
  expect(homeInvoiceAfterReplay.body?.publicId).toBe(homeInvoice?.publicId)
  expect(homeInvoiceAfterReplay.body?.invoiceNumber).toBe(homeInvoice?.invoiceNumber)

  await customerPage.reload()
  await expect(customerPage.getByText('付款狀態：已付款', { exact: true })).toBeVisible()
  await expect(customerPage.getByText(`已付款：NT$ ${homeOrder.amounts.grandTotal}`, { exact: true }))
    .toBeVisible()
  await expect(customerPage.getByText(/DEMO-NOT-A-TAX-INVOICE/)).toBeVisible()
  await expect(customerPage.getByText('狀態：已開立', { exact: true })).toBeVisible()

  await grantGuestOrderAccess(customerPage, storeOrder, storeEmail)
  await expect(customerPage.getByText('付款狀態：等待付款', { exact: true })).toBeVisible()
  await expect(customerPage.getByText('已付款：NT$ 0', { exact: true })).toBeVisible()

  await page.goto(`./orders/${storeOrder.publicId}`)
  const storeInTransit = await executeShipmentActionThroughAdminUi(page, '配送中', 'in-transit')
  expect(storeInTransit.order.paymentStatus).toBe('AwaitingPayment')
  expect(storeInTransit.order.amounts.paidAmount).toBe(0)
  const storePickupReady = await executeShipmentActionThroughAdminUi(page, '超商到店', 'pickup-ready')
  expect(storePickupReady.order.fulfillmentStatus).toBe('PickupReady')
  expect(storePickupReady.order.paymentStatus).toBe('AwaitingPayment')
  expect(storePickupReady.order.amounts.paidAmount).toBe(0)
  expect(storePickupReady.order.paidAtUtc).toBeNull()
  expect((await readCustomerInvoice(customerPage, storeOrder.publicId)).status).toBe(404)

  const storePickedUp = await executeShipmentActionThroughAdminUi(page, '顧客取貨', 'picked-up')
  expect(storePickedUp.order.fulfillmentStatus).toBe('PickedUp')
  expect(storePickedUp.order.paymentStatus).toBe('Paid')
  expect(storePickedUp.order.orderStatus).toBe('Completed')
  expect(storePickedUp.order.amounts.paidAmount).toBe(storePickedUp.order.amounts.grandTotal)
  expect(storePickedUp.order.paidAtUtc).toBeTruthy()

  let storeInvoice: InvoiceSnapshot | undefined
  await expect.poll(async () => {
    const result = await readCustomerInvoice(customerPage, storeOrder.publicId)
    storeInvoice = result.body
    return result.status
  }, {
    message: 'Picked-up COD must create one invoice through the outbox consumer',
    timeout: 20_000,
    intervals: [500, 1_000, 2_000],
  }).toBe(200)
  expect(storeInvoice?.orderPublicId).toBe(storeOrder.publicId)
  expect(storeInvoice?.status).toBe('issued')
  expect(storeInvoice?.grossAmount).toBe(storeOrder.amounts.grandTotal)

  await customerPage.reload()
  await expect(customerPage.getByText('狀態：已完成', { exact: true })).toBeVisible()
  await expect(customerPage.getByText('付款狀態：已付款', { exact: true })).toBeVisible()
  await expect(customerPage.getByText(`已付款：NT$ ${storeOrder.amounts.grandTotal}`, { exact: true }))
    .toBeVisible()
  await expect(customerPage.getByText(/DEMO-NOT-A-TAX-INVOICE/)).toBeVisible()
  await customerContext.close()

  // The order detail route currently has no auth metadata, so the hard navigation above does not
  // restore the Pinia session. Entering the guarded home route rehydrates it from the real cookie
  // before exercising the existing logout assertion.
  await page.getByRole('link', { name: '首頁', exact: true }).click()
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
