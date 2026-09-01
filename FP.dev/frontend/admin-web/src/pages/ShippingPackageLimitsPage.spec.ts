import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'

const mockList = vi.fn()
const mockCreate = vi.fn()
const mockPublish = vi.fn()

vi.mock('../features/shipping/api', () => ({
  listConvenienceStores: vi.fn(),
  createConvenienceStore: vi.fn(),
  updateConvenienceStore: vi.fn(),
  listPackageLimitVersions: mockList,
  createPackageLimitVersion: mockCreate,
  publishPackageLimitVersion: mockPublish,
}))

const { default: ShippingPackageLimitsPage } = await import('./ShippingPackageLimitsPage.vue')

function version(overrides: Record<string, unknown> = {}) {
  return {
    publicId: 'v1',
    providerCode: 'StorePickup',
    version: 1,
    status: 'Published',
    maxWeightKg: 5,
    maxLengthCm: 45,
    maxWidthCm: 45,
    maxHeightCm: 45,
    maxTotalCm: 105,
    maxDeclaredValue: 20000,
    effectiveFromUtc: null,
    effectiveToUtc: null,
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function mountPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return mount(ShippingPackageLimitsPage, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } })
}

describe('ShippingPackageLimitsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    mockList.mockReset()
    mockCreate.mockReset()
    mockPublish.mockReset()
  })

  it('lists the versions for the selected provider and shows the safe range', async () => {
    mockList.mockResolvedValue([version()])

    const wrapper = mountPage()
    await flushPromises()

    expect(mockList).toHaveBeenLastCalledWith('StorePickup')
    // 超商的安全範圍：單邊 1～45、三邊和 3～105、重量 0.1～5（購物車、訂單、付款與物流.md）。
    expect(wrapper.text()).toContain('單邊 1～45 cm')
    expect(wrapper.text()).toContain('重量 0.1～5 kg')
  })

  it('reloads with the home-delivery range when the provider changes', async () => {
    mockList.mockResolvedValue([version()])

    const wrapper = mountPage()
    await flushPromises()

    await wrapper.find('select[aria-label="物流服務"]').setValue('HomeDelivery')
    await flushPromises()

    expect(mockList).toHaveBeenLastCalledWith('HomeDelivery')
    expect(wrapper.text()).toContain('單邊 1～150 cm')
    expect(wrapper.text()).toContain('重量 0.1～20 kg')
  })

  /** 只有 Draft 能發布——Published／Superseded 不該出現發布按鈕（後端也只接受 Draft）。 */
  it('only offers 發布 for a Draft version', async () => {
    mockList.mockResolvedValue([
      version({ publicId: 'v1', version: 1, status: 'Published' }),
      version({ publicId: 'v2', version: 2, status: 'Draft' }),
    ])

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.findAll('button').filter((button) => button.text() === '發布')).toHaveLength(1)
  })

  it('confirms before publishing and sends the version RowVersion', async () => {
    mockList.mockResolvedValue([version({ publicId: 'v2', version: 2, status: 'Draft', rowVersion: 'BBB=' })])
    mockPublish.mockResolvedValue(version({ status: 'Published' }))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '發布')!.trigger('click')
    await flushPromises()

    expect(globalThis.confirm).toHaveBeenCalled()
    expect(mockPublish).toHaveBeenCalledWith('StorePickup', 'v2', { rowVersion: 'BBB=' })
  })

  /**
   * 自我審查發現：`PROVIDER_LABELS[providerCode]` 在 `<script setup>` 裡少了 `.value`，確認訊息會
   * 變成「undefined 的版本」——而 vue-tsc 不會抓到這個。釘住確認訊息的內容。
   */
  it('names the provider and the effective time in the publish confirmation', async () => {
    const scheduled = '2027-03-01T02:30:00.000Z'
    mockList.mockResolvedValue([version({ publicId: 'v2', version: 2, status: 'Draft', effectiveFromUtc: scheduled })])
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '發布')!.trigger('click')

    const message = confirmSpy.mock.calls.at(-1)![0] as string
    expect(message).toContain('超商取貨')
    expect(message).not.toContain('undefined')
    expect(message).toContain(new Date(scheduled).toLocaleString('zh-Hant-TW'))
  })

  it('does not publish when the confirmation is dismissed', async () => {
    mockList.mockResolvedValue([version({ publicId: 'v2', status: 'Draft' })])
    vi.spyOn(globalThis, 'confirm').mockReturnValue(false)

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '發布')!.trigger('click')
    await flushPromises()

    expect(mockPublish).not.toHaveBeenCalled()
  })

  /**
   * 安全範圍是程式固定的、後端一定會再驗；前台先擋是為了讓管理員在送出前就看到問題。超商單邊
   * 上限 45 cm，填 46 必須擋住而且不送出。
   */
  it('blocks a draft that breaks the provider safe range before sending it', async () => {
    mockList.mockResolvedValue([])

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '新增草稿')!.trigger('click')

    await wrapper.find('input[aria-label="最長邊"]').setValue(46)
    await wrapper.find('form[aria-label="新增包裹限制草稿"]').trigger('submit')
    await flushPromises()

    expect(mockCreate).not.toHaveBeenCalled()
    expect(wrapper.find('[aria-label="草稿問題"]').text()).toContain('1～45 cm')
  })

  /** 跨欄位規則：單邊不得大於三邊和（購物車、訂單、付款與物流.md）。 */
  it('blocks a draft whose largest side exceeds the three-side total', async () => {
    mockList.mockResolvedValue([])

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '新增草稿')!.trigger('click')

    await wrapper.find('input[aria-label="三邊和"]').setValue(40)
    await wrapper.find('form[aria-label="新增包裹限制草稿"]').trigger('submit')
    await flushPromises()

    expect(mockCreate).not.toHaveBeenCalled()
    expect(wrapper.find('[aria-label="草稿問題"]').text()).toContain('單邊不得大於三邊和')
  })

  /**
   * 後端只接受 UTC 瞬間（沒有 Z 的值回 validation_failed，組長 PR #73 round-3 item 5）。
   * `datetime-local` 給的是本地時間字串，必須真的換算成 UTC，不能在字串後面接一個 Z。
   */
  it('sends the scheduled time as a real UTC instant', async () => {
    mockList.mockResolvedValue([])
    mockCreate.mockResolvedValue(version({ status: 'Draft' }))

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '新增草稿')!.trigger('click')

    await wrapper.find('input[aria-label="生效時間"]').setValue('2027-03-01T10:30')
    await wrapper.find('form[aria-label="新增包裹限制草稿"]').trigger('submit')
    await flushPromises()

    const sent = mockCreate.mock.calls.at(-1)![1] as { effectiveFromUtc: string | null }
    expect(sent.effectiveFromUtc).toBe(new Date('2027-03-01T10:30').toISOString())
    expect(sent.effectiveFromUtc!.endsWith('Z')).toBe(true)
  })

  it('leaves the effective times null when they are not filled in', async () => {
    mockList.mockResolvedValue([])
    mockCreate.mockResolvedValue(version({ status: 'Draft' }))

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '新增草稿')!.trigger('click')
    await wrapper.find('form[aria-label="新增包裹限制草稿"]').trigger('submit')
    await flushPromises()

    const sent = mockCreate.mock.calls.at(-1)![1] as { effectiveFromUtc: string | null, effectiveToUtc: string | null }
    expect(sent.effectiveFromUtc).toBeNull()
    expect(sent.effectiveToUtc).toBeNull()
  })

  /**
   * 組長 PR #73 裁定 B1：可用性看時間窗，不看 Published 這個狀態字——被接班的 Superseded 版本在
   * cutoff 之前仍然是唯一有效的版本，畫面必須據實標示。
   */
  it('marks a superseded version as effective while its window still contains now', async () => {
    const future = new Date(Date.now() + 86_400_000).toISOString()
    const past = new Date(Date.now() - 86_400_000).toISOString()
    mockList.mockResolvedValue([
      version({ publicId: 'v1', version: 1, status: 'Superseded', effectiveFromUtc: past, effectiveToUtc: future }),
      version({ publicId: 'v2', version: 2, status: 'Published', effectiveFromUtc: future, effectiveToUtc: null }),
    ])

    const wrapper = mountPage()
    await flushPromises()

    const rows = wrapper.findAll('tbody > tr')
    expect(rows[0].text()).toContain('目前生效')
    expect(rows[1].text()).not.toContain('目前生效')
  })

  it('surfaces the API error message when publishing fails', async () => {
    mockList.mockResolvedValue([version({ publicId: 'v2', status: 'Draft' })])
    mockPublish.mockRejectedValue(new ApiError('overlap', {
      status: 409,
      code: 'package_limit_period_overlap',
    }))
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true)

    const wrapper = mountPage()
    await flushPromises()
    await wrapper.findAll('button').find((button) => button.text() === '發布')!.trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').text()).toContain('生效期間重疊')
  })
})
