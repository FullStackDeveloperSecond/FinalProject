/**
 * PR #24 review round 2: a lookup that needs the *complete* set (to resolve an existing
 * association's code back to a publicId, or to populate a "pick a parent" dropdown) used to
 * just request `pageSize: 500` — but `CatalogLookupListRequest.PageSize` is capped at
 * `[Range(1, 100)]` server-side, so that request was rejected outright (400 validation_failed)
 * and the lookup silently came back empty. This pages through in <=100-sized requests and
 * aggregates every item instead.
 */
export interface PageResultLike<T> {
  items: T[]
  // Generated OpenAPI types mark int32 fields as `number | string` (schema.d.ts convention),
  // so this matches that rather than forcing every list DTO to be re-cast.
  pageNumber: number | string
  pageSize: number | string
  totalCount: number | string
  totalPages?: number | string
}

const MAX_PAGE_SIZE = 100

/**
 * Safety cap on how many pages to walk. PR #24 review round 3: this used to just stop and
 * return whatever had been collected so far once the cap was hit, silently handing back an
 * incomplete set that callers (e.g. ProductEditPage's tag-code-to-publicId resolution) treat as
 * the *complete* list — anything past item 5,000 would look identical to "doesn't exist" and get
 * dropped on save. A truncated set is unsafe to use as if it were complete, so this throws
 * instead; callers must treat it as a load failure, not silently proceed.
 */
const MAX_PAGES = 50

export class FetchAllPagesTruncatedError extends Error {
  constructor() {
    super(`fetchAllPages: result set exceeds the ${MAX_PAGES * MAX_PAGE_SIZE}-item safety cap`)
    this.name = 'FetchAllPagesTruncatedError'
  }
}

export async function fetchAllPages<T>(
  fetchPage: (pageNumber: number, pageSize: number) => Promise<PageResultLike<T>>,
): Promise<T[]> {
  const items: T[] = []
  let pageNumber = 1

  while (pageNumber <= MAX_PAGES) {
    const page = await fetchPage(pageNumber, MAX_PAGE_SIZE)
    items.push(...page.items)

    const totalPages = Number(page.totalPages ?? Math.ceil(Number(page.totalCount) / Number(page.pageSize)))
    if (page.items.length === 0 || pageNumber >= totalPages) {
      return items
    }

    pageNumber += 1
  }

  throw new FetchAllPagesTruncatedError()
}
