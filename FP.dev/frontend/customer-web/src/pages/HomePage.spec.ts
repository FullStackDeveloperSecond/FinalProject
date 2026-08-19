import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import HomePage from './HomePage.vue'

describe('HomePage', () => {
  it('renders the customer foundation status', () => {
    const wrapper = mount(HomePage)

    expect(wrapper.get('h1').text()).toBe('前台基礎環境已就緒')
  })
})
