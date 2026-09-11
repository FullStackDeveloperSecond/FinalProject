export const TAIPEI_TIME_ZONE = 'Asia/Taipei'

type DateInput = string | number | Date | null | undefined

function parseDate(value: DateInput): Date | null {
  if (value === null || value === undefined || value === '') return null

  const date = value instanceof Date ? value : new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

export function formatTaipeiDateTime(value: DateInput, emptyValue = '—'): string {
  const date = parseDate(value)
  if (!date) return emptyValue

  return new Intl.DateTimeFormat('zh-TW', {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: TAIPEI_TIME_ZONE,
  }).format(date)
}

export function formatTaipeiDate(value: DateInput, emptyValue = '—'): string {
  const date = parseDate(value)
  if (!date) return emptyValue

  return new Intl.DateTimeFormat('zh-TW', {
    dateStyle: 'medium',
    timeZone: TAIPEI_TIME_ZONE,
  }).format(date)
}
