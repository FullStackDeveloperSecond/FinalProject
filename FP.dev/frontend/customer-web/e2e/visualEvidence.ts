import { mkdir } from 'node:fs/promises'
import path from 'node:path'
import { expect, type Page } from '@playwright/test'

/** Opt-in screenshots from real journeys. Secrets never appear in review images. */
export async function captureVisualEvidence(page: Page, name: string) {
  const directory = process.env.VISUAL_REVIEW_DIR
  if (!directory) return
  await mkdir(directory, { recursive: true })
  const previous = page.viewportSize()
  for (const width of [360, 768, 1280]) {
    await page.setViewportSize({ width, height: 900 })
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await page.evaluate(() => document.fonts.ready)
    await expect(page.locator('.shared-state--loading')).toHaveCount(0)
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth), {
      message: `${name} must fit the ${width}px viewport`,
    }).toBeLessThanOrEqual(1)
    await page.screenshot({
      path: path.join(directory, `${name}-${width}.png`),
      fullPage: true,
      animations: 'disabled',
      mask: [page.locator('input[type="password"], .totp-secret, .totp-qr-code, .recovery-code-list')],
    })
  }
  if (previous) await page.setViewportSize(previous)
}
