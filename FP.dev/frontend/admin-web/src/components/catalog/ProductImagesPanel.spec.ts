import { flushPromises, mount } from '@vue/test-utils'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '@doselect/web-shared/api'
import type { AdminProductImageDto } from '../../features/products/types'

const mockUploadProductImage = vi.fn()
const mockUpdateProductImage = vi.fn()
const mockPublishProductImage = vi.fn()
const mockDeleteProductImage = vi.fn()

vi.mock('../../features/products/api', () => ({
  uploadProductImage: mockUploadProductImage,
  updateProductImage: mockUpdateProductImage,
  publishProductImage: mockPublishProductImage,
  deleteProductImage: mockDeleteProductImage,
  getAdminProduct: vi.fn(),
  listAdminProducts: vi.fn(),
  createProduct: vi.fn(),
  updateProduct: vi.fn(),
  applyBulkProductAction: vi.fn(),
  exportAdminProducts: vi.fn(),
}))

const { default: ProductImagesPanel } = await import('./ProductImagesPanel.vue')

function image(overrides: Partial<AdminProductImageDto> = {}): AdminProductImageDto {
  return {
    publicId: 'img-1',
    productPublicId: 'p1',
    status: 'Ready',
    altText: '正面',
    sortOrder: 0,
    isPrimary: true,
    sourceUrl: null,
    licenseName: null,
    licenseUrl: null,
    hasCompleteMetadata: false,
    originalFileName: 'front.png',
    mediaType: 'image/png',
    fileSizeBytes: 1024,
    width: 2000,
    height: 1000,
    previewPathBase: '/api/v1/admin/product-images/img-1/preview',
    variants: [
      { variant: '320', width: 320, height: 160, publicUrl: null },
      { variant: '800', width: 800, height: 400, publicUrl: null },
      { variant: '1600', width: 1600, height: 800, publicUrl: null },
    ],
    createdAtUtc: '2026-09-04T00:00:00Z',
    updatedAtUtc: '2026-09-04T00:00:00Z',
    publishedAtUtc: null,
    rowVersion: 'AAA=',
    ...overrides,
  }
}

function mountPanel(images: AdminProductImageDto[]) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
  const wrapper = mount(ProductImagesPanel, {
    props: { productPublicId: 'p1', images },
    global: { plugins: [[VueQueryPlugin, { queryClient }]] },
  })
  return { wrapper, invalidate }
}

