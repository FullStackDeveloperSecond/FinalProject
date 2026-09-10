import { describe, expect, it } from 'vitest'
import { districtsForCity, TAIWAN_ADMINISTRATIVE_AREAS, TAIWAN_CITIES } from './taiwanAdministrativeAreas'

describe('Taiwan administrative areas', () => {
  it('provides all 22 cities/counties and 368 township/district choices', () => {
    expect(TAIWAN_CITIES).toHaveLength(22)
    expect(Object.values(TAIWAN_ADMINISTRATIVE_AREAS).flat()).toHaveLength(368)
  })

  it('never offers New Taipei districts for Taipei City', () => {
    expect(districtsForCity('臺北市')).toContain('中正區')
    expect(districtsForCity('臺北市')).not.toContain('新店區')
    expect(districtsForCity('新北市')).toContain('新店區')
    expect(districtsForCity('')).toEqual([])
  })
})
