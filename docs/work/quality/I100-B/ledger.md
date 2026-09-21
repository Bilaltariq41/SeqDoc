# I100-B planning decision ledger

This is a planning-only ledger. No tests, focused command, final gate, implementation, GitHub operation, commit, push,
or activation was run.

## Research sources

- Git worktree identity and removal: https://git-scm.com/docs/git-worktree
- Restart Manager admission/session: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmstartsession
- Restart Manager resource registration: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmregisterresources
- Restart Manager process listing: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmgetlist
- Restart Manager session end: https://learn.microsoft.com/windows/win32/api/restartmanager/nf-restartmanager-rmendsession
- Local accepted #106 public API boundary and cleanup pattern findings: `I100-A/checkpoint.md` and `I100-A/ledger.md`,
  including public `ContainedProcess` observations, active-zero proof, teardown evidence, and ordered secondary evidence.
- Reusable QHTTP/GH93 patterns were inspected as read-only risk input; neither contract supplies a sentinel, quarantine,
  or complete Restart Manager cleanup contract.
- Readiness and authority: Issue #107, owner authorization
  https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5696378047; parent split
  https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5681943603; publication
  https://github.com/Bilaltariq41/SeqDoc/issues/100#issuecomment-5694678193.

## Decisions

| Decision | Frozen resolution |
|---|---|
| Ownership | Immutable receipt plus versioned sentinel is the sole destructive authority; all paths are canonical, non-reparse, listed descendants. |
| Process boundary | #106 public observations only; cleanup must prove `ACTIVE_PROCESS_ZERO` before any deletion, RM diagnostics, or quarantine. |
| Git boundary | Exact registration and captured admin path are removed through `git worktree remove --force`; no prune or manual admin deletion. |
| Diagnostics | RM is bounded attribution evidence only, never ownership or termination authority; stable receipts exclude raw paths and timestamps. |
| Failure | Existing primary failure wins; cleanup evidence is chronological and degradation cannot become success. |
| Lifecycle | Revalidate before every deletion attempt, use the fixed eight-attempt schedule, and quarantine only after all gates. |
| Concurrency | Serialize only same-common-dir metadata mutations; independent fixture roots and unrelated repository snapshots remain isolated. |
| Scope | Future implementation is limited to the declared three test paths and governance/generated state paths. |

## F1-F6 readiness dispositions

| Finding | Disposition |
|---|---|
| F1 — family-proof bound and cancellation | Fixed in the planning contract: finite `CleanupTimeout`, fixed 10-second `FamilyProofGrace`, 40-second default outer deadline, reserved grace before #106 `WaitAsync`, mandatory cleanup despite caller cancellation, overrun/finalization rules, and nested two-second deletion sub-budget are frozen. |
| F2 — Git residual safety | Fixed: only receipt-listed control-root descendants may be directly deleted; captured common-dir admin data is preserved on residual or quarantine paths, and success requires both admin and registration absence. |
| F3 — exact sentinel, receipt, and partial identity | Fixed: schema-v1 sentinel, cryptographic token, FILE_ID_INFO identities, reparse-safe revalidation, monotonic owner stages, and reconstructed-owner fail-closed behavior are frozen. |
| F4 — Restart Manager managed interop admission/state machine | Fixed: exact Unicode P/Invoke attributes, constants, layouts, signatures, 3-call count semantics, real admitted-platform empty/known-lock call, injected negatives, mandatory session end, and no shutdown/ownership inference are frozen. |
| F5 — claims and reviewer | Fixed: Ready record has no claims; the exact canonical lowercase activation set is frozen in the capsule/ledger and acquired only by activation. Qhatahet is reserved as untouched non-author peer, with replacement governed by current evidence policy and no Ready review metadata. |
| F6 — lifecycle truth | Fixed: GH-107 is transactionally prepared and validated, Ready, unselected, and execution remains idle. |

## Readiness and lifecycle

Readiness claims are frozen but intentionally unleased. Activation must supply exactly these canonical lowercase claims:
`path:tests/seqdoc.acceptancetests/fixturecleanup.cs`,
`path:tests/seqdoc.acceptancetests/fixturecleanuptests.cs`,
`path:tests/seqdoc.acceptancetests/seqdoc.acceptancetests.csproj`,
`path:docs/work/quality/i100-b`, `path:docs/project/work-items/gh-107.json`,
`fixture:fixturecleanup`, `governance-tool:tools/governance/work_state.py`, and
`exclusive:acceptance-git-worktree-metadata`. This exact set is acquired only by activation to avoid preemptive lease
blocking; no selection occurs now.

GH-106 is the closed dependency at the supplied accepted head and closeout baseline. GH-107 was transactionally
prepared and validated, is `Ready`, is not selected or activated, and execution remains idle. The readiness audit passed
after the complete governance validation set: `prepare`, work-state validation, execution projection check, and
`git diff --check`. No product/test/build verification, GitHub operation, commit, push, or activation was performed.
