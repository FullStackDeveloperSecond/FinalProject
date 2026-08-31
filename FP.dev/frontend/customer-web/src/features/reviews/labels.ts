export const reviewStatusLabels: Record<string, string> = {
  draft: '草稿',
  pendingReview: '待審核',
  approved: '已公開',
  rejected: '已退回',
  hidden: '已隱藏',
  withdrawn: '已撤回',
}
export function formatReviewDate(value: string | null | undefined): string {
  return value ? new Intl.DateTimeFormat('zh-Hant-TW', { dateStyle: 'medium' }).format(new Date(value)) : '—'
}
