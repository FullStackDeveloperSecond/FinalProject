import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { endOfLocalDayExclusiveBoundary, startOfLocalDay } from './dateRange'

// tsconfig.app.json is browser-typed (types: ["vite/client"], no @types/node), but these tests
// run under vitest's Node runtime and must set the real TZ environment variable — declare the
// one member of  they touch instead of pulling in the whole Node type surface.
declare const process: { env: Record<string, string | undefined> }

/**
 * 組長 PR #37 round-2 review, item 4: a fixed "+24h" end boundary is wrong on DST-switching days —
 * America/New_York's 2026-03-08 (spring forward) is 23 hours long, so +24h leaked the first hour
 * of 03-09 into the filter; 2026-11-01 (fall back) is 25 hours long, so +24h dropped that day's
 * last hour. The boundary must be "next local midnight − 1 ms", with the platform's own calendar
 * arithmetic resolving what "next midnight" means.
 *
 * Node re-reads process.env.TZ on date construction (verified on both the Linux CI runner and
 * local Windows Node 22), so these tests pin the zone explicitly rather than depending on the
 * machine's own — Asia/Taipei has no DST and would let the old implementation pass.
 */
describe('dateRange under DST (America/New_York)', () => {
  let originalTz: string | undefined

  beforeAll(() => {
    originalTz = process.env.TZ
    process.env.TZ = 'America/New_York'
  })

  afterAll(() => {
    if (originalTz === undefined) {
      delete process.env.TZ
    }
    else {
      process.env.TZ = originalTz
    }
  })

  it('runs under a DST-observing zone (guard for the environment itself)', () => {
    // Jan is UTC-5 (300), Jul is UTC-4 (240) in New York — if this fails, TZ was not applied and
    // the assertions below would not exercise DST at all.
    expect(new Date(2026, 0, 1, 12).getTimezoneOffset()).toBe(300)
    expect(new Date(2026, 6, 1, 12).getTimezoneOffset()).toBe(240)
  })

  it('spring forward: the 23-hour 2026-03-08 ends 1ms before the true next midnight, not +24h', () => {
    const boundary = endOfLocalDayExclusiveBoundary('2026-03-08')
    const nextMidnight = startOfLocalDay('2026-03-09')

    expect(boundary.getTime()).toBe(nextMidnight.getTime() - 1)
    // The day is 23 hours long — the old "+24h" boundary sat a full hour past next midnight,
    // leaking 03-09 00:00–01:00 into a filter for 03-08.
    expect(nextMidnight.getTime() - startOfLocalDay('2026-03-08').getTime()).toBe(23 * 60 * 60 * 1000)
  })

  it('fall back: the 25-hour 2026-11-01 keeps its final hour inside the boundary', () => {
    const boundary = endOfLocalDayExclusiveBoundary('2026-11-01')
    const nextMidnight = startOfLocalDay('2026-11-02')

    expect(boundary.getTime()).toBe(nextMidnight.getTime() - 1)
    // 25-hour day — the old "+24h" boundary fell an hour short, dropping 23:00–24:00 local.
    expect(nextMidnight.getTime() - startOfLocalDay('2026-11-01').getTime()).toBe(25 * 60 * 60 * 1000)
  })

  it('a plain non-DST day still ends exactly 1ms before its next midnight', () => {
    expect(endOfLocalDayExclusiveBoundary('2026-08-25').getTime())
      .toBe(startOfLocalDay('2026-08-26').getTime() - 1)
  })
})
