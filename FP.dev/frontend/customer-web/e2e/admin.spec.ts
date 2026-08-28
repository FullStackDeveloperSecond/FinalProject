import { expect, test } from './fixtures.js'

test('an anonymous administrator is routed to the login page', async ({ page }) => {
  await page.goto('./')

  await expect(page).toHaveURL(/\/admin\/login\?redirect=\/$/)
  await expect(page.getByRole('heading', { level: 1, name: '管理員登入' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '電子郵件' })).toBeVisible()
  await expect(page.getByLabel('密碼')).toBeVisible()
  await expect(page.getByRole('button', { name: '登入' })).toBeVisible()
})
