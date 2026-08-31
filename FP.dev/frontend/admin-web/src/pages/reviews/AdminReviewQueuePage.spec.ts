import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mocks = await vi.hoisted(async () => {
  const { ref } = await import('vue')
  return {
    data: ref([{
      publicId: '33333333-3333-3333-3333-333333333333',
      productPublicId: '22222222-2222-2222-2222-222222222222',
      productName: '人體工學椅',
      skuName: '黑色',
      rating: 4,
      title: '好坐',
      content: '坐起來很穩。',
      status: 'pendingReview',
      rejectionReason: null,
      createdAtUtc: '2026-08-20T00:00:00Z',
      reviewedAtUtc: null,
      rowVersion: 'AAAAAAAAB9E=',
      images: [],
    }]),
    isPending: ref(false),
    isError: ref(false),
    error: ref(null),
    mutationPending: ref(false),
    mutate: vi.fn(),
  }
})

vi.mock('../../features/reviews/queries', () => ({
  useAdminReviewsQuery: () => ({
    data: mocks.data,
    isPending: mocks.isPending,
    isError: mocks.isError,
    error: mocks.error,
    refetch: vi.fn(),
  }),
  useModerateReviewMutation: () => ({ isPending: mocks.mutationPending, mutate: mocks.mutate }),
}))

const { default: AdminReviewQueuePage } = await import('./AdminReviewQueuePage.vue')

describe('AdminReviewQueuePage', () => {
  beforeEach(() => mocks.mutate.mockReset())

  it('approves a pending review with row-version and an auditable reason code', async () => {
    const wrapper = mount(AdminReviewQueuePage)
    expect(wrapper.text()).toContain('人體工學椅')
    const approve = wrapper.findAll('button').find(button => button.text() === '核准公開')
    await approve!.trigger('click')

    expect(mocks.mutate).toHaveBeenCalledWith({
      id: '33333333-3333-3333-3333-333333333333',
      action: 'approve',
      body: {
        reasonCode: 'review_approve',
        note: null,
        rowVersion: 'AAAAAAAAB9E=',
      },
    }, expect.any(Object))
  })

  it('requires an explicit reason code before rejecting', async () => {
    const wrapper = mount(AdminReviewQueuePage)
    const reject = wrapper.findAll('button').find(button => button.text() === '退回修改')!
    expect(reject.attributes('disabled')).toBeDefined()

    await wrapper.find('input').setValue('irrelevant_content')
    expect(reject.attributes('disabled')).toBeUndefined()
    await reject.trigger('click')

    expect(mocks.mutate).toHaveBeenCalledWith(expect.objectContaining({
      action: 'reject',
      body: expect.objectContaining({ reasonCode: 'irrelevant_content' }),
    }), expect.any(Object))
  })
})
