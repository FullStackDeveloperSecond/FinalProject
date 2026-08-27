import { expect, test } from '@playwright/test'

test('a shopper can open the seeded catalog and view product details', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'DoSelect 懂選' })).toBeVisible()
  await page.getByRole('link', { name: /瀏覽全部商品/ }).click()

  await expect(page).toHaveURL(/\/products$/)
  await expect(page.getByRole('heading', { level: 1, name: '商品搜尋' })).toBeVisible()

  const seededProduct = page.getByRole('heading', {
    level: 3,
    name: '懂選開發用顯示卡',
  })
  await expect(seededProduct).toBeVisible()
  await seededProduct.click()

  await expect(page).toHaveURL(/\/products\/5940b1db-3c83-4db0-b285-9777616d11b1$/)
  await expect(page.getByRole('heading', { level: 1, name: '懂選開發用顯示卡' })).toBeVisible()
  await expect(page.getByText('NT$19,900')).toBeVisible()
  await expect(page.getByText('現貨供應')).toBeVisible()
})
