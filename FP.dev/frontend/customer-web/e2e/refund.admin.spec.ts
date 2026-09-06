import { createHmac } from 'node:crypto'
import type { Page } from '@playwright/test'
import { expect, test } from './fixtures.js'

// M-13 WP4（alex 2026-09-05 #98 A1～D1 裁定；#111 合併後依 #98 執行順序第 5 點更新）：
// 依既有 A1 裁定保留穩定、隔離的前置資料——訂單、付款、出貨由 --seed-minimal 頂住（見
// MinimalDevelopmentDataSeeder.EnsureRefundJourneyOrderAsync），讓這支測試專注在退貨申請
// 開始之後的 Return／Refund／Allowance 全程 production API／UI 路徑，不需要因此重寫成
// 涵蓋物流狀態命令的完整垂直旅程（那條垂直路徑的證據在 #117）。
// 從建立退貨申請開始，這支測試全程走 production API／UI，不 seed 任何 Return／Refund 狀態：
// 建立退貨申請（API）→ 審核／收貨（A-21 UI）→ 檢查（A-21 UI，assemblyFeeDisposition／
// returnShippingCost 兩個欄位已隨 #111 補進 UI，不再用 API 繞過）→ 驗證這筆 Return 只建立了
// 唯一一筆 PendingReview Refund → 核准／執行（A-22 UI，斷言分攤方向與合計）→ 驗證 Return
// 完成 → 開立折讓（API，見 C1：Invoice UI 目前只顯示折讓，沒有建立表單；這裡的手動開票只
// 證明「管理員開票 API」這條路徑，不代表付款 Outbox／Consumer 自動開票鏈路——那條鏈路的證據
// 在 #117，見 alex 2026-09-05 #98 裁定第 2 點）。
//
// 管理員登入用的是 seed 階段就已寫死綁定 TOTP 秘鑰的獨立帳號（refundJourneyAdminEmail／
// refundJourneyAdminTotpSecret，見 MinimalDevelopmentDataSeeder），不在這支測試裡跑一次性
// 的 UI 綁定流程——退款旅程不需要驗證「綁定」這個能力本身（admin.spec.ts 已有專門測試），
// 用已知秘鑰能讓 Playwright CI 的內建 retry 安全重跑（alex 2026-09-05 #98 review P3）。

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
/// 審核／收貨／檢查／核准／執行都走 A-21／A-22 真實 UI；折讓建立仍是 admin-web 既有的
/// UI 缺口（alex C1 裁定：另案處理，這支測試先直接呼叫 production API）。
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

test('a finance administrator approves, executes and issues an allowance via the manual invoice API for a production-created refund', async ({
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

  // ── 管理員：登入（全程沿用同一個 page，後面所有動作共用這個登入態）。用獨立的
  // refundJourneyAdminEmail，不是共用的 seed.adminEmail——同一輪 CI 的 admin-chromium
  // 專案單一 worker 依序跑完所有 spec，admin.spec.ts 自己的 TOTP 綁定測試會先把共用帳號
  // 綁定掉。這個帳號的 TOTP 秘鑰在 seed 階段就已經寫死綁定（見
  // MinimalDevelopmentDataSeeder.EnsureRefundJourneyOrderAsync 與
  // seed.refundJourneyAdminTotpSecret），不在這支測試裡跑一次性的 UI 綁定流程——退款旅程
  // 不需要驗證「綁定」這個能力本身（admin.spec.ts 已經有專門測試），用已知秘鑰讓登入本身
  // 可以安全重試：Playwright CI 的內建 retry 只需要用同一把秘鑰重新算一次 TOTP code，
  // 不會像走一次性 enroll 畫面那樣，秘鑰只活在第一次成功的畫面上，重試落在 verify 頁面
  // 就必然失敗、遮蔽原始錯誤（alex 2026-09-05 #98 review P3）。 ──────────────────
  if (!seed.adminPassword) {
    throw new Error('Seed__AdminPassword is required for an administrator E2E journey.')
  }

  await page.goto('./')
  await page.getByRole('textbox', { name: '電子郵件' }).fill(seed.refundJourneyAdminEmail)
  await page.getByLabel('密碼').fill(seed.adminPassword)
  await page.getByRole('button', { name: '登入' }).click()

  await expect(page).toHaveURL((url) => url.pathname === '/admin/login/verify')
  await page.getByLabel('驗證碼').fill(currentTotp(seed.refundJourneyAdminTotpSecret))
  await page.getByRole('button', { name: '驗證', exact: true }).click()
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

  // ── 檢查：改走 A-21 真實 UI——assemblyFeeDisposition／returnShippingCost 兩個欄位已隨
  // #111 補進「商品檢查」表單，不再需要 API 繞過（alex 2026-09-05 #98 裁定第 5 點）。 ──
  await page.getByLabel('回補判定').selectOption('quarantine')
  await page.getByLabel('組裝費處置').selectOption('notApplicable')
  await page.getByLabel('退貨運費').fill('0.00')
  await page.getByRole('button', { name: '送出檢查結果' }).click()

  await expect(page.getByText('等待退款', { exact: true })).toBeVisible()

  // ── 驗證這筆 Return 自己只建立了唯一一筆 PendingReview Refund——查詢先按 returnPublicId
  // 篩到這次的 Return，而不是斷言「整個系統只有一筆待審退款」（alex 2026-09-05 #98 review
  // P2：全系統唯一性跟其他 E2E 或並行資料無關，篩錯清單也可能誤拿到不屬於這次 Return 的
  // 退款）。 ──────────────────────────────────────────────────────────────
  const pendingRefunds = await browserFetch<{ items: Array<{ publicId: string, returnPublicId: string | null }> }>(
    page, 'admin', 'GET', '/api/v1/admin/refunds?Statuses=pendingReview&PageSize=50')
  expect(pendingRefunds.status).toBe(200)
  const refundsForThisReturn = pendingRefunds.body.items.filter(
    (item) => item.returnPublicId === createdReturn.publicId)
  expect(
    refundsForThisReturn,
    'Exactly one PendingReview Refund must exist for this Return — the one just staged by Inspect',
  ).toHaveLength(1)
  const refundPublicId = refundsForThisReturn[0]!.publicId

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
  // API；驗證則回到 Invoice UI 真的點。這裡呼叫的是「管理員手動開票」端點——只證明
  // POST /admin/orders/{id}/invoices 這條 API 本身能開票並掛上折讓，不代表付款完成後
  // Outbox／Consumer 自動開票那條鏈路有被驗證過；那條鏈路的整合證據在 #117
  // （alex 2026-09-05 #98 裁定第 2 點：兩種開票路徑不可混為同一項驗收）。──────────
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
    `The admin manual invoice-issuance API must succeed (this is not the payment-Outbox auto-invoice path — see #117): ${JSON.stringify(issuedInvoice.body)}`,
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
