export const TAIPEI_TIME_ZONE = 'Asia/Taipei'

type DateInput = string | number | Date | null | undefined

const ZONELESS_ISO_DATE_TIME = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?$/

function parseDate(value: DateInput): Date | null {
  if (value === null || value === undefined || value === '') return null

  const normalizedValue = typeof value === 'string' && ZONELESS_ISO_DATE_TIME.test(value)
    ? `${value}Z`
    : value
  const date = normalizedValue instanceof Date ? normalizedValue : new Date(normalizedValue)
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
