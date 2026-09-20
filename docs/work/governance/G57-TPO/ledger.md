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

## Rereview finding repair (F11)

- F11: **Fixed** — canonical binding validates source repository and issue number, and PR repository independently;
  positive PR numbers are not required to equal issue numbers. Authenticated PR observation separately validates its
  observed number against the PR URL, while all repository, receipt, and fail-closed checks remain intact.
- This is a repair disposition only; no rereview or final gate is claimed.

## F12 receipt revalidation repair

- F12: **Fixed** — fresh exact peer receipts `5715444371` and `5716001245` are bound to blocked head `d3453f0`.
  Authenticated resume at `d3453f0` was committed as `c4c7e7c`.
- F12 adds realistic `GH-57`/`G57-TPO`/`GH-57:G57-TPO`, Issue #57/PR #110 successful two-peer closeout
  revalidation, plus edited/deleted receipt complete byte-snapshot rejection coverage.
- No production code changed. Focused verification: **28 passed, 1 platform skip**.

## Current-policy reconciliation (phase A)

- The owner/two-peer takeover protocol, fixed repair-limit requirement, and closeout takeover revalidation are
  **superseded policy**. The current authority is `docs/project/collaboration-model.md`, lines 57-64; the historical
  receipts and observations above remain evidence only.
- Worker-owned T2/T3 continuation removes takeover authorization parsing and CLI arguments. Resume is transactional,
  GitHub-free, dependency-aware, claim-conflict-aware, and records only deterministic `resume` evidence. It consumes
  legacy takeover metadata rather than preserving it.
- Phase A retains a narrow validation/schema allowance for the existing `Blocked` legacy record so live GH-57 state can
  validate. Phase B deletes that allowance after migration. No lifecycle invocation, resume, final gate, or GitHub write
  is claimed by this repair.
- This is a repair disposition only; no independent rereview or final gate is claimed.

## Phase-B migration completion

- The historical owner/two-peer authorization and takeover entries above are retained as evidence only and are
  explicitly **superseded** by current collaboration policy.
- Live GH-57 migration completed at `f8919e2`: the current record contains `resume` and no `takeover`. The obsolete
  schema allowance and runtime validation machinery are now deleted; worker-owned resume is final.
- F13 identity repair remains **Fixed**. No lifecycle invocation, handoff, commit, push, or final gate is claimed.

## F13 identity repair disposition

- F13: **Fixed** — review metadata now binds `reviewPeer` to `review.peer` case-insensitively, while `reviewEpoch` and
  `reviewFindings` remain exact mirrors; review author/peer distinctness and closeout caller-peer matching use GitHub
  login casefolding without broadening non-login IDs. Authenticated attribution continues to persist the exact observed
  spelling.
- This is a repair disposition only; no rereview, lifecycle invocation, resume, or final gate is claimed.

## Final Phase-B corrections

- Handoff now persists a supplied nonempty `next_action` verbatim and uses the deterministic default
  `Obtain one latest-head non-author peer review.` when omitted; empty values are rejected.
- Resume documentation no longer claims legacy metadata removal. The live-migrated checkpoint is `ResolvingFindings`
  pending verification and handoff; no work-item or execution-state edit is claimed here.

## Complete-candidate F14/F15 repairs

- F14: **Fixed** — handoff and closeout validate canonical source issue, item number, PR repository, and supplied
  repository before any GitHub observation or write; issue and PR numbers remain independent.
- F15: **Fixed** — persisted claim objects must equal their normalized records, while caller-supplied claims retain
  normalization and duplicate-after-normalization rejection.
- These are complete-candidate repairs only; no lifecycle invocation, handoff, commit, push, or final gate is claimed.

## Abood complete-candidate review — F18-F24

- Reviewer: Abood-essa; review locus: https://github.com/Bilaltariq41/SeqDoc/pull/110
- F18: **Fixed** — every filesystem-mutating public operation takes one repository-scoped blocking Windows/POSIX lock
  from load through validation, observation, journal, replacement, rollback, or recovery completion.
- F19: **Fixed** — deterministic staged preimages, current-hash validation, confined journal targets, `os.replace`
  restoration, and retained journal/staging evidence make interrupted recovery retryable and idempotent.
- F20: **Fixed** — scaffold mutations and validation/projection payloads derive from one deep-copied candidate, including
  self-consistent active capsules.
