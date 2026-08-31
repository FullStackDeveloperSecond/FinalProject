import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import type { MemberAddress } from '../../features/members/api'

const mockFetchAddresses = vi.fn<() => Promise<MemberAddress[]>>()
const mockCreateAddress = vi.fn<(...args: unknown[]) => Promise<MemberAddress>>()
const mockUpdateAddress = vi.fn<(...args: unknown[]) => Promise<MemberAddress>>()
const mockDeleteAddress = vi.fn<(...args: unknown[]) => Promise<void>>()

vi.mock('../../features/members/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../features/members/api')>()
  return {
    ...actual,
    fetchAddresses: () => mockFetchAddresses(),
    createAddress: (...args: unknown[]) => mockCreateAddress(...args),
    updateAddress: (...args: unknown[]) => mockUpdateAddress(...args),
    deleteAddress: (...args: unknown[]) => mockDeleteAddress(...args),
  }
})

function address(overrides: Partial<MemberAddress> = {}): MemberAddress {
  return {
    publicId: 'address-1',
    label: '住家',
    recipientName: '王小明',
    phone: '0912345678',
    postalCode: '100',
    city: '台北市',
    district: '中正區',
    addressLine1: '測試路一號',
    addressLine2: null,
    isDefault: false,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: '2026-01-01T00:00:00Z',
    rowVersion: 'AAAA',
    ...overrides,
  }
}

async function mountAddressesPage() {
  const { default: AddressesPage } = await import('./AddressesPage.vue')
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return mount(AddressesPage, {
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })
}

beforeEach(() => {
  mockFetchAddresses.mockReset()
  mockCreateAddress.mockReset()
  mockUpdateAddress.mockReset()
  mockDeleteAddress.mockReset()
})

describe('AddressesPage', () => {
  it('shows the loading state before addresses resolve', async () => {
    mockFetchAddresses.mockReturnValue(new Promise(() => {}))
    const wrapper = await mountAddressesPage()

    expect(wrapper.text()).toContain('收件地址載入中')
  })

  it('shows an empty state when there are no addresses', async () => {
    mockFetchAddresses.mockResolvedValueOnce([])
    const wrapper = await mountAddressesPage()
    await flushPromises()

    expect(wrapper.text()).toContain('尚未新增收件地址')
  })

  it('renders each address and marks the default one', async () => {
    mockFetchAddresses.mockResolvedValueOnce([address({ isDefault: true })])
    const wrapper = await mountAddressesPage()
    await flushPromises()

    expect(wrapper.text()).toContain('住家')
    expect(wrapper.text()).toContain('王小明')
    expect(wrapper.text()).toContain('預設')
  })

  it('creates a new address and hides the form on success', async () => {
    mockFetchAddresses.mockResolvedValue([])
    mockCreateAddress.mockResolvedValueOnce(address())
    const wrapper = await mountAddressesPage()
    await flushPromises()

    await wrapper.get('header button').trigger('click')
    await wrapper.get('#address-label').setValue('住家')
    await wrapper.get('#address-recipient-name').setValue('王小明')
    await wrapper.get('#address-phone').setValue('0912345678')
    await wrapper.get('#address-postal-code').setValue('100')
    await wrapper.get('#address-city').setValue('台北市')
    await wrapper.get('#address-district').setValue('中正區')
    await wrapper.get('#address-line1').setValue('測試路一號')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(mockCreateAddress).toHaveBeenCalledWith({
      label: '住家',
      recipientName: '王小明',
      phone: '0912345678',
      postalCode: '100',
      city: '台北市',
      district: '中正區',
      addressLine1: '測試路一號',
      addressLine2: null,
      isDefault: false,
    })
    expect(wrapper.find('.address-form').exists()).toBe(false)
  })

  it('edits an existing address inline', async () => {
    mockFetchAddresses.mockResolvedValue([address()])
    mockUpdateAddress.mockResolvedValueOnce(address({ label: '公司' }))
    const wrapper = await mountAddressesPage()
    await flushPromises()

    await wrapper.get('.address-card__actions button').trigger('click')
    await wrapper.get('#address-label').setValue('公司')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(mockUpdateAddress).toHaveBeenCalledWith(
      'address-1',
      expect.objectContaining({ label: '公司', rowVersion: 'AAAA' }),
    )
  })

  it('deletes an address and shows an error without removing it on failure', async () => {
    mockFetchAddresses.mockResolvedValue([address()])
    mockDeleteAddress.mockRejectedValueOnce(new ApiError('Conflict', {
      status: 409,
      code: 'concurrency_conflict',
    }))
    const wrapper = await mountAddressesPage()
    await flushPromises()

    const buttons = wrapper.findAll('.address-card__actions button')
    await buttons[1]!.trigger('click')
    await flushPromises()

    expect(mockDeleteAddress).toHaveBeenCalledWith('address-1', 'AAAA')
    expect(wrapper.text()).toContain('已被更新')
    expect(wrapper.text()).toContain('住家')
  })
})
