import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AiUsagePage from './AiUsagePage.vue'

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')
  return {
    data: ref<Record<string, unknown> | null>(null),
    isPending: ref(false),
    isError: ref(false),
    error: ref<unknown>(null),
    refetch: vi.fn(),
  }
})

vi.mock('../features/aiUsage/queries', () => ({
  useAdminAiUsageQuery: () => mocks,
}))

describe('AiUsagePage', () => {
  beforeEach(() => {
    mocks.data.value = {
      fromUtc: '2026-08-01T00:00:00Z',
      toUtc: '2026-08-29T00:00:00Z',
      dataAsOfUtc: '2026-08-28T12:00:00Z',
      cumulativeCostUsd: null,
      budgetWarningActive: true,
      budgetProtectionActive: false,
      rows: [{
        feature: 'support',
        model: 'integration-model',
        status: 'answered',
        interactionCount: 2,
        inputTokens: 100,
        outputTokens: 20,
        estimatedCostUsd: null,
      }],
    }
    mocks.isPending.value = false
    mocks.isError.value = false
    mocks.error.value = null
  })

  it('shows threshold status while keeping cost masked for aggregate-only roles', () => {
    const wrapper = mount(AiUsagePage)

    expect(wrapper.text()).toContain('US$70 警告門檻')
    expect(wrapper.text()).toContain('integration-model')
    expect(wrapper.text()).toContain('無權限')
    expect(wrapper.text()).not.toContain('71.25')
  })
})
