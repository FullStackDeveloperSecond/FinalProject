export const favoriteAvailabilityLabels: Record<string, string> = {
  available: '現貨供應',
  outOfStock: '缺貨中',
  unlisted: '已下架',
}

export function canAddFavoriteToCart(availability: string): boolean {
  return availability === 'available'
}
