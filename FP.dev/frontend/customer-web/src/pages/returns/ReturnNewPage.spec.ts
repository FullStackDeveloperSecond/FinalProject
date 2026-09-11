import { ApiError } from '@doselect/web-shared/api'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ReturnNewPage from './ReturnNewPage.vue'

const { mutateAsync, routerPush, mutationState } = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  routerPush: vi.fn(),
  mutationState: {
    isPending: { value: false },
    isError: { value: false },
    error: { value: undefined as unknown },
  },
}))

vi.mock('../../features/returns/queries', () => ({
  useCreateReturnMutation: () => ({ ...mutationState, mutateAsync }),
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({
      params: { orderId: 'order-1' },
      query: {
        orderRowVersion: 'AAAAAAAAJXs=',
        items: JSON.stringify([{
          orderItemPublicId: 'item-1',
          skuName: '青軸鍵盤',
          maxQuantity: 1,
        }]),
      },
    }),
    useRouter: () => ({ push: routerPush }),
  }
})

describe('ReturnNewPage', () => {
  beforeEach(() => {
    mutateAsync.mockReset()
    routerPush.mockReset()
    mutationState.isPending.value = false
    mutationState.isError.value = false
    mutationState.error.value = undefined
  })

  it('limits the quantity to the remaining handoff quantity', () => {
    const wrapper = mount(ReturnNewPage)

    expect(wrapper.get('input[type="number"]').attributes('max')).toBe('1')
  })

  it('handles an already requested item without an unhandled rejection and explains the next step', async () => {
    const error = new ApiError('Conflict', {
      status: 409,
      code: 'return_quantity_exceeded',
    })
    mutateAsync.mockImplementationOnce(async () => {
      mutationState.isError.value = true
      mutationState.error.value = error
      throw error
    })
    const wrapper = mount(ReturnNewPage)
    await wrapper.get('textarea[required]').setValue('商品有瑕疵')

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    wrapper.vm.$forceUpdate()
    await flushPromises()

    expect(wrapper.text()).toContain('部分商品已經提出退貨申請')
    expect(wrapper.text()).toContain('返回訂單詳情')
    expect(routerPush).not.toHaveBeenCalled()
  })
})
