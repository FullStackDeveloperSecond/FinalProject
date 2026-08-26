import { createQuerySerializer } from 'openapi-fetch'

/**
 * PR #24 review round 2: openapi-fetch's default querySerializer throws
 * "Deeply-nested arrays/objects aren't supported" the moment an array element is itself an
 * object — `Specs` (SpecFilterRequest[]) hits exactly that, so any spec-filter selection failed
 * before the request even reached the network. ASP.NET Core's default query-string model
 * binder for a `[FromQuery] IReadOnlyList<SpecFilterRequest> Specs` property expects the
 * indexed-property convention below (`Specs[0].SemanticKey=...`), so that's what this produces
 * for `Specs` specifically; every other (primitive/simple-array) param still goes through the
 * library's own default serializer unchanged.
 */
const defaultSerializer = createQuerySerializer()

interface SpecFilterRequestLike {
  semanticKey: string
  operator: string
  value?: string | null
  values?: string[] | null
}

export function productSearchQuerySerializer(query: Record<string, unknown>): string {
  const { Specs, ...rest } = query as Record<string, unknown> & { Specs?: SpecFilterRequestLike[] | null }

  const parts: string[] = []
  const restSerialized = defaultSerializer(rest)
  if (restSerialized) {
    parts.push(restSerialized)
  }

  if (Specs && Specs.length > 0) {
    Specs.forEach((spec, index) => {
      const prefix = `Specs[${index}]`
      parts.push(`${prefix}.SemanticKey=${encodeURIComponent(spec.semanticKey)}`)
      parts.push(`${prefix}.Operator=${encodeURIComponent(spec.operator)}`)
      if (spec.value != null) {
        parts.push(`${prefix}.Value=${encodeURIComponent(spec.value)}`)
      }
      if (spec.values) {
        spec.values.forEach((value, valueIndex) => {
          parts.push(`${prefix}.Values[${valueIndex}]=${encodeURIComponent(value)}`)
        })
      }
    })
  }

  return parts.join('&')
}
