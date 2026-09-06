# DoSelect product-search-v11 / support-v5 SEARCH-NOVICE-020 focused verification

## Verdict

`PASS`. The post-merge Fast-tier and existing-part deduplication check completed 3/3 provider calls, passed 3/3 deterministic checks and 3/3 formal human reviews, and had no timeout or premature recommendation.

## Pinned execution

- Revision: `bfe2420c9d96e607e44259188491a18b983deb3f`
- Run ID: `20260906T202525Z-v11-support-v5-search020-focused-postmerge-bfe2420c`
- Versions: `product-search-v11` / `support-v5`; dataset `zh-TW-v1.0.10-draft`; fixture `v1.0.4`; grader `deterministic-v1.1.8`
- Scope: `SEARCH-NOVICE-020` x 3 trials; 3 planned and 3 actual requests
- Cost: US$0.001789; stop line US$0.03; not stopped
- Latency: 1,627–2,759 ms; product P95 2,759 ms; approved limit 5,000 ms
- Data: synthetic only; no production data or real personal data

## Human review

All trials preserved the motherboard `SingleProduct` intent, TWD 7,000 maximum, and Wi-Fi preference. The AM5 CPU appeared only as one unconfirmed existing-part proposal with `CPU_SOCKET=AM5`; it was not repeated in top-level required specifications or preferences. Each trial stopped at the application confirmation boundary and produced no recommendation.

## Evidence

The ignored raw artifacts, separate formal review, T2 evidence manifest, hashes, and sanitization results are stored at:

`FP.dev/.run/ai-evals/20260906T202525Z-v11-support-v5-search020-focused-postmerge-bfe2420c`

This focused run proves only the repaired `SEARCH-NOVICE-020` path. It does not replace the complete release baseline.
