import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')
  const mutation = () => ({ isPending: ref(false), mutate: vi.fn() })
  return {
    eligible: ref([{
      orderItemPublicId: '11111111-1111-1111-1111-111111111111',
      productPublicId: '22222222-2222-2222-2222-222222222222',
      skuCode: 'SKU-01',
      productName: '人體工學椅',
      skuName: '黑色',
      completedAtUtc: '2026-08-20T00:00:00Z',
      reviewPublicId: null,
      reviewStatus: null,
    }]),
    mine: ref<Array<Record<string, unknown>>>([]),
    eligiblePending: ref(false),
    minePending: ref(false),
    eligibleError: ref(null),
    mineError: ref(null),
    create: mutation(),
    update: mutation(),
    submit: mutation(),
    withdraw: mutation(),
    upload: mutation(),
    deleteImage: mutation(),
  }
})

vi.mock('../../features/reviews/queries', () => ({
  useEligibleReviewItemsQuery: () => ({
    data: mocks.eligible,
    isPending: mocks.eligiblePending,
    error: mocks.eligibleError,
    refetch: vi.fn(),
  }),
  useMyReviewsQuery: () => ({
    data: mocks.mine,
    isPending: mocks.minePending,
    error: mocks.mineError,
    refetch: vi.fn(),
  }),
  useCreateReviewMutation: () => mocks.create,
  useUpdateReviewMutation: () => mocks.update,
  useSubmitReviewMutation: () => mocks.submit,
  useWithdrawReviewMutation: () => mocks.withdraw,
  useUploadReviewImageMutation: () => mocks.upload,
  useDeleteReviewImageMutation: () => mocks.deleteImage,
}))

const { default: MyReviewsPage } = await import('./MyReviewsPage.vue')

describe('MyReviewsPage', () => {
  beforeEach(() => {
    mocks.create.mutate.mockReset()
    mocks.submit.mutate.mockReset()
    mocks.mine.value = []
  })

  it('creates a submitted review from an eligible completed-order item', async () => {
    const wrapper = mount(MyReviewsPage)
    await wrapper.find('select').setValue('11111111-1111-1111-1111-111111111111')
    await wrapper.find('textarea').setValue('坐起來很穩，組裝也很簡單。')
    await wrapper.find('form').trigger('submit')

    expect(mocks.create.mutate).toHaveBeenCalledWith(expect.objectContaining({
      orderItemPublicId: '11111111-1111-1111-1111-111111111111',
      rating: 5,
      content: '坐起來很穩，組裝也很簡單。',
      submit: true,
    }), expect.any(Object))
  })

  it('shows a rejected review reason and lets the member resubmit it', async () => {
    mocks.mine.value = [{
      publicId: '33333333-3333-3333-3333-333333333333',
      orderItemPublicId: '11111111-1111-1111-1111-111111111111',
      productPublicId: '22222222-2222-2222-2222-222222222222',
      productName: '人體工學椅',
      skuName: '黑色',
      rating: 4,
      title: '好坐',
      content: '內容需要補充。',
      status: 'rejected',
      rejectionReason: '請移除與商品無關的資訊',
      createdAtUtc: '2026-08-20T00:00:00Z',
      updatedAtUtc: '2026-08-21T00:00:00Z',
      rowVersion: 'AAAAAAAAB9E=',
      images: [],
    }]
    const wrapper = mount(MyReviewsPage)

    expect(wrapper.text()).toContain('請移除與商品無關的資訊')
    const submitButton = wrapper.find('.review-card').findAll('button')
      .find(button => button.text() === '送出審核')
    await submitButton!.trigger('click')

    expect(mocks.submit.mutate).toHaveBeenCalledWith({
      id: '33333333-3333-3333-3333-333333333333',
      rowVersion: 'AAAAAAAAB9E=',
    }, expect.any(Object))
  })
})
