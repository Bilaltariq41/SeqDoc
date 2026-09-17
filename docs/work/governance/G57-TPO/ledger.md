# G57-TPO evidence ledger

## Activation

- Claimed by Ahmad from canonical `Ready` state.
- Baseline: `ab6e3e1cf16213ee5346506b16949fa32c4ddfa4`.
- Branch: `feature/issue-57-transactional-project-operations`.
- Claim receipt: https://github.com/Bilaltariq41/SeqDoc/issues/57#issuecomment-5711611308
- Current-main validation before activation: 50 work items valid; execution projection current and idle.

## Verification

- Repair rerun focused command: `python -B -m unittest tests.governance.test_work_state` — **28/28 passed**.
- Self-review: inspected the complete diff from `8b1512f`; `git diff --check` passed. Repaired activation admission,
  execution migration, typed claims, capsule transitions, metadata validation, transaction cleanup/recovery, legacy
  transition behavior, review handoff, closeout isolation, promotion, and bounded projection compatibility.
- Requested post-checks: `git diff --check` passed; `python -B tools/governance/work_state.py validate --root .`
  reported **50 work items valid**; `project-execution --root . --check` reported **execution projection: current**.
- Final gate, independent review, and lifecycle transition were intentionally not run.

## Repair round 1 dispositions

- F1: **Fixed** — handoff observes the PR with authenticated `gh pr view`; caller-observed head/author values cannot override it.
- F2: **Fixed** — closeout requires authenticated merged PR state, authoritative head/merge SHA, and validated receipts.
- F3: **Fixed** — peer designation is stored at handoff; exact-head `APPROVED` review by that peer is required at closeout.
- F4: **Fixed** — blocked/cancelled transition clears only the target execution, review metadata, and claims atomically.
- F5: **Fixed** — recovery rejects symlink/reparse components and escaped journal targets before reading or writing.
- F6: **Fixed** — scaffold consumes and validates paired checkpoint ID/path inputs and writes them transactionally.
- F7: **Fixed** — projection uses exact item/lifecycle markers with distinct deterministic start and closure packets.

Focused verification passed 28/28 tests with 1 platform-dependent symlink skip. Canonical validation, projection check, and
`git diff --check` passed. No final gate or human approval is claimed.

## Repair self-review discovery

- Handoff records the authenticated request boundary as `requestHead` (H1). Closeout authenticates the merged PR's
  actual head (H2), requires the stored peer's exact-head approval at H2, rejects caller/approval/merge mismatches, and
  persists H2 as the closeout head.
- Focused verification rerun: **28/28 passed**, with 1 platform-dependent symlink skip. Post-checks remained green.

## Repair self-review regression

- **Fixed** — re-handoff now accepts `Active` epoch 1 and later `ResolvingFindings` epochs only when the authenticated
  PR advances beyond the prior `requestHead`, the peer remains valid (unless explicitly authorized to change), and
  complete sorted dispositions are supplied. It atomically replaces request-boundary evidence and preserves final-head
  authentication at closeout.
- Focused repair rerun required normalization of complete dispositions into deterministic sorted order while retaining
  duplicate/invalid rejection.

## Real-GitHub author-shape probe

- Failed-safe probe: the authenticated handoff rejected a documented nested human author payload because validation
  incorrectly required the nested object to contain only `login`.
- **Fixed** — nested `id`, `is_bot`, `name`, and benign fields are accepted; login remains required and valid,
  `is_bot` is type-checked, and bot authors are rejected for the human-peer workflow. Top-level PR identity, state,
  head, merge, and SHA validation remains strict.

## Owner-authorized takeover trace

- `resume` is bounded to one `Blocked` → `ResolvingFindings` transition. It authenticates the actual clean checkout
  HEAD, branch, and 40-character start head, while treating caller-supplied values only as expectations.
- The deterministic takeover record contains exactly `authorizationReceipt`, `reason`, and `startHead`; baseline,
  owner, branch, checkpoint, contract revision, and PR are preserved. Claims are normalized before persistence, and
  selected, stale, dirty, malformed, conflicting, and repeated requests fail before any payload is written.
- Registry, checkpoint capsule, and execution projection are committed through the same atomic transaction. No resume
  invocation, GitHub mutation, lifecycle projection, or final gate was performed during this takeover.
- Focused verification after the path-normalization expectation repair: `python -B -m unittest
  tests.governance.test_work_state` — **28/28 passed**, with **1 skipped** platform-dependent symlink case.

## Strengthened takeover and author boundaries

- `resume` now requires explicit nonempty `current_head`, `start_head`, and `next_action`; both heads must be
  lowercase 40-character SHAs matching the actual observed HEAD. Success persists `reason` as `statusReason` and
  `next_action` as `nextAction`.
- Authenticated PR authors now require `login`, a bounded opaque string node ID compatible with `U_kgDODXRwzA`, and a
  boolean `is_bot`; whitespace, invalid, overlong, numeric, boolean, and missing node IDs are rejected. Human handoff
  continues to reject `is_bot: true`, while benign author fields remain compatible.

## Resume execution trace

- `resume` was invoked successfully at start head `d3110626d011b46729f8fcf3c4c33e359341a4d5`.
- Durable state moved to `ResolvingFindings` in commit `d5e336c`.
- The first resumed focused run exposed one stale Blocked-only read assertion. It was repaired to derive exact
  execution expectations from canonical selection.
- Focused verification now reports **28 passed, 1 skipped**. No review or final gate is claimed.

## Independent review findings F1-F4

- F1: **Fixed** — resume derives the repository owner from validated `owner/repo`, compares the authenticated comment
  login case-insensitively (never against assigned contributor `item.owner`), requires `OWNER` association, and stores
  the exact authenticated login with URL, body digest, reason, and start head.
- F2: **Fixed** — closeout re-fetches and revalidates the takeover receipt and digest; the legacy three-field record is
  readable only for migration and cannot authorize closure.
- F3: **Fixed** — closeout requires attribution to equal the authenticated PR author and stored handoff author, and
  persists only the authenticated author.
- F4: **Fixed** — reviews use strict `gh api --paginate --slurp` page-array flattening and require reviewer login plus
  `type == User`.

Current receipt migration is **pending the owner comment**. No GitHub write, block/resume, handoff, rereview, or final
gate was performed.
