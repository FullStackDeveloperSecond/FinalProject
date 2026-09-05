import { createHmac } from 'node:crypto'
import type { Page } from '@playwright/test'
import { expect, test } from './fixtures.js'

// M-13 WP4（alex 2026-09-05 #98 A1～D1 裁定）：訂單、付款、出貨等前置資料由 --seed-minimal
// 頂住（見 MinimalDevelopmentDataSeeder.EnsureRefundJourneyOrderAsync）——目前 production 沒有
// 任何 HTTP 可達的路徑能把訂單推進 Delivered，那個缺口屬於物流範圍，另案處理（#98）。
// 從建立退貨申請開始，這支測試全程走 production API／UI，不 seed 任何 Return／Refund 狀態：
// 建立退貨申請（API）→ 審核／收貨（A-21 UI）→ 檢查（API，UI 缺少 assemblyFeeDisposition／
// returnShippingCost 兩個必要欄位，另案追蹤，見 D1）→ 驗證系統自己建立唯一的 PendingReview
// Refund → 核准／執行（A-22 UI，斷言分攤方向與合計）→ 驗證 Return 完成 → 開立折讓（API，
// 見 C1：Invoice UI 目前只顯示折讓，沒有建立表單）。
//
// 全程只用一個 test()：TOTP 只能在第一次登入時綁定，第二個 test() 若並行跑同一個管理員會撞
// requiresEnrollment=false 卻沒有金鑰可用（playwright.config.ts 的 admin-chromium 專案
// fullyParallel），因此整段旅程刻意收在同一個 test 裡，一次登入重複使用同一把 TOTP 金鑰。

const guestAccessPepper = 'e2e-guest-order-access-pepper-32-bytes'
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

function deriveGuestVerificationCode(requestPublicId: string, sendNumber = 1): string {
  const normalizedId = requestPublicId.replaceAll('-', '').toLowerCase()
  const digest = createHmac('sha256', guestAccessPepper)
    .update(`verification-code:${normalizedId}:${sendNumber}`)
    .digest()
  return String(digest.readUInt32BE(0) % 1_000_000).padStart(6, '0')
}

/// <summary>
/// 這支測試混合了三種呼叫方式：guest-order-access 的 Cookie 在 E2E 環境是 Secure
/// Cookie（非 Development），標準的 <c>APIRequestContext</c> 不會像瀏覽器一樣把
/// 127.0.0.1／localhost 當成可信任的安全來源，收到後不會在下一次純 HTTP 請求帶回去——
/// 所以 guest 與 admin 兩段都必須讓 <c>page</c> 自己的瀏覽器 fetch 發球，Cookie 才留得住。
/// 核准／執行走 A-22 真實 UI；檢查（inspect）與折讓建立則是 admin-web 既有的欄位缺口／
/// 缺少的 UI（alex D1／C1 裁定：另案處理，這支測試先直接呼叫 production API）。
/// </summary>
async function browserFetch<T>(
  page: Page,
  client: 'member' | 'admin',
  method: 'GET' | 'POST',
  path: string,
  body?: unknown,
): Promise<{ status: number, body: T }> {
  const result = await page.evaluate(async ({ client, method, path, body }) => {
    const headers: Record<string, string> = { 'X-DoSelect-Client': client }
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json'
      const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
        credentials: 'include',
        headers: { 'X-DoSelect-Client': client },
      })
      const token = await tokenResponse.json() as { requestToken: string }
      headers['X-XSRF-TOKEN'] = token.requestToken
    }

    const response = await fetch(path, {
      method,
      credentials: 'include',
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
    return { status: response.status, body: await response.json() }
  }, { client, method, path, body })
  return result as { status: number, body: T }
}

interface ReturnRequestSnapshot {
  publicId: string
  returnNumber: string
  status: string
  rowVersion: string
  items: Array<{ publicId: string }>
}

