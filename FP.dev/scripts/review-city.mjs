import { createRequire } from 'node:module'
import path from 'node:path'
import { mkdir } from 'node:fs/promises'
const require = createRequire(path.resolve('frontend/customer-web/package.json'))
const { chromium, expect } = require('@playwright/test')
const directory = path.resolve('frontend/review/city-0906')
await mkdir(directory, { recursive: true })
const browser = await chromium.launch()
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' })
const errors = []
page.on('pageerror', error => errors.push(error.message))
try {
  await page.goto('http://127.0.0.1:5173/')
  await expect(page.getByRole('heading', { name: '歡迎來到懂選電腦城' })).toBeVisible()
  await page.screenshot({ path: path.join(directory, 'home-desktop.png'), fullPage: true })
  await page.getByRole('button', { name: '收起 Donngu 導覽' }).click()
  await expect(page.locator('#donngu-dialog')).toHaveCount(0)
  await page.reload()
  await expect(page.getByRole('button', { name: '開啟 Donngu 導覽' })).toBeVisible()
  await page.getByRole('button', { name: '開啟 Donngu 導覽' }).click()
  await page.getByRole('link', { name: '商品', exact: true }).click()
  await expect(page.getByRole('heading', { name: '零件探索區' })).toBeVisible()
  await expect(page.locator('.product-card').first()).toBeVisible()
  await page.screenshot({ path: path.join(directory, 'products-desktop.png'), fullPage: true })
  await page.getByRole('button', { name: '關閉 Donngu 介紹' }).focus()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('button', { name: '開啟 Donngu 導覽' })).toBeFocused()
  for (const width of [360, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    for (const [url, name] of [['/', 'home'], ['/products', 'products'], ['/ai-search', 'ai'], ['/login', 'login'], ['/support', 'support']]) {
      await page.goto('http://127.0.0.1:5173' + url)
      await expect(page.locator('main h1').first()).toBeVisible()
      await expect(page.locator('.shared-state--loading')).toHaveCount(0)
      if (name === 'products') await expect(page.locator('.product-card').first()).toBeVisible()
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1)
      if (width === 360 && name === 'home') {
        await page.getByRole('button', { name: '開啟 Donngu 導覽' }).click()
        await page.screenshot({ path: path.join(directory, 'home-mobile-guide.png'), fullPage: true })
        await page.getByRole('button', { name: '收起 Donngu 導覽' }).click()
      }
      await page.screenshot({ path: path.join(directory, `${name}-${width}.png`), fullPage: true })
    }
  }
  if (errors.length) throw new Error(errors.join('\n'))
  console.log('City guide toggle, persistence, route copy, Escape focus and 15 responsive page checks passed.')
} finally { await browser.close() }
