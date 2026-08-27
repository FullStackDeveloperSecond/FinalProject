import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { useAddSupportMessageMutation, useCancelSupportTicketMutation } from './queries'

const apiClientMock = vi.hoisted(() => ({
  POST: vi.fn(),
}))

vi.mock('../../api/client', () => ({
  apiBaseUrl: 'http://localhost:5126',
  apiClient: apiClientMock,
}))

const ticketId = '018f2e6a-0000-7000-8000-000000000001'
const detailKey = ['support-tickets', 'detail', ticketId]
let mutationUnderTest: 'add' | 'cancel'
let mutate!: () => Promise<unknown>

const Harness = defineComponent({
  setup() {
    if (mutationUnderTest === 'add') {
      const mutation = useAddSupportMessageMutation(ticketId)
      mutate = () => mutation.mutateAsync({ body: 'update', rowVersion: 'AAAAAAAAAAE=' })
    }
    else {
      const mutation = useCancelSupportTicketMutation(ticketId)
      mutate = () => mutation.mutateAsync({ reasonCode: 'changed-mind', rowVersion: 'AAAAAAAAAAE=' })
    }
    return () => null
  },
})
describe('support detail mutation cache', () => {
  afterEach(() => {
    apiClientMock.POST.mockReset()
  })

  it('invalidates the complete detail after adding a message', async () => {
    apiClientMock.POST.mockResolvedValue({ data: { rowVersion: 'AAAAAAAAAAI=' } })
    mutationUnderTest = 'add'
    const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')
    const setDataSpy = vi.spyOn(queryClient, 'setQueryData')
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await mutate()

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: detailKey })
    expect(setDataSpy).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('invalidates both the complete detail and list after cancellation', async () => {
    apiClientMock.POST.mockResolvedValue({ data: { rowVersion: 'AAAAAAAAAAI=' } })
    mutationUnderTest = 'cancel'
    const queryClient = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')
    const setDataSpy = vi.spyOn(queryClient, 'setQueryData')
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })

    await mutate()

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: detailKey })
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['support-tickets', 'list'] })
    expect(setDataSpy).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})