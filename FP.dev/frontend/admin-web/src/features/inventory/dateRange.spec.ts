import { describe, expect, it } from 'vitest'
import { endOfLocalDayExclusiveBoundary, startOfLocalDay } from './dateRange'

/**
 * These assert behavioral invariants (span length, day-to-day shift) rather than hardcoded
 * absolute ISO strings, so they hold regardless of which timezone the test runner's Node
 * process happens to be in — the whole point of the fix is correctness across timezones
 * (組長 PR #37 review, item 2), not correctness under one specific one.
 */
describe('dateRange', () => {
  it('startOfLocalDay and endOfLocalDayExclusiveBoundary for the same date span exactly one day minus 1ms', () => {
    const from = startOfLocalDay('2026-08-25')
    const to = endOfLocalDayExclusiveBoundary('2026-08-25')

    expect(to.getTime() - from.getTime()).toBe(24 * 60 * 60 * 1000 - 1)
  })

  it('endOfLocalDayExclusiveBoundary is exactly 1ms before the next local day starts', () => {
    const to = endOfLocalDayExclusiveBoundary('2026-08-25')
    const nextDayStart = startOfLocalDay('2026-08-26')

    expect(nextDayStart.getTime() - to.getTime()).toBe(1)
  })

  it('shifting the input date by one day shifts both boundaries by exactly 24 hours', () => {
    const fromDay1 = startOfLocalDay('2026-08-25')
    const fromDay2 = startOfLocalDay('2026-08-26')
    const toDay1 = endOfLocalDayExclusiveBoundary('2026-08-25')
    const toDay2 = endOfLocalDayExclusiveBoundary('2026-08-26')

    expect(fromDay2.getTime() - fromDay1.getTime()).toBe(24 * 60 * 60 * 1000)
    expect(toDay2.getTime() - toDay1.getTime()).toBe(24 * 60 * 60 * 1000)
  })

  it('a whole-day range for a single date includes an occurrence at 23:59:59.999 local time but excludes the first millisecond of the next day', () => {
    const to = endOfLocalDayExclusiveBoundary('2026-08-25')
    const lastMomentOfDay = new Date(2026, 7, 25, 23, 59, 59, 999)
    const firstMomentOfNextDay = new Date(2026, 7, 26, 0, 0, 0, 0)

    expect(to.getTime()).toBe(lastMomentOfDay.getTime())
    expect(to.getTime()).toBeLessThan(firstMomentOfNextDay.getTime())
  })
})
