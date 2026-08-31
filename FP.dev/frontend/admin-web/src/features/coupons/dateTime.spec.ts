import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { toLocalInputValue, toUtcInstant } from './dateTime'

/**
 * Node 會在執行期讀 `TZ`，所以可以直接驗真正的時區行為，不用注入偏移量。
 *
 * 一定要指定非 UTC 時區：CI 跑在 UTC，而錯的寫法在 UTC 下剛好是對的。
 */
function useTimeZone(timeZone: string) {
  vi.stubEnv('TZ', timeZone)
}

beforeEach(() => {
  useTimeZone('Asia/Taipei')
})

afterEach(() => {
  vi.unstubAllEnvs()
})

describe('toLocalInputValue', () => {
  it('shows a UTC instant as the matching local wall-clock time', () => {
    // UTC+8：世界標準時間的 00:00 是本地的 08:00。直接把 UTC 數字切下來
    // （舊寫法）會顯示 00:00，等於把時段整整提前八小時。
    expect(toLocalInputValue('2026-09-01T00:00:00Z')).toBe('2026-09-01T08:00')
  })

  it('uses the offset in effect at that instant, not today', () => {
    // 紐約在九月是 DST（UTC-4）、一月不是（UTC-5）。用固定偏移量會錯一小時。
    useTimeZone('America/New_York')

    expect(toLocalInputValue('2026-09-01T12:00:00Z')).toBe('2026-09-01T08:00')
    expect(toLocalInputValue('2026-01-01T12:00:00Z')).toBe('2026-01-01T07:00')
  })
})

describe('toUtcInstant', () => {
  it('round-trips an untouched value back to the very same instant', () => {
    // alex review P1#2 的驗收條件：不改日期時，送回的 instant 必須與原值相同。
    const original = '2026-09-01T00:00:00Z'

    expect(toUtcInstant(toLocalInputValue(original), original)).toBe(original)
  })

  it('keeps seconds that datetime-local cannot represent', () => {
    // 輸入框只到分鐘。單純往返一趟會把秒抹掉，只改名稱的編輯不該改到時段。
    const original = '2026-09-01T00:00:37Z'

    expect(toUtcInstant(toLocalInputValue(original), original)).toBe(original)
  })

  it('converts an edited local value back to UTC', () => {
    expect(toUtcInstant('2026-09-01T08:30', '2026-09-01T00:00:00Z'))
      .toBe('2026-09-01T00:30:00.000Z')
  })

  it('treats a value with no original as local time', () => {
    // 建立新券時沒有原值可以比對。
    expect(toUtcInstant('2026-09-01T08:00')).toBe('2026-09-01T00:00:00.000Z')
  })

  it('round-trips in a timezone behind UTC too', () => {
    useTimeZone('America/New_York')
    const original = '2026-09-01T00:00:00Z'

    expect(toLocalInputValue(original)).toBe('2026-08-31T20:00')
    expect(toUtcInstant(toLocalInputValue(original), original)).toBe(original)
  })
})
