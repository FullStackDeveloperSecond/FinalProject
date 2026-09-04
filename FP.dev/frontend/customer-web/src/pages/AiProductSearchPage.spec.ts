import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mutateAsync = vi.fn()
const mutationData = ref<Record<string, unknown> | undefined>()
const mutationError = ref<unknown>()

vi.mock('../features/aiProductSearch/queries', () => ({
  useAiProductSearchMutation: () => ({
    data: mutationData,
    error: mutationError,
    isPending: ref(false),
    isError: ref(false),
    mutateAsync,
    reset: vi.fn(() => { mutationData.value = undefined }),
  }),
}))

const { default: AiProductSearchPage } = await import('./AiProductSearchPage.vue')

function baseResult(overrides: Record<string, unknown> = {}) {
  return {
    searchPublicId: '10000000-0000-0000-0000-000000000001',
    resultType: 'recommendations',
    degradationMode: 'none',
    intent: {
      intent: 'PrebuiltComputer',
      purposes: ['VideoEditing'],
      minimumBudget: null,
      maximumBudget: 50000,
      keyword: '剪輯',
      categoryCode: 'PREBUILT_COMPUTER',
      preferredBrandCodes: [],
      excludedBrandCodes: [],
      requiredSpecs: [],
      preferences: ['安靜'],
      proposedExistingParts: [],
    },
    clarifications: [],
    recommendations: [],
    customBuild: null,
    fallbackProducts: [],
    disclaimerKey: 'ai.productSearch.recommendationDisclaimer',
    usage: { remainingRequests: 29, resetAtUtc: '2026-08-30T16:00:00Z' },
    ...overrides,
  }
}

function product() {
  return {
    productPublicId: '20000000-0000-0000-0000-000000000002',
    defaultSkuPublicId: '30000000-0000-0000-0000-000000000003',
    productCode: 'PC-CREATOR',
    skuCode: 'PC-CREATOR-01',
    name: '創作者工作站',
    brand: { code: 'DOSELECT', name: '懂選' },
    category: { code: 'PREBUILT_COMPUTER', name: '套裝電腦' },
    price: { list: 49000, sale: null, currency: 'TWD' },
    availability: 'inStock',
    primaryImage: null,
    badges: [],
  }
}

function mountPage() {
  return mount(AiProductSearchPage, {
    global: {
      stubs: {
        RouterLink: { template: '<a><slot /></a>' },
      },
    },
  })
}

beforeEach(() => {
  mutateAsync.mockReset()
  mutationData.value = undefined
  mutationError.value = undefined
})

