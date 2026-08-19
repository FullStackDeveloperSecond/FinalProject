import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import HomePage from './HomePage.vue'

describe('HomePage', () => {
  it('renders the administration foundation status', () => {
    const wrapper = mount(HomePage)

    expect(wrapper.get('h1').text()).toBe('管理後台基礎環境已就緒')
  })
})
