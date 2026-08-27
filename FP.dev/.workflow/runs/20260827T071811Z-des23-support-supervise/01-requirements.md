# DES-23 客服主管操作需求

- Run ID: `20260827T071811Z-des23-support-supervise`
- Stage: Requirements analysis
- Author/runtime: Codex / GPT-5
- UTC time: 2026-08-27T07:18:11Z
- Input report paths: none; implementation evidence supplied by Claude Code and repository diff
- Repository root: `D:\期末小組電商網站\Final\support-des23-worktree\FP.dev`
- Base commit: `origin/dev@8ef986c160e2939d21f3ee3d268165dc82a7ea4f`
- Working-tree summary: DES-23 commits rebased onto latest dev; pre-existing untracked `.localdata/` preserved

## Objective

Complete the DES-23 support supervision vertical slice without changing return, refund, inventory, or other members'' modules.

## Acceptance criteria

1. Assign, transfer, priority, status, cancel, reopen, and internal-note operations enforce the approved Handle/Supervise role matrix.
2. SuperAdmin-only may supervise but may not perform ordinary Handle actions; multiple roles use their permission union.
3. Assignment races and stale RowVersion produce stable conflicts with no partial history or audit effects.
4. Reopen follows the three-day rule and recalculates the applicable resolution SLA.
5. Case Workbench applies actor scope before SQL materialization and binds its cursor to actor/supervisor scope.
6. Admin UI exposes allowed actions and refreshes detail, SLA queue, and workbench after success or conflict.
7. Existing dev features and generated API contracts remain compatible.

## Constraints

- Rebase, do not merge, the latest `origin/dev`.
- No Migration or cross-module business behavior.
- Fixed .NET SDK 10.0.303 and provider-backed SQL Server verification.
- Preserve unrelated local files and do not commit `.localdata/`.
