export const favoriteAvailabilityLabels: Record<string, string> = {
  inStock: '現貨供應',
  lowStock: '庫存有限',
  outOfStock: '缺貨中',
  delisted: '已下架',
}

export function formatFavoritedDate(value: string | null | undefined): string {
  return value ? new Intl.DateTimeFormat('zh-Hant-TW', { dateStyle: 'medium' }).format(new Date(value)) : '—'
}