describe('AiProductSearchPage', () => {
  it('submits a natural-language request and shows a grounded recommendation reason', async () => {
    const response = baseResult({
      recommendations: [{
        product: product(),
        reason: '在五萬元預算內，適合剪輯用途。',
        compatibilityStatus: 'NotRequired',
        compatibilityMessageKeys: [],
      }],
    })
    mutateAsync.mockImplementation(async () => {
      mutationData.value = response
      return response
    })
    const wrapper = mountPage()

    await wrapper.find('textarea').setValue('五萬元剪輯 4K 影片')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({
      message: '五萬元剪輯 4K 影片',
      locale: 'zh-TW',
      existingParts: [],
    }))
    expect(wrapper.text()).toContain('在五萬元預算內，適合剪輯用途。')
    expect(wrapper.text()).toContain('今日剩餘 29 次')
  })

  it('keeps the original need when submitting an answer to a clarification', async () => {
    const clarification = baseResult({
      resultType: 'clarification',
      intent: null,
      clarifications: ['你的最高預算是多少？'],
    })
    mutateAsync.mockImplementationOnce(async () => {
      mutationData.value = clarification
      return clarification
    }).mockImplementationOnce(async () => {
      const response = baseResult()
      mutationData.value = response
      return response
    })
    const wrapper = mountPage()

    await wrapper.find('textarea').setValue('想組一台剪輯電腦')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.text()).toContain('你的最高預算是多少？')

    await wrapper.find('textarea').setValue('最高五萬元')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mutateAsync).toHaveBeenLastCalledWith(expect.objectContaining({
      message: '想組一台剪輯電腦\n使用者補充：最高五萬元',
    }))
  })

  it('shows a complete compatible build and excludes an existing part from the purchase total', async () => {
    const cpu = {
      ...product(),
      name: '懂選處理器',
      category: { code: 'CPU', name: '處理器' },
      price: { list: 10000, sale: null, currency: 'TWD' },
    }
    const response = baseResult({
      intent: {
        ...baseResult().intent,
        intent: 'CustomBuild',
        categoryCode: null,
      },
      customBuild: {
        components: [
          {
            product: cpu,
            skuPublicId: cpu.defaultSkuPublicId,
            sourceType: 'catalogSku',
            categoryCode: 'CPU',
            displayName: cpu.name,
            quantity: 1,
            isExistingPart: false,
            reason: '符合用途與新購預算。',
          },
          {
            product: null,
            skuPublicId: null,
            sourceType: 'structuredManual',
            categoryCode: 'GPU',
            displayName: '既有顯示卡',
            quantity: 1,
            isExistingPart: true,
            reason: null,
          },
        ],
        purchaseSubtotal: 10000,
        assemblyFee: 300,
        purchaseTotal: 10300,
        currency: 'TWD',
        compatibilityStatus: 'Compatible',
        compatibilityMessageKeys: [],
      },
    })
    mutateAsync.mockImplementation(async () => {
      mutationData.value = response
      return response
    })
    const wrapper = mountPage()

    await wrapper.find('textarea').setValue('一萬五組一台遊戲電腦')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('完整組裝清單')
    expect(wrapper.text()).toContain('NT$10,300')
    expect(wrapper.text()).toContain('既有零件・不計入新購預算')
    expect(wrapper.text()).toContain('相容性：通過')
    expect(wrapper.text()).toContain('符合用途與新購預算。')
  })

  it('labels keyword fallback as degraded rather than an AI recommendation', async () => {
    const response = baseResult({
      resultType: 'degraded',
      degradationMode: 'keywordSearch',
      intent: null,
      fallbackProducts: [product()],
    })
    mutateAsync.mockImplementation(async () => {
      mutationData.value = response
      return response
    })
    const wrapper = mountPage()

    await wrapper.find('textarea').setValue('剪輯電腦')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('已改用一般搜尋')
    expect(wrapper.text()).toContain('不代表 AI 推薦或相容性保證')
  })

  it('shows bounded ways to relax constraints when no catalog result exists', async () => {
    const response = baseResult({
      resultType: 'noResults',
      recommendations: [],
      fallbackProducts: [],
    })
    mutateAsync.mockImplementation(async () => {
      mutationData.value = response
      return response
    })
    const wrapper = mountPage()

    await wrapper.find('textarea').setValue('要求資料庫不存在的規格')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('目前沒有完全符合的商品')
    expect(wrapper.text()).toContain('提高預算、放寬品牌或規格偏好')
  })

  it('submits a user-confirmed structured manual part', async () => {
    const response = baseResult()
    mutateAsync.mockImplementation(async () => {
      mutationData.value = response
      return response
    })
    const wrapper = mountPage()

    await wrapper.find('textarea').setValue('找相容的主機板')
    const addManual = wrapper.findAll('button').find(button => button.text() === '加入手填零件')
    await addManual!.trigger('click')
    await wrapper.get('[data-testid="manual-category"]').setValue('CPU')
    await wrapper.get('[data-testid="manual-display-name"]').setValue('既有處理器')
    await wrapper.get('[data-testid="manual-semantic-key"]').setValue('cpu_socket')
    await wrapper.get('[data-testid="manual-spec-value"]').setValue('AM5')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({
      existingParts: [expect.objectContaining({
        sourceType: 'structuredManual',
        categoryCode: 'CPU',
        displayName: '既有處理器',
        confirmedByUser: true,
        specifications: [expect.objectContaining({
          semanticKey: 'cpu_socket',
          value: 'AM5',
        })],
      })],
    }))
  })

  it('keeps a natural-language existing part out until the user confirms it', async () => {
    const clarification = baseResult({
      resultType: 'clarification',
      intent: {
        ...baseResult().intent,
        proposedExistingParts: [{
          categoryCode: 'CPU',
          displayName: 'AM5 CPU',
          quantity: 1,
          specifications: [{ semanticKey: 'cpu_socket', operator: 'eq', value: 'AM5', unit: null }],
        }],
      },
      clarifications: ['請確認 AI 解析出的既有零件與規格。'],
    })
    mutateAsync.mockImplementationOnce(async () => {
      mutationData.value = clarification
      return clarification
    }).mockImplementationOnce(async () => {
      const response = baseResult()
      mutationData.value = response
      return response
    })
    const wrapper = mountPage()

    await wrapper.find('textarea').setValue('已有 AM5 CPU，想找主機板')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('尚未確認')
    expect(mutateAsync).toHaveBeenLastCalledWith(expect.objectContaining({ existingParts: [] }))
    const confirm = wrapper.findAll('button').find(button => button.text() === '確認並加入')
    await confirm!.trigger('click')
    await wrapper.find('textarea').setValue('以上資料正確')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mutateAsync).toHaveBeenLastCalledWith(expect.objectContaining({
      existingParts: [expect.objectContaining({
        sourceType: 'structuredManual',
        categoryCode: 'CPU',
        displayName: 'AM5 CPU',
        confirmedByUser: true,
      })],
    }))
  })
})
