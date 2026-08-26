# Requirements Analysis

- Run ID: `20260825T081500Z-m12-rebase-after-support-merge`
- Stage: `01-requirements`
- Author/runtime: `Codex / GPT-5`
- UTC time: `2026-08-25T08:15:00Z`
- Input reports: none; repository and PR state inspected directly
- Repository root: `D:\期末小組電商網站\Final\returns-m12-worktree`
- Base commit: `origin/dev` `27911e8`
- Working tree: `feature/returns-m12`; tracked files clean; pre-existing untracked `.workflow/` must remain uncommitted.

## Objective

Rebase the existing M-12 branch/PR #42 onto latest `dev` after PR #10 merged, preserving the complete Support and Returns features. Leave a locally committed, unpushed result for Codex verification.

## Evidence

PR #42 head is `fc68fcd` and GitHub reports `mergeable=false`, `mergeable_state=dirty`. Predicted overlaps include CI, OpenAPI/schema, both frontend Apps/routers/API clients, `Program.cs`, and `KafenEntityTests.cs`.

## Requirements

1. Fetch origin; verify `feature/returns-m12`, no active rebase/merge, and no tracked pre-existing changes.
2. Preserve `.workflow/`; do not stage, delete, move, or commit it.
3. Rebase M-12 onto current `origin/dev` (`27911e8`, or report a newer actual base).
4. Preserve latest Support pages/routes/navigation, SLA/workbench actor scope, private attachments and tests together with all M-12 return pages/routes/services/persistence/migration/tests/fixes.
5. Keep latest-dev CI YAML structure and SQL Server/EF flow; retain OpenAPI committed-client check exactly once and avoid duplicate YAML keys.
6. `Program.cs` must retain latest-dev registrations plus Returns services and `ReturnActorResolver`; Member/Guest behavior for cart/order/return paths stays without weakening admin authorization.
7. Frontends must expose Auth/reset-password, Support, and Returns flows together.
8. Do not text-merge generated contracts. Build the integrated API, then regenerate OpenAPI JSON and TypeScript schema.
9. Manually preserve the union of Support and Returns assertions in `KafenEntityTests.cs`.
10. No new product features, packages, scheduler/timer, second Guest Cookie, inventory mutation, refund execution, planning or log edits.
11. Use fixed .NET SDK 10.0.303 and existing locked frontend dependencies.
12. Commit integration-required tracked changes only. Do not push or edit PR #42.

## Acceptance criteria

- Rebase completes with no conflict markers/rebase state; merge-base equals the chosen latest dev.
- Only `.workflow/` may remain untracked; no tracked changes remain.
- Backend restore/build 0 warnings/errors, format verify and complete SQL Server-backed test suite pass.
- Customer/Admin Web typecheck, lint 0 warnings, tests and production builds pass.
- Final OpenAPI check passes and contains Auth, Support, and all 10 Return endpoints.
- Claude reports actual base/head, conflict decisions, commits, commands/counts and blockers; explicitly confirms no push/PR edit.

## Risks

Never resolve App/router/Program/CI/domain tests by choosing one entire side. Never hand-merge generated contracts. Do not force-push; Codex will verify and publish with exact force-with-lease.