# Requirements Analysis

- Run ID: `20260826T020000Z-m12-review-p1-repair`
- Stage: `01-requirements`
- Author/runtime: `Codex / GPT-5`
- UTC time: `2026-08-26T02:00:00Z`
- Input reports: group-lead PR #42 review; prior run `20260825T081500Z-m12-rebase-after-support-merge/06-final.md`
- Repository root: `D:\期末小組電商網站\Final\returns-m12-worktree`
- Base commit: latest fetched `origin/dev` `64b0ef4`
- Working tree: `feature/returns-m12` at `8e5dd06`; tracked clean; `.workflow/` untracked and excluded.

## Objective

Repair the remaining PR #42 review findings, rebase onto latest dev, and leave a locally committed but unpushed implementation for Codex to author independent regression tests, verify and publish.

## Confirmed current state

- The duplicate private-attachment route finding is stale for current head: Support and Returns already share one controller and HTTP coverage. Preserve this integration through rebase.
- Latest dev advanced beyond the reviewed base to `64b0ef4`; a fresh rebase is required.
- Guest upload still writes synthetic `guest-order:{id}` into required ApplicationUser FK and lacks storage cleanup on DB failure.
- Review/Inspect exact-set validation still permits duplicate IDs with omissions.
- Admin UI always sends `inspectionRequired: true`.
- C-19 still asks customers for internal PublicId/RowVersion because C-18 handoff is absent.

## Decisions

1. Guest uploader schema: `ReturnAttachments.UploadedByUserId` becomes nullable Member FK; add nullable `UploadedByGuestOrderId` FK to Orders. Add a check constraint requiring exactly one uploader identity. Never persist synthetic user IDs.
2. Storage compensation: after successful file store, any DB persistence failure/cancellation must attempt delete by storage key. Preserve the original exception; if cleanup also fails, surface/report a compensation exception without leaking storage details.
3. Exact-set: approval and inspection inputs must have distinct ReturnItem PublicIds and their set must equal the stored ReturnItem set exactly. Reject duplicate, omitted and foreign IDs before mutation.
4. Shared attachment route: retain the current single-controller Support-first/Return-fallback behavior, independent actor authorization and uniform 404.
5. C-19 dependency: keep the route for integration use but clearly label the page as blocked on C-18, explain that normal customers should enter through order detail, and disable formal submission unless required handoff data is supplied by navigation state/query contract. Do not invent C-18 APIs or ask customers to type internal IDs/Base64.
6. Admin no-shipment path: expose a clear `需要寄回檢查` toggle defaulting to true; when false send `inspectionRequired: false` for every item and clearly explain direct `AwaitingRefund` behavior.
7. Cooling-off: use Asia/Taipei calendar-day semantics while storing UTC. Seven-day deadline is the start of the local calendar day after the seventh eligible day, converted to UTC. Test delivery around UTC/local midnight and DST-independent Windows/Linux timezone resolution.

## Constraints

- Do not create a second attachment route/controller.
- Do not weaken Member/Admin/Guest actor scope or uniform 404 behavior.
- Do not add scheduler, Guest Cookie mint, inventory mutation, refund execution, packages, planning or log changes.
- Do not stage/commit `.workflow/`.
- Implementation stage does not author independent SQL Server/HTTP acceptance tests; Codex owns them after handoff. Claude may update compile-coupled unit fixtures and existing tests only as necessary.
- Do not push or edit PR #42.

## Acceptance criteria

- Rebased onto actual latest origin/dev; no conflict/rebase state.
- Guest and Member upload entities persist with valid mutually-exclusive uploader identities; no synthetic FK.
- Stored file is deleted on DB failure; original error semantics remain stable.
- Duplicate/omitted approval or inspection item sets return stable validation failure and perform no transition.
- Single private-attachment route remains.
- C-19 no longer presents internal IDs/row versions as normal customer inputs.
- Admin can select shipment-required or direct-refund review path.
- Cooling deadline follows Asia/Taipei calendar days with UTC persistence.
- Build/typecheck succeeds; tracked tree clean after local commit; no push.