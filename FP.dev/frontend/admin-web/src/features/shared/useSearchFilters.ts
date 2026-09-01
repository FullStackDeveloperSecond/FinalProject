import { computed, reactive, ref, watch } from 'vue'

export interface SearchFilters {
  q: string
  pageNumber: number
}

/**
 * PR #24 review round 7 (P3): BrandsPage/CategoriesPage/TagsPage bound their keyword input
 * straight into the same reactive object the list query watches, so every keystroke re-queried
 * immediately — but `pageNumber` only reset to 1 on an explicit search() call (form submit).
 * An admin on page 2 who started typing a new keyword would query that keyword against the
 * stale page 2, which could show "no results" even though page 1 has matches.
 *
 * `filters.q` stays the live v-model target for instant input feedback; the query itself reads
 * a debounced copy, and `pageNumber` is reset to 1 the instant `q` changes (not only once the
 * debounce settles) so the mismatch window this bug depended on cannot occur. Calling `search()`
 * (Enter / clicking 搜尋) applies the current input immediately, bypassing the debounce.
 */
export function useSearchFilters(pageSize: number, debounceMs = 300) {
  const filters = reactive<SearchFilters>({ q: '', pageNumber: 1 })
  const queriedQ = ref('')
  let timer: ReturnType<typeof setTimeout> | undefined
  let restoredQ: string | undefined

  watch(() => filters.q, (value) => {
    // Programmatic URL restoration sets q and pageNumber as one state transition. Do not let
    // the ordinary typing watcher reset that restored page back to 1 on the next tick.
    if (restoredQ === value) {
      restoredQ = undefined
      return
    }
    restoredQ = undefined
    filters.pageNumber = 1
    if (timer !== undefined) {
      clearTimeout(timer)
    }
    timer = setTimeout(() => {
      queriedQ.value = value
    }, debounceMs)
  })

  const listParams = computed(() => ({
    q: queriedQ.value,
    pageNumber: filters.pageNumber,
    pageSize,
  }))

  function search() {
    if (timer !== undefined) {
      clearTimeout(timer)
      timer = undefined
    }
    filters.pageNumber = 1
    queriedQ.value = filters.q
  }

  function goToPage(nextPage: number) {
    filters.pageNumber = nextPage
  }

  function restore(q: string, pageNumber: number) {
    if (timer !== undefined) {
      clearTimeout(timer)
      timer = undefined
    }

    restoredQ = filters.q === q ? undefined : q
    filters.q = q
    queriedQ.value = q
    filters.pageNumber = pageNumber
  }

  return { filters, listParams, search, goToPage, restore }
}
