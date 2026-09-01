import { createHmac } from 'node:crypto'

const BASE32_ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'

function decodeBase32(secret: string): Buffer {
  const clean = secret.toUpperCase().replace(/[^A-Z2-7]/g, '')
  let bits = ''
  for (const char of clean) {
    const value = BASE32_ALPHABET.indexOf(char)
    if (value === -1) {
      continue
    }
    bits += value.toString(2).padStart(5, '0')
  }

  const bytes: number[] = []
  for (let i = 0; i + 8 <= bits.length; i += 8) {
    bytes.push(Number.parseInt(bits.slice(i, i + 8), 2))
  }
  return Buffer.from(bytes)
}

/**
 * RFC 6238 TOTP (SHA1, 6 digits, 30s step) — matches the backend's admin enrollment/verification
 * provider (see DoSelect.Api.IntegrationTests/Admin/TotpTestHelper.cs), so a code computed here
 * from the `secretKey` an enrollment/rebind screen displays is accepted by the real API.
 */
export function generateTotpCode(secretBase32: string, atUtc: Date = new Date()): string {
  const key = decodeBase32(secretBase32)
  const counter = Math.floor(atUtc.getTime() / 1000 / 30)
  const counterBuffer = Buffer.alloc(8)
  counterBuffer.writeBigUInt64BE(BigInt(counter))

  const hmac = createHmac('sha1', key).update(counterBuffer).digest()
  const offset = hmac[hmac.length - 1]! & 0x0f
  const binary =
    ((hmac[offset]! & 0x7f) << 24) |
    ((hmac[offset + 1]! & 0xff) << 16) |
    ((hmac[offset + 2]! & 0xff) << 8) |
    (hmac[offset + 3]! & 0xff)

  return (binary % 1_000_000).toString().padStart(6, '0')
}
