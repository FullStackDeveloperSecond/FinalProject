import { createHmac } from 'node:crypto'

/**
 * Matches the backend's GuestOrderAccessHasher.DeriveVerificationCode (HMAC-SHA256, keyed by the
 * GuestOrderAccess:Pepper config value): digest = HMACSHA256(pepper, UTF8(`verification-code:
 * {requestPublicId:N}:{sendNumber}`)); take the first 4 bytes of the digest as a big-endian
 * uint32, mod 1_000_000, zero-padded to 6 digits. playwright.config.ts pins the E2E pepper to a
 * known fixed value, so this can be computed here without ever reading an email.
 */
export function deriveGuestOrderVerificationCode(
  requestPublicId: string,
  sendNumber: number,
  pepper: string,
): string {
  const requestPublicIdN = requestPublicId.replace(/-/g, '').toLowerCase()
  const message = `verification-code:${requestPublicIdN}:${sendNumber}`
  const digest = createHmac('sha256', Buffer.from(pepper, 'utf-8'))
    .update(Buffer.from(message, 'utf-8'))
    .digest()
  const value = digest.readUInt32BE(0)
  return (value % 1_000_000).toString().padStart(6, '0')
}
