import { createRequire } from 'node:module'
import path from 'node:path'
import { mkdir } from 'node:fs/promises'
const require = createRequire(path.resolve('frontend/customer-web/package.json'))
const { chromium, expect } = require('@playwright/test')
const directory = path.resolve('frontend/review/home-entry-ads')
await mkdir(directory, { recursive: true })
const browser = await chromium.launch()
try {
 const page = await browser.newPage({ viewport: { width: 1440, height: 1150 }, reducedMotion: 'reduce' })
 await page.goto('http://127.0.0.1:5173/')
 await expect(page.locator('.home-hero .home-step__link')).toHaveCount(3)
 await page.getByRole('button', { name: '收起 Donngu 導覽' }).click()
 await page.screenshot({ path: path.join(directory, 'desktop.png'), fullPage: true })
 await page.getByRole('button', { name: '下一則廣告' }).click()
 await expect(page.locator('.home-promotions h2')).toHaveText('讓靈感，跟得上你的速度')
 await page.getByRole('button', { name: '上一則廣告' }).click()
 await expect(page.locator('.home-promotions h2')).toHaveText('開啟你的遊戲新世界')
 for (const width of [360, 768]) {
  await page.setViewportSize({width, height: 900})
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1)
  await page.evaluate(() => window.scrollTo(0, 0))
  await page.screenshot({path: path.join(directory, `home-${width}.png`), fullPage: true})
 }
 console.log('Homepage: 3 hero links, carousel controls, reduced motion and responsive overflow checks passed.')
} finally { await browser.close() }
