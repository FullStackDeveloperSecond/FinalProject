import { expect, test } from '@playwright/test'

test('the admin application exposes its desktop entry point', async ({ page }) => {
  await page.goto('./')

  await expect(page.getByRole('heading', {
    level: 1,
    name: '管理後台基礎環境已就緒',
  })).toBeVisible()
  await expect(page.getByRole('navigation', { name: '管理功能導覽' })).toBeVisible()
  await expect(page.getByRole('link', { name: '前往客服 SLA 佇列' })).toBeVisible()
})
