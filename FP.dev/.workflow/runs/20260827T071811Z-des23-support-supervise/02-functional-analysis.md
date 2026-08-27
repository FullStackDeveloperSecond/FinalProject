# DES-23 功能分析

- Run ID: `20260827T071811Z-des23-support-supervise`
- Stage: Functional analysis
- Author/runtime: Codex / GPT-5
- UTC time: 2026-08-27T07:18:11Z
- Input report paths: `01-requirements.md`
- Repository root: `D:\期末小組電商網站\Final\support-des23-worktree\FP.dev`
- Base commit: `origin/dev@8ef986c160e2939d21f3ee3d268165dc82a7ea4f`
- Working-tree summary: implementation present in two rebased feature commits

## Affected components

- Support domain transitions and SLA reopen behavior.
- Admin application service/store DTOs, optimistic concurrency, history, and audit writes.
- Admin Support HTTP policies and role matrix.
- Admin Web support detail actions and Case Workbench page.
- OpenAPI and generated TypeScript contracts.

## Key decisions

- Controller actions use the existing Handle, Supervise, or Admin entry policies; no new policy family is introduced.
- Assignment/transfer use conditional SQL updates; other mutations use RowVersion optimistic concurrency.
- Ticket, history, and audit effects are committed atomically.
- Case Workbench filtering occurs in SQL and its cursor fingerprint includes actor identity and supervisory scope.
- Conflict responses trigger refetch of all affected read models.

## Test strategy

- Domain and application tests for allowed transitions, role-sensitive actions, reopen SLA, and zero-side-effect failures.
- Provider-backed SQL Server tests for assignment races, scope filtering, cursor behavior, and audit/history atomicity.
- HTTP policy acceptance tests for the complete role matrix.
- Admin Web unit tests plus typecheck, zero-warning lint, and production build.
- Full backend suite after rebase, followed by generated-contract consistency verification.

## Risks

- Shared SQL integration data can exceed one SLA page; scope acceptance tests must traverse the official cursor rather than assume the seeded records are on page one.
- Generated OpenAPI artifacts must be recreated after rebasing newer dev endpoints.