test('a finance administrator approves, executes and issues an allowance for a production-created refund', async ({
  page,
  seed,
}) => {
  test.setTimeout(90_000)

  // ── 顧客：透過 production API 建立退貨申請（前置的訂單／付款／出貨由 seed 頂住，
  // 見檔案開頭說明）。全程用瀏覽器自己的 fetch（見 browserFetch 的說明：Secure Cookie
  // 在 E2E 環境需要一個真正的瀏覽器才留得住）。 ─────────────────────────────────
  await page.goto('http://127.0.0.1:5173/')

  const accessRequest = await browserFetch<{ requestPublicId: string }>(
    page, 'member', 'POST', '/api/v1/guest-orders/access-requests',
    { orderNumber: seed.refundJourneyOrderNumber, email: seed.refundJourneyBuyerEmail })
  expect(accessRequest.status, 'The guest access request must be accepted').toBe(202)
  const { requestPublicId } = accessRequest.body

  const verifyResult = await browserFetch(
    page, 'member', 'POST', '/api/v1/guest-orders/access-verifications',
    { requestPublicId, code: deriveGuestVerificationCode(requestPublicId) })
  expect(verifyResult.status, 'The guest verification code must be accepted').toBe(200)

  const orderSnapshot = await browserFetch<{ rowVersion: string }>(
    page, 'member', 'GET', `/api/v1/orders/${seed.refundJourneyOrderPublicId}`)
  expect(orderSnapshot.status, 'The delivered seed order must be readable by the verified guest').toBe(200)

  const createReturnResult = await browserFetch<ReturnRequestSnapshot>(
    page, 'member', 'POST', `/api/v1/orders/${seed.refundJourneyOrderPublicId}/returns`,
    {
      items: [{
        orderItemPublicId: seed.refundJourneyOrderItemPublicId,
        quantity: 1,
        reasonCode: 'Defective',
        description: 'E2E refund journey return',
      }],
      requestReason: 'Defective',
      orderRowVersion: orderSnapshot.body.rowVersion,
    },
  )
  expect(createReturnResult.status, 'The production Return creation path must succeed').toBe(201)
  const createdReturn = createReturnResult.body

  // ── 管理員：登入並綁定 TOTP（全程沿用同一個 page，後面所有動作共用這個登入態） ──
  if (!seed.adminPassword) {
    throw new Error('Seed__AdminPassword is required for an administrator E2E journey.')
  }

  await page.goto('./')
  await page.getByRole('textbox', { name: '電子郵件' }).fill(seed.adminEmail)
  await page.getByLabel('密碼').fill(seed.adminPassword)
  await page.getByRole('button', { name: '登入' }).click()

  await expect(page).toHaveURL((url) => url.pathname === '/admin/login/enroll')
  const secret = (await page.locator('.totp-secret code').textContent())?.trim()
  expect(secret, 'The enrollment page must expose a manual TOTP secret').toBeTruthy()
  await page.getByLabel('請輸入 App 顯示的 6 位數驗證碼以確認綁定').fill(currentTotp(secret!))
  await page.getByRole('button', { name: '確認綁定' }).click()

  await expect(page.getByRole('heading', { level: 1, name: '請保存您的備援碼' })).toBeVisible()
  await page.getByRole('checkbox', { name: '我已抄下並妥善保存這些備援碼' }).check()
  await page.getByRole('button', { name: '完成，進入後台' }).click()
  await expect(page).toHaveURL(/\/admin\/$/)

  // ── 審核：走 A-21 真實表單，保留預設「需要寄回檢查」，不需要額外欄位（alex D1） ──
  await page.goto(`./returns/${createdReturn.publicId}`)
  await expect(page.getByRole('heading', { level: 1 })).toContainText(createdReturn.returnNumber)

  await page.getByLabel('理由代碼').fill('return_approved')
  await page.getByRole('button', { name: '核准' }).click()
  await expect(page.getByText('等待寄回', { exact: true })).toBeVisible()

  // ── 收貨：同樣是 A-21 真實表單 ──────────────────────────────────────────────
  await page.getByRole('button', { name: '確認收貨' }).click()
  await expect(page.getByRole('heading', { level: 2, name: '商品檢查' })).toBeVisible()

  // ── 檢查：admin-web 的檢查表單沒有 assemblyFeeDisposition／returnShippingCost 欄位，
  // 但這條路徑建立 Refund 一定需要這兩個值（alex D1：另開 Issue／PR 修 UI，這支測試先
  // 直接呼叫 production API 補上）。 ─────────────────────────────────────────
  const preInspect = await browserFetch<{ return: ReturnRequestSnapshot }>(
    page, 'admin', 'GET', `/api/v1/admin/returns/${createdReturn.publicId}`)
  expect(preInspect.status).toBe(200)
  const returnItemPublicId = preInspect.body.return.items[0]!.publicId

  const inspectResult = await browserFetch<ReturnRequestSnapshot>(
    page, 'admin', 'POST', `/api/v1/admin/returns/${createdReturn.publicId}/actions/inspect`,
    {
      items: [{
        returnItemPublicId,
        conditionCode: 'Unopened',
        disposition: 'quarantine',
        note: null,
      }],
      returnRowVersion: preInspect.body.return.rowVersion,
      assemblyFeeDisposition: 'notApplicable',
      returnShippingCost: 0,
    },
  )
  expect(inspectResult.status, 'The production Inspect action must create the Refund').toBe(200)
  expect(inspectResult.body.status).toBe('awaitingRefund')

  // ── 驗證系統自己建立了唯一的 PendingReview Refund（不是這支測試 seed 出來的） ──
  const pendingRefunds = await browserFetch<{ items: Array<{ publicId: string }> }>(
    page, 'admin', 'GET', '/api/v1/admin/refunds?Statuses=pendingReview&PageSize=50')
  expect(pendingRefunds.status).toBe(200)
  expect(
    pendingRefunds.body.items,
    'Exactly one PendingReview Refund must exist — the one just staged by Inspect',
  ).toHaveLength(1)
  const refundPublicId = pendingRefunds.body.items[0]!.publicId

  // ── 核准：A-22 真實 UI，斷言核准金額 = 商品退款 + 原運費（全額退貨，無扣回） ──
  await page.goto(`./refunds/${refundPublicId}`)
  await expect(page.getByRole('heading', { level: 1 })).toContainText('退款')

  await page.getByLabel('核准原因').selectOption('return_approved')
  await page.getByRole('checkbox', { name: /我已核對申請金額與訂單/ }).check()
  await page.getByRole('button', { name: '確認核准退款' }).click()

  await expect(page.getByText('已核准', { exact: true })).toBeVisible()
  await expect(page.locator('dt:has-text("退款上限（核准金額）") + dd')).toHaveText('NT$20,000')

  // ── 執行：A-22 真實 UI，斷言分攤方向與合計 ──────────────────────────────────
  await page.getByLabel('執行原因').selectOption('return_approved')
  await page.getByRole('checkbox', { name: /我已核對退款上限、分攤正負方向與訂單/ }).check()
  await page.getByRole('button', { name: '確認執行退款' }).click()

  await expect(page.getByText('退款成功', { exact: true })).toBeVisible()
  await expect(page.locator('tr').filter({ hasText: '商品退款' })).toContainText('+NT$19,900')
  await expect(page.locator('tr').filter({ hasText: '原訂單運費退還' })).toContainText('+NT$100')

  // ── 驗證關聯 Return 已完成（不是停在 AwaitingRefund） ─────────────────────────
  await page.goto(`./returns/${createdReturn.publicId}`)
  await expect(page.getByText('已完成', { exact: true })).toBeVisible()

  // ── 折讓：目前沒有任何 admin-web UI 能建立折讓（alex C1），直接呼叫既有 production
  // API；驗證則回到 Invoice UI 真的點。 ──────────────────────────────────────
  const adminOrder = await browserFetch<{ rowVersion: string }>(
    page, 'admin', 'GET', `/api/v1/admin/orders/${seed.refundJourneyOrderPublicId}`)
  expect(adminOrder.status).toBe(200)

  const issuedInvoice = await page.evaluate(async ({ orderId, orderRowVersion }) => {
    const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
      credentials: 'include',
      headers: { 'X-DoSelect-Client': 'admin' },
    })
    const token = await tokenResponse.json() as { requestToken: string }
    const response = await fetch(`/api/v1/admin/orders/${orderId}/invoices`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-DoSelect-Client': 'admin',
        'X-XSRF-TOKEN': token.requestToken,
        'Idempotency-Key': `e2e-refund-journey-invoice-${orderId}`,
      },
      body: JSON.stringify({ orderRowVersion }),
    })
    return { status: response.status, body: await response.json() }
  }, { orderId: seed.refundJourneyOrderPublicId, orderRowVersion: adminOrder.body.rowVersion })
  expect(
    issuedInvoice.status,
    `The manual invoice-issuance path must succeed: ${JSON.stringify(issuedInvoice.body)}`,
  ).toBe(201)
  const invoice = issuedInvoice.body as {
    invoice: { publicId: string, rowVersion: string }
  }

  const allowance = await page.evaluate(async ({ invoicePublicId, invoiceRowVersion, refundPublicId }) => {
    const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
      credentials: 'include',
      headers: { 'X-DoSelect-Client': 'admin' },
    })
    const token = await tokenResponse.json() as { requestToken: string }
    const response = await fetch(`/api/v1/admin/invoices/${invoicePublicId}/allowances`, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-DoSelect-Client': 'admin',
        'X-XSRF-TOKEN': token.requestToken,
        'Idempotency-Key': `e2e-refund-journey-allowance-${invoicePublicId}`,
      },
      body: JSON.stringify({ refundPublicId, invoiceRowVersion }),
    })
    return { status: response.status, body: await response.json() }
  }, {
    invoicePublicId: invoice.invoice.publicId,
    invoiceRowVersion: invoice.invoice.rowVersion,
    refundPublicId,
  })
  expect(allowance.status, 'The Invoice Allowance creation must succeed').toBe(201)

  await page.goto(`./invoices/${invoice.invoice.publicId}`)
  await expect(page.getByRole('region', { name: '折讓' })).toContainText('$20,000.00')
})
