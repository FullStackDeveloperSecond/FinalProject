import { expect, test } from './fixtures.js'

test('a shopper can open the seeded catalog and view product details', async ({ page, api, seed }) => {
  const productResponse = await api.get(`/api/v1/products/${seed.productPublicId}`)
  expect(productResponse.ok(), 'The deterministic catalog seed must exist').toBe(true)

  await page.goto('/')

  await expect(page.getByRole('heading', { level: 1, name: 'DoSelect 懂選' })).toBeVisible()
  await page.getByRole('link', { name: /瀏覽全部商品/ }).click()

  await expect(page).toHaveURL(/\/products$/)
  await expect(page.getByRole('heading', { level: 1, name: '商品搜尋' })).toBeVisible()

  const seededProduct = page.getByRole('heading', {
    level: 3,
    name: '懂選開發用顯示卡',
    exact: true,
  })
  await expect(seededProduct).toBeVisible()
  await seededProduct.click()

  await expect(page).toHaveURL(new RegExp(`/products/${seed.productPublicId}$`))
  await expect(page.getByRole('heading', { level: 1, name: '懂選開發用顯示卡' })).toBeVisible()
  await expect(page.getByText('NT$19,900')).toBeVisible()
  await expect(page.getByText('現貨供應')).toBeVisible()
})

test('a member can consent to AI support and fall back to a human case when AI is disabled', async ({
  page,
  loginAsMember,
}) => {
  await loginAsMember()
  await page.goto('/support')

  await expect(page.getByRole('heading', { level: 1, name: 'AI 客服' })).toBeVisible()
  const consentCheckbox = page.getByRole('checkbox', {
    name: '我已閱讀並同意上述外部 AI 處理方式',
  })
  const remainingUsage = page.getByText(/今日剩餘：/)
  await expect(consentCheckbox.or(remainingUsage)).toBeVisible()
  if (await consentCheckbox.isVisible()) {
    await consentCheckbox.check()
    await page.getByRole('button', { name: '同意並開始使用' }).click()
  }

  await expect(remainingUsage).toBeVisible()
  await page.getByRole('textbox', { name: '你的問題' }).fill('請說明退貨流程')
  await page.getByRole('button', { name: '送出問題' }).click()

  await expect(page.getByRole('link', { name: '建立人工客服案件' })).toBeVisible()
  await page.getByRole('button', { name: '撤回 AI 同意' }).click()
  await expect(consentCheckbox).toBeVisible()
})

test('a public shopper can use AI search safely when the provider is disabled', async ({ page }) => {
  await page.goto('/ai-search')

  await expect(page.getByRole('heading', { level: 1, name: '說出需求，不必先學會所有規格' }))
    .toBeVisible()
  await page.getByRole('textbox', { name: '你想找什麼？' }).fill('懂選開發用顯示卡')
  await page.getByRole('button', { name: '開始懂選' }).click()

  await expect(page.getByRole('heading', { name: 'AI 暫時無法使用，已改用一般搜尋' }))
    .toBeVisible()
  await expect(page.getByText('不代表 AI 推薦或相容性保證')).toBeVisible()
  await expect(page.getByRole('heading', { level: 3, name: '懂選開發用顯示卡', exact: true }))
    .toBeVisible()
})
