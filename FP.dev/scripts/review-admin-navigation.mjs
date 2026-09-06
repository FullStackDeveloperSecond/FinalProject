import { createRequire } from 'node:module'
import path from 'node:path'
import { mkdir } from 'node:fs/promises'
const require = createRequire(path.resolve('frontend/customer-web/package.json'))
const { chromium, expect } = require('@playwright/test')
const output = path.resolve('frontend/review/admin-navigation')
await mkdir(output, { recursive: true })
const browser = await chromium.launch()
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' })
  let roles = ['SuperAdmin']
  await page.route('**/api/v1/admin/auth/session', route => route.fulfill({ json: { isAuthenticated: true, requiresTwoFactor: false, expiresAtUtc: null, user: { publicId: 'visual-review', displayName: '視覺預覽管理員', emailMasked: 'preview@example.test', emailVerified: true, locale: 'zh-TW', roles } } }))
  await page.goto('http://127.0.0.1:5176/admin/')
  await expect(page.getByRole('heading', { name: '管理工作台', exact: true })).toBeVisible()
  const catalog = page.getByRole('button', { name: '商品管理', exact: true })
  await expect(catalog).toHaveAttribute('aria-expanded', 'false')
  await catalog.focus()
  await page.keyboard.press('Enter')
  await expect(catalog).toHaveAttribute('aria-expanded', 'true')
  await page.screenshot({ path: path.join(output, 'desktop-expanded.png'), fullPage: true })
  await page.screenshot({ path: path.join(output, 'desktop-viewport.png') })
  await page.keyboard.press('Space')
  await expect(catalog).toHaveAttribute('aria-expanded', 'false')
  await page.screenshot({ path: path.join(output, 'desktop-collapsed.png'), fullPage: true })
  for (const width of [360, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 })
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1)
    await page.screenshot({ path: path.join(output, `dashboard-${width}.png`), fullPage: true })
    if (width === 360) {
      await page.getByRole('button', { name: '管理選單', exact: true }).click()
      await catalog.click()
      await expect(page.locator('#admin-group-catalog')).toBeVisible()
      await page.screenshot({ path: path.join(output, 'mobile-navigation.png') })
      await page.getByRole('button', { name: '管理選單', exact: true }).click()
    }
  }
  roles = ['CustomerService']
  await page.reload()
  await expect(page.getByRole('heading', { name: '客服與售後', exact: true })).toBeVisible()
  await expect(page.locator('.admin-nav-group')).toHaveCount(1)
  await expect(page.getByRole('heading', { name: '財務與報表', exact: true })).toHaveCount(0)
  await page.screenshot({ path: path.join(output, 'customer-service-role.png'), fullPage: true })
  console.log('Passed: keyboard collapse/expand, 3 responsive widths, mobile navigation, role filtering. Screenshots use an isolated mocked session; no account or API authorization was modified.')
} finally { await browser.close() }
