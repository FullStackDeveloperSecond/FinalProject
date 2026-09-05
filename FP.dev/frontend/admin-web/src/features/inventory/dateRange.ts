/**
 * `<input type="date">` gives a bare "YYYY-MM-DD" string with no timezone. Converting it with
 * `new Date(value)` interprets it as UTC midnight, not local midnight — for an admin whose
 * calendar day runs on local time (matching how every timestamp is displayed elsewhere via
 * `toLocaleString`), that silently shifts the whole filtered range by the local UTC offset and,
 * combined with the backend's inclusive `OccurredAtUtc <= To` comparison, excludes most of the
 * day the admin actually picked (組長 PR #37 review, item 2).
 *
 * `startOfLocalDay`/`endOfLocalDayExclusiveBoundary` build the range against `[from, to)` in the
 * browser's own local calendar instead: `from` is local midnight of the start date, `to` is the
 * last millisecond of the end date (local time) — matching the backend's inclusive comparison
 * exactly, whichever timezone the browser is actually running in.
 *
 * 組長 PR #37 round-2 review, item 4: the end boundary must come from the *next local midnight*,
 * never from "local midnight + 24 h" — a DST-switching local day is 23 or 25 hours long, so the
 * fixed-hours version leaked one hour of the next day on spring-forward days and dropped the last
 * hour of the picked day on fall-back days (verified against America/New_York 2026-03-08 and
 * 2026-11-01). `new Date(year, month - 1, day + 1)` lets the platform's own calendar arithmetic
 * resolve the next midnight, DST included.
 */

function parseDateInput(value: string): { year: number, month: number, day: number } {
  const [year, month, day] = value.split('-').map(Number)
  return { year, month, day }
}

export function startOfLocalDay(dateInput: string): Date {
  const { year, month, day } = parseDateInput(dateInput)
  return new Date(year, month - 1, day)
}

export function endOfLocalDayExclusiveBoundary(dateInput: string): Date {
  const { year, month, day } = parseDateInput(dateInput)
  // Day overflow is normalized by the Date constructor (Dec 31 + 1 → Jan 1), and the resulting
  // instant is the true next local midnight regardless of how many hours the day contained.
  return new Date(new Date(year, month - 1, day + 1).getTime() - 1)
}
