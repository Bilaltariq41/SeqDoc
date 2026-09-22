# I100-B2 planning decision ledger

Planning-only. Issue [#117](https://github.com/Bilaltariq41/SeqDoc/issues/117) and its Abood findings, plus parent/reviewer context [#107 comment](https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5774557010) and [#116 findings](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600), predate this package. No product/test command, activation, implementation, commit, or push occurred in package preparation.

| Finding/risk | Planned disposition | Proof |
|---|---|---|
| F1 — split provenance ([#117](https://github.com/Bilaltariq41/SeqDoc/issues/117)) | Preserve the process/RM child boundary and its Abood decision provenance; no owner promotion is inferred. | Capsule authority |
| F2 — immutable package ([#117](https://github.com/Bilaltariq41/SeqDoc/issues/117)) | Freeze this package at one SHA; both peers review that SHA, with no Ready now. | Package SHA receipt |
| F3 — common-dir identity ([#117](https://github.com/Bilaltariq41/SeqDoc/issues/117)) | Inherit B1 exact common/admin FILE_ID and vector authority; no process/RM action may weaken it. | Process tests 1–3 |
| F4 — final gate ([#117](https://github.com/Bilaltariq41/SeqDoc/issues/117)) | Require exact 82/0/0 quoted command after review and record capability evidence. | Final receipt |
| F5 — authority/scope/review ([#117](https://github.com/Bilaltariq41/SeqDoc/issues/117)) | Keep two implementation paths, excluded csproj, exact activation claims, sequential predecessor handoff, and reserved Abood-essa review. | Capsule and review receipts |

Sources: GH-117 body, GH-107/I100-B, GH-106/I100-A, `collaboration-model.md` lines 57–60, `issue-readiness.md`, `testing-policy.md`, and `project-operations.md`.

Frozen inherited Git vectors: `worktree add --detach <owned-absolute-path> <40-lowercase-revision>`; `rev-parse --git-common-dir`; `rev-parse --absolute-git-dir`; `status --porcelain=v1 -z --untracked-files=all`; `for-each-ref --format=%(refname)%00%(objectname)%00%(symref)%00 --sort=refname`; `config --local --null --list`; `worktree list --porcelain`; `worktree remove --force <owned-absolute-path>`. B2 adds the exact RM signatures and state machine in its capsule.

F1 retry proof is exact: starts `[0,50,150,350,750,1150,1550,1950]` ms from retry-start, delays `[50,100,200,400,400,400,400]` ms, maximum eight attempts, nested 2-second monotonic deletion deadline plus remaining outer deadline. Test 2 asserts count, offsets, delays, no inadmissible start, full authority revalidation before each attempt and after every awaited delay, and immediate success/nonretryable/invalid-authority/deadline stops.

Phase A receipts are split-only: both Qhatahet and Abood-essa review the same immutable planning SHA and post authenticated T2 dispositions; they authorize only split/spec/allowlists, not implementation review or a final gate. Phase B requires implementer self-review, Reviewer agent, dispositions, focused green, `ReviewRequired`, then one latest-head non-author human approval for the implementation SHA (B2 Abood; replacement only by policy evidence), then the final gate. Phase B cannot amend Phase A. Before Ready, amend the exact accepted GH-116 merge/head and claims and rerun readiness audit; this is assigned issue readiness approval, not a new split decision absent scope/contract change.