describe('ProductImagesPanel', () => {
  beforeEach(() => {
    mockUploadProductImage.mockReset()
    mockUpdateProductImage.mockReset()
    mockPublishProductImage.mockReset()
    mockDeleteProductImage.mockReset()
  })

  it('renders thumbnails from the authorized preview route and marks the primary image', () => {
    const { wrapper } = mountPanel([image(), image({ publicId: 'img-2', isPrimary: false, sortOrder: 1 })])

    const thumbnails = wrapper.findAll('img')
    expect(thumbnails).toHaveLength(2)
    expect(thumbnails[0]!.attributes('src')).toBe('/api/v1/admin/product-images/img-1/preview/320')
    expect(thumbnails[0]!.attributes('width')).toBe('320')
    expect(wrapper.findAll('.product-images__badge')).toHaveLength(1)
  })

  it('uploads the selected file with the optional metadata as multipart input and resets the form', async () => {
    mockUploadProductImage.mockResolvedValueOnce(image())
    const { wrapper, invalidate } = mountPanel([])
    const file = new File(['png'], 'front.png', { type: 'image/png' })
    const input = wrapper.find('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [file] })
    await input.trigger('change')
    await wrapper.find('input[aria-label="上傳 Alt"]').setValue('正面')
    await wrapper.find('input[aria-label="上傳來源網址"]').setValue('https://example.com/src')

    await wrapper.find('form[aria-label="上傳商品圖片"]').trigger('submit')
    await flushPromises()

    expect(mockUploadProductImage).toHaveBeenCalledWith('p1', {
      file,
      altText: '正面',
      sourceUrl: 'https://example.com/src',
      licenseName: undefined,
      licenseUrl: undefined,
    })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin-products', 'detail', 'p1'] })
    expect((wrapper.find('input[aria-label="上傳 Alt"]').element as HTMLInputElement).value).toBe('')
  })

  it('disables publishing until the metadata is complete and publishes with the current RowVersion', async () => {
    mockPublishProductImage.mockResolvedValueOnce(image({ status: 'Published' }))
    const { wrapper } = mountPanel([image({ hasCompleteMetadata: false })])
    const publishButton = () => wrapper.findAll('button').find((button) => button.text() === '發布')!
    expect(publishButton().attributes('disabled')).toBeDefined()

    await wrapper.setProps({ images: [image({ hasCompleteMetadata: true, rowVersion: 'BBB=' })] })
    expect(publishButton().attributes('disabled')).toBeUndefined()
    await publishButton().trigger('click')
    await flushPromises()

    expect(mockPublishProductImage).toHaveBeenCalledWith('img-1', 'BBB=')
  })

  it('edits metadata with the RowVersion captured when editing started', async () => {
    mockUpdateProductImage.mockResolvedValueOnce(image())
    const { wrapper } = mountPanel([image({ rowVersion: 'AAA=' })])

    await wrapper.findAll('button').find((button) => button.text() === '編輯')!.trigger('click')
    // 背景重抓帶來新的 RowVersion——送出時仍要用按下「編輯」那一刻的 token。
    await wrapper.setProps({ images: [image({ rowVersion: 'CCC=' })] })
    await wrapper.find('input[aria-label="圖片 Alt"]').setValue('顯示卡正面')
    await wrapper.find('input[aria-label="來源網址"]').setValue('https://example.com/src')
    await wrapper.find('input[aria-label="授權名稱"]').setValue('CC BY 4.0')
    await wrapper.find('input[aria-label="授權網址"]').setValue('https://creativecommons.org/licenses/by/4.0/')
    await wrapper.findAll('button').find((button) => button.text() === '儲存')!.trigger('click')
    await flushPromises()

    expect(mockUpdateProductImage).toHaveBeenCalledWith('img-1', {
      altText: '顯示卡正面',
      sortOrder: 0,
      sourceUrl: 'https://example.com/src',
      licenseName: 'CC BY 4.0',
      licenseUrl: 'https://creativecommons.org/licenses/by/4.0/',
      rowVersion: 'AAA=',
    })
  })

  it('asks for confirmation before deleting and shows the catalogued error message on failure', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    mockDeleteProductImage.mockRejectedValueOnce(new ApiError('stale', { status: 409, code: 'concurrency_conflict', correlationId: 'corr-1' }))
    const { wrapper } = mountPanel([image()])
    const deleteButton = () => wrapper.findAll('button').find((button) => button.text() === '刪除')!

    await deleteButton().trigger('click')
    expect(mockDeleteProductImage).not.toHaveBeenCalled()

    await deleteButton().trigger('click')
    await flushPromises()

    expect(mockDeleteProductImage).toHaveBeenCalledWith('img-1', 'AAA=')
    expect(wrapper.text()).toContain('此資料已被其他人修改')
  })

  it('links to the public URL once published', () => {
    const { wrapper } = mountPanel([image({
      status: 'Published',
      hasCompleteMetadata: true,
      variants: [
        { variant: '320', width: 320, height: 160, publicUrl: '/media/products/img-1/320/abc.webp' },
        { variant: '800', width: 800, height: 400, publicUrl: '/media/products/img-1/800/def.webp' },
        { variant: '1600', width: 1600, height: 800, publicUrl: '/media/products/img-1/1600/ghi.webp' },
      ],
    })])

    const link = wrapper.find('a[href="/media/products/img-1/800/def.webp"]')
    expect(link.exists()).toBe(true)
    expect(wrapper.findAll('button').find((button) => button.text() === '發布')!.attributes('disabled')).toBeDefined()
  })
})
