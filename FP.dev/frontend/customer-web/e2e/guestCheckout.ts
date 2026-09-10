import { createHmac } from 'node:crypto'
import type { Page } from '@playwright/test'

const customerOrigin = 'http://127.0.0.1:5173'
const guestAccessPepper = 'e2e-guest-order-access-pepper-32-bytes'

export interface BrowserJsonResponse<T> {
  status: number
  body: T
  bodyText: string
}

function deriveGuestVerificationCode(requestPublicId: string, sendNumber = 1): string {
  const normalizedId = requestPublicId.replaceAll('-', '').toLowerCase()
  const digest = createHmac('sha256', guestAccessPepper)
    .update(`verification-code:${normalizedId}:${sendNumber}`)
    .digest()
  return String(digest.readUInt32BE(0) % 1_000_000).padStart(6, '0')
}

export async function postBrowserJson<T>(
  page: Page,
  path: string,
  body: unknown,
  extraHeaders: Record<string, string> = {},
): Promise<BrowserJsonResponse<T>> {
  const result = await page.evaluate(async ({ path, body, extraHeaders }) => {
    const tokenResponse = await fetch('/api/v1/security/antiforgery-token', {
      credentials: 'include',
      headers: { 'X-DoSelect-Client': 'member' },
    })
    const tokenBody = await tokenResponse.json() as { requestToken?: string }
    if (!tokenResponse.ok || !tokenBody.requestToken) {
      throw new Error(`Could not obtain an antiforgery token (${tokenResponse.status}).`)
    }

    const response = await fetch(path, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-DoSelect-Client': 'member',
        'X-XSRF-TOKEN': tokenBody.requestToken,
        ...extraHeaders,
      },
      body: JSON.stringify(body),
    })
    const bodyText = await response.text()
    return {
      status: response.status,
      bodyText,
      body: bodyText ? JSON.parse(bodyText) : null,
    }
  }, { path, body, extraHeaders })

  return result as BrowserJsonResponse<T>
}

export async function establishGuestCheckoutEmailProof(
  page: Page,
  email: string,
  guestCartKey: string,
): Promise<void> {
  if (!page.url().startsWith(customerOrigin)) {
    await page.goto(`${customerOrigin}/`)
  }

  const headers = { 'X-DoSelect-Guest-Cart-Key': guestCartKey }
  const request = await postBrowserJson<{ requestPublicId: string }>(
    page,
    '/api/v1/checkout/guest-email/verification-requests',
    { email },
    headers,
  )
  if (request.status !== 202 || !request.body.requestPublicId) {
    throw new Error(
      `Guest checkout email verification request failed (${request.status}): ${request.bodyText}`,
    )
  }

  const verification = await postBrowserJson(
    page,
    '/api/v1/checkout/guest-email/verifications',
    {
      requestPublicId: request.body.requestPublicId,
      code: deriveGuestVerificationCode(request.body.requestPublicId),
    },
    headers,
  )
  if (verification.status !== 200) {
    throw new Error(
      `Guest checkout email verification failed (${verification.status}): ${verification.bodyText}`,
    )
  }
}