- F21: **Fixed** — persisted and supplied path-like claims reject existing symlink/junction/reparse components while
  preserving nonexistent ordinary paths and abstract exclusive resources.
- F22: **Fixed** — projection writes missing labels, issue edits, and marker comments in deterministic phases; failures
  stop later phases and marker retries remain idempotent.
- F23: **Fixed** — handoff requires an exact lowercase 40-hex head before any GitHub subprocess.
- F24: **Fixed** — the accepted T2 amendment records the seven additional governance paths, rationale, risks, coverage,
  and the rule that execution/GH-57 state are operation-generated rather than hand-edited.
- Focused verification after implementation: **28 passed, 2 capability-gated symlink skips**. No lifecycle invocation,
  GitHub write, final gate, commit, push, or handoff is claimed.

## F18/F19 hardening repair

- Stage metadata now persists only canonical repository-relative paths. Recovery confines each stage beside its target,
  requires the exact target/original prefix, rejects absolute, escaped, symlink, reparse, and external-sentinel paths,
  and relies on embedded preimage bytes and hashes for restoration authority.
- The repository lock is an exactly one-byte `O_CREAT|O_RDWR` file. Windows acquisition loops on contention while
  preserving unexpected errors; POSIX `fcntl` locking and finally-release behavior remain unchanged.
- Journal writes, including staged metadata and interrupted status, use an fsyncing helper. Present originals restore by
  staged rename; absent originals retain the documented validated direct-unlink boundary. Focused verification now
  passes **28/28 with 1 actual symlink-capability skip**.

## Inspected lock-release correction

- **Fixed** — `repository_lock` now tracks successful acquisition and unlocks only after acquisition. Initialization or
  unexpected acquisition errors close the descriptor without an unlock attempt, preserving the original failure while
  retaining blocking contention and finally-release semantics.

## Reviewer F25/F26

- F25: **Fixed** — `execution` now takes the repository lock across load, validation, comparison, and projection write
  for both check and write modes; `execution_payload` remains pure and concurrent projection tests preserve coherence.
- F26: **Fixed** — existing stage files are validated as regular non-reparse files with exact role bytes and hashes before
  restoration or cleanup. Absolute/escaped/external stages, wrong-content adjacent stages, and legacy hashless stage
  metadata fail closed without mutation; missing stages remain valid when a prior rename consumed them.
- Focused verification after these repairs: **28 passed, 1 actual symlink-capability skip**. No lifecycle invocation,
  commit, push, GitHub operation, or final gate is claimed.

## Reviewer F27

- F27: **Fixed** — one checked write-all helper now handles every binary descriptor write, and centralized stage creation
  closes descriptors, fsyncs complete contents, and unlinks failed temporary stages. Short/zero progress cannot expose
  an incomplete replaceable stage; journal failure leaves no false success or canonical mutation.
- Focused verification: **28 passed, 1 actual symlink-capability skip**. No lifecycle invocation, commit, push, GitHub
  operation, or final gate is claimed.

## Reviewer F28

- F28: **Fixed** — rollback now promotes each pre-staged original directly with one reversed-order `os.replace`, never
  rewrites a stage. Consumed and missing stages are cleanup-safe; a failed rollback rename preserves the journal and
  remaining evidence for successful recovery.

## Reviewer F29

- F29: **Fixed** — removed the `rollback_success` parameter and all caller special cases. Immediate rollback always
  re-raises the original canonical replacement error; subsequent retry remains successful after byte-identical cleanup.
- The compatible concurrent activation diagnostic was captured with each worker's return code, stdout, and stderr. Both
  workers use explicit independent IDs, executions, branches, worktrees, and claims, synchronize on both ready files and
  one shared start signal, and now persist successfully under the repository lock. No production race was observed.

## Reviewer F16

- F16: **Fixed** — handoff now has direct cross-repository, malformed or missing source URL, and mismatched
  issue-number negatives. Each proves canonical identity rejection before subprocess observation and preserves
  byte-identical registry, capsule, and execution projection state.
- F16 changed the supplied tests. The exact focused command at test-changing head
  `76551dc3c36f99d5225b46b3d6240b2af35f1c22` ran 28 tests in 14.297s and passed, with one symlink-capability skip.

## Reviewer F17

- F17: **Fixed** — corrected the F16 verification record. This subsequent repair changes durable evidence text only;
  it does not change source, tests, or lifecycle state.
