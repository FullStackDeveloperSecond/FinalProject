import { ref } from 'vue'
import { exportOperationalReport, fetchOperationalReport } from './api'
import type { OperationalReportExportFormat } from './api'
import type {
  OperationalReportFilters,
  OperationalReportKey,
  OperationalReportResult,
} from './types'

export function useOperationalReport() {
  const data = ref<OperationalReportResult | null>(null)
  const isLoading = ref(false)
  const isLoadingMore = ref(false)
  const isExporting = ref(false)
  const error = ref<unknown>(null)
  const actionError = ref<unknown>(null)
  let requestSequence = 0

  async function load(
    reportKey: OperationalReportKey,
    filters: OperationalReportFilters,
  ): Promise<void> {
    const sequence = ++requestSequence
    isLoading.value = true
    error.value = null
    actionError.value = null
    try {
      const result = await fetchOperationalReport(reportKey, filters)
      if (sequence === requestSequence) {
        data.value = result
      }
    } catch (caught) {
      if (sequence === requestSequence) {
        error.value = caught
        data.value = null
      }
    } finally {
      if (sequence === requestSequence) {
        isLoading.value = false
      }
    }
  }

  async function loadMore(
    reportKey: OperationalReportKey,
    filters: OperationalReportFilters,
  ): Promise<void> {
    const cursor = data.value?.rows.nextCursor
    if (!cursor || isLoadingMore.value) return

    const sequence = requestSequence
    isLoadingMore.value = true
    actionError.value = null
    try {
      const result = await fetchOperationalReport(reportKey, filters, cursor)
      if (sequence === requestSequence && data.value) {
        data.value = {
          ...result,
          rows: {
            ...result.rows,
            items: [...data.value.rows.items, ...result.rows.items],
          },
        }
      }
    } catch (caught) {
      if (sequence === requestSequence) {
        actionError.value = caught
      }
    } finally {
      if (sequence === requestSequence) {
        isLoadingMore.value = false
      }
    }
  }

  async function download(
    reportKey: OperationalReportKey,
    filters: OperationalReportFilters,
    format: OperationalReportExportFormat = 'csv',
  ): Promise<void> {
    if (isExporting.value) return
    isExporting.value = true
    actionError.value = null
    try {
      const blob = await exportOperationalReport(reportKey, filters, format)
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = `${reportKey}-${filters.fromDate}-${filters.toDate}.${format}`
      link.click()
      URL.revokeObjectURL(url)
    } catch (caught) {
      actionError.value = caught
    } finally {
      isExporting.value = false
    }
  }

  return {
    data,
    error,
    actionError,
    isLoading,
    isLoadingMore,
    isExporting,
    load,
    loadMore,
    download,
  }
}
