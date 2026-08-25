import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import HomePage from './HomePage.vue'

describe('HomePage', () => {
  it('renders the customer foundation status', () => {
    const wrapper = mount(HomePage, {
      global: {
        stubs: ['RouterLink'],
      },
    })

    expect(wrapper.get('h1').text()).toBe('DoSelect 懂選')
  })
})
