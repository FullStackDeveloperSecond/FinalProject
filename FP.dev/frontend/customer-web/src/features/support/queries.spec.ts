import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'

describe('support attachment query', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('uploads multipart data with member antiforgery conventions and refreshes detail', async () => {
    const attachment = {
      publicId: '018f2e6a-0000-7000-8000-000000000099',
      originalFileName: 'receipt.pdf',
      mimeType: 'application/pdf',
      fileSizeBytes: 7,
      createdAtUtc: '2026-08-19T03:30:00Z',
    }
    const fetchStub = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(Response.json({ requestToken: 'csrf-token' }))
      .mockResolvedValueOnce(Response.json(attachment))
    vi.stubGlobal('fetch', fetchStub)

    const { useUploadSupportAttachmentMutation } = await import('./queries')
    const queryClient = new QueryClient({
      defaultOptions: { mutations: { retry: false } },
    })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')
    let upload!: (file: File) => Promise<unknown>
    const Harness = defineComponent({
      setup() {
        const mutation = useUploadSupportAttachmentMutation('018f2e6a-0000-7000-8000-000000000001')
        upload = (file) => mutation.mutateAsync(file)
        return () => null
      },
    })
    const wrapper = mount(Harness, {
      global: { plugins: [[VueQueryPlugin, { queryClient }]] },
    })
    const file = new File(['receipt'], 'receipt.pdf', { type: 'application/pdf' })

    await expect(upload(file)).resolves.toEqual(attachment)

    expect(fetchStub).toHaveBeenCalledTimes(2)
    expect(fetchStub.mock.calls[0]?.[0])
      .toBe('http://localhost:5126/api/v1/security/antiforgery-token')
    expect(fetchStub.mock.calls[0]?.[1]).toMatchObject({ credentials: 'include' })
    expect(new Headers(fetchStub.mock.calls[0]?.[1]?.headers).get('X-DoSelect-Client'))
      .toBe('member')

    const [uploadUrl, uploadInit] = fetchStub.mock.calls[1] ?? []
    expect(uploadUrl)
      .toBe('http://localhost:5126/api/v1/support-tickets/018f2e6a-0000-7000-8000-000000000001/attachments')
    expect(uploadInit).toMatchObject({ method: 'POST', credentials: 'include' })
    const uploadHeaders = new Headers(uploadInit?.headers)
    expect(uploadHeaders.get('X-XSRF-TOKEN')).toBe('csrf-token')
    expect(uploadHeaders.get('X-Correlation-ID')).toMatch(/^[0-9a-f]{32}$/)
    expect(uploadHeaders.has('Content-Type')).toBe(false)
    expect(uploadInit?.body).toBeInstanceOf(FormData)
    expect((uploadInit?.body as FormData).get('file')).toBe(file)
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ['support-tickets', 'detail', '018f2e6a-0000-7000-8000-000000000001'],
    })

    wrapper.unmount()
  })
})
