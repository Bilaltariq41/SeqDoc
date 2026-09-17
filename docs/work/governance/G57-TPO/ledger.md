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
