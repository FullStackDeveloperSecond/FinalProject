/**
 * `<input type="date">` gives a bare "YYYY-MM-DD" string with no timezone. Converting it with
 * `new Date(value)` interprets it as UTC midnight, not local midnight — for an admin whose
 * calendar day runs on local time (matching how every timestamp is displayed elsewhere via
 * `toLocaleString`), that silently shifts the whole filtered range by the local UTC offset and,
 * combined with the backend's inclusive `OccurredAtUtc <= To` comparison, excludes most of the
 * day the admin actually picked (組長 PR #37 review, item 2).
 *
 * `startOfLocalDay`/`endOfLocalDayExclusiveBoundary` build the range against `[from, to)` in the
 * browser's own local calendar instead: `from` is local midnight of the start date, `to` is
 * local midnight of the *next* day after the end date minus 1ms (i.e. the last millisecond of
 * the end date, local time) — matching the backend's inclusive comparison exactly with no
 * off-by-one, whichever timezone the browser is actually running in.
 */

const ONE_DAY_MS = 24 * 60 * 60 * 1000

function parseDateInput(value: string): { year: number, month: number, day: number } {
  const [year, month, day] = value.split('-').map(Number)
  return { year, month, day }
}

export function startOfLocalDay(dateInput: string): Date {
  const { year, month, day } = parseDateInput(dateInput)
  return new Date(year, month - 1, day)
}

export function endOfLocalDayExclusiveBoundary(dateInput: string): Date {
  return new Date(startOfLocalDay(dateInput).getTime() + ONE_DAY_MS - 1)
}
