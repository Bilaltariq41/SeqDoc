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

The earlier owner-receipt migration note is superseded by the authenticated two-peer resume below. No rereview or final
gate is claimed.

## Authorized two-peer takeover route

- Exact peer approvals: issue comment `5713964023` by `Abood-essa`; issue comment `5714397001` by `Qhatahet`.
- Route: exactly two authenticated `--peer-authorization-receipt` URLs plus `--authorization-head`, mutually exclusive
  with the owner receipt route.
- Implemented persistence uses `mode: two-peer`, authorization head, sorted URL/digest/authenticated-login receipts,
  reason, and start head. Owner mode has a separate explicit `mode: owner` shape; malformed mixed shapes are rejected.
- Closeout reauthenticates both peer comments, PR author, receipt digests, distinct identities, and ancestry.
- Owner and peer bodies now use one shared strict normalization helper: CRLF/CR become LF; zero or one final LF is
  accepted, while extra final LFs, trailing spaces, blank lines, and all other changes fail. Hashing uses the canonical
  marker with exactly one final LF, resolving the live-receipt compatibility boundary without weakening identity checks.
- State remains **Blocked**. No resume invocation, GitHub write, lifecycle invocation, rereview, or final gate is claimed.

## Live two-peer resume

- `resume` succeeded at start head `5ab1b19027339aabfd8175317b31e0afe5e9cc3d`, using authorization head
  `07319b35ad2d7c1d2ee12f6c1438ad0e13e7afde` and exact peer comments `5713964023` / `5714397001`.
- Canonical resume state was committed as `e1aa578`. Post-resume focused verification passed **28/28**, with **1
  skipped**; validation reported **50 valid work items** and projection reported **current**.
- Review dispositions: **F1 Fixed** — authenticated owner/two-peer authorization routes and strict receipt validation;
  **F2 Fixed** — authenticated attribution boundary; **F3 Fixed** — strict paginated review observation; **F4 Fixed** —
  human reviewer identity enforcement.
- No rereview or final gate was run or claimed.

## Rereview finding repairs (F8-F10)

- F8: **Fixed** — removed runtime test seams and legacy merge compatibility. Authenticated PR observation now requires
  all seven production fields plus repository, PR number, and metadata validation; closeout requires literal `MERGED`,
  exact final head, merge object/SHA, and authenticated review. Tests retain production-shaped mocks.
- F9: **Fixed** — canonical GitHub issue identity binds source URL, supplied repository, item number, receipt URL, and
  PR repository/number case-insensitively for owner/repo and exactly for issue number, before observation or writes. The
  same binding helper is used by the peer route, including cross-repository same-number rejection coverage.
- F10: **Fixed** — project validates that comments are present as a list of dictionaries with string bodies before
  computing actions; malformed or missing comments abort before any write subprocess, while empty and unrelated valid
  lists remain actionable.
- These are repair dispositions only; no rereview or final gate is claimed.
