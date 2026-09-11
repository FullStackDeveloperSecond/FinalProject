import { formatTaipeiDate, formatTaipeiDateTime, TAIPEI_TIME_ZONE } from '@doselect/web-shared/datetime'
import { describe, expect, it } from 'vitest'

describe('Taipei date presentation', () => {
  it('uses Asia/Taipei even when the runtime timezone differs', () => {
    const instant = '2026-09-10T16:30:00Z'

    expect(TAIPEI_TIME_ZONE).toBe('Asia/Taipei')
    expect(formatTaipeiDate(instant)).toContain('2026')
    expect(formatTaipeiDate(instant)).toContain('9月11日')
    expect(formatTaipeiDateTime(instant)).toContain('凌晨12:30')
  })

  it('uses a stable empty marker for missing or invalid values', () => {
    expect(formatTaipeiDateTime(null)).toBe('—')
    expect(formatTaipeiDate('not-a-date')).toBe('—')
  })
})
