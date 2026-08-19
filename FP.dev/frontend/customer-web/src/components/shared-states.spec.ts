import { EmptyState, ErrorState, HttpStatusPage, LoadingState } from '@doselect/web-shared/components'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

describe('shared state components', () => {
  it('announces loading and empty states accessibly', () => {
    const loading = mount(LoadingState)
    const empty = mount(EmptyState)

    expect(loading.attributes('aria-busy')).toBe('true')
    expect(loading.text()).toContain('資料載入中')
    expect(empty.text()).toContain('目前沒有資料')
  })

  it('shows tracking information and emits retry', async () => {
    const wrapper = mount(ErrorState, {
      props: {
        correlationId: 'request-123',
        traceId: 'trace-123',
      },
    })

    await wrapper.get('button').trigger('click')

    expect(wrapper.text()).toContain('request-123')
    expect(wrapper.text()).toContain('trace-123')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('renders a safe status page without internal error details', () => {
    const wrapper = mount(HttpStatusPage, {
      props: {
        status: 403,
        homeHref: '/',
      },
    })

    expect(wrapper.get('h1').text()).toBe('沒有權限')
    expect(wrapper.get('a').attributes('href')).toBe('/')
    expect(wrapper.text()).not.toContain('Stack Trace')
  })
})
