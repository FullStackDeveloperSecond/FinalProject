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

  it('treats API date-time strings without an offset as UTC', () => {
    expect(formatTaipeiDateTime('2026-09-11T06:19:24.155')).toContain('下午2:19')
    expect(formatTaipeiDateTime('2026-09-11T14:19:24.155+08:00')).toContain('下午2:19')
  })

  it('uses a stable empty marker for missing or invalid values', () => {
    expect(formatTaipeiDateTime(null)).toBe('—')
    expect(formatTaipeiDate('not-a-date')).toBe('—')
  })
})
