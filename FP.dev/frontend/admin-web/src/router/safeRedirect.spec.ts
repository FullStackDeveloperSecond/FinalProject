import { describe, expect, it } from 'vitest'
import { resolveSafeRedirect } from './safeRedirect'

describe('resolveSafeRedirect', () => {
  it('accepts a plain internal path', () => {
    expect(resolveSafeRedirect('/products/123')).toBe('/products/123')
  })

  it('accepts an internal path with a query string and hash', () => {
    expect(resolveSafeRedirect('/products?page=2#top')).toBe('/products?page=2#top')
  })

  it('falls back to / for undefined or missing values', () => {
    expect(resolveSafeRedirect(undefined)).toBe('/')
    expect(resolveSafeRedirect(null)).toBe('/')
    expect(resolveSafeRedirect('')).toBe('/')
  })

  it('falls back for non-string values (e.g. a duplicated query param becomes an array)', () => {
    expect(resolveSafeRedirect(['/a', '/b'])).toBe('/')
  })

  it('rejects an absolute external URL', () => {
    expect(resolveSafeRedirect('https://evil.example/phish')).toBe('/')
  })

  it('rejects a protocol-relative URL', () => {
    expect(resolveSafeRedirect('//evil.example/phish')).toBe('/')
  })

  it('rejects a backslash variant some browsers still treat as protocol-relative', () => {
    expect(resolveSafeRedirect('/\\evil.example')).toBe('/')
  })

  it('rejects a javascript: URL', () => {
    expect(resolveSafeRedirect('javascript:alert(1)')).toBe('/')
  })

  it('rejects a path that does not start with a slash', () => {
    expect(resolveSafeRedirect('login')).toBe('/')
    expect(resolveSafeRedirect('evil.example')).toBe('/')
  })

  it('uses the caller-supplied fallback when given', () => {
    expect(resolveSafeRedirect('https://evil.example', '/home')).toBe('/home')
  })
})
