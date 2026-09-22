# I100-B1 planning decision ledger

Planning-only. Issues [#116](https://github.com/Bilaltariq41/SeqDoc/issues/116), [#117](https://github.com/Bilaltariq41/SeqDoc/issues/117), [#118](https://github.com/Bilaltariq41/SeqDoc/issues/118), parent correction [comment](https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5774557010), Abood's #116 findings [comment](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600), and reviewer decision context predate this package. No product/test command, activation, implementation, commit, or push occurred in package preparation.

## Sources and decisions

- GH-116 body and Abood findings: https://github.com/Bilaltariq41/SeqDoc/issues/116 and https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600.
- Parent correction: https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5774557010.
- GH-107/I100-B, accepted GH-106/I100-A, collaboration-model lines 57–60, issue-readiness, testing-policy, and project-operations were read at baseline `18aae5e0364c0b13549bd0c5333ba48f8116bc9a`.

| Abood finding | Planned disposition | First proof |
|---|---|---|
| F1 — split provenance | Preserve the parent correction and Abood's actual split provenance; B1 is the authority/Git boundary, not a relabelled technical finding. | Capsule authority and peer receipt |
| F2 — immutable package | Freeze this exact authored nine-file package at one commit SHA; both peers review that same SHA. | Package SHA receipt |
| F3 — common-directory identity | Enforce component-by-component non-reparse volume/file identity and exact common-dir/admin authority before each mutation. | Authority tests 1–2 |
| F4 — final gate | Require the exact 79/0/0 affected command after review, with platform/Git identity evidence and no RM requirement. | Final receipt |
| F5 — authority, scope, and review | Keep exact two implementation paths, excluded csproj, activation claims, sequential leases, Qhatahet route, and dual-peer T2 disposition. | Capsule and review receipts |

## T2 disposition

Repair is proposed only. Both non-author peers Qhatahet and Abood-essa must review one immutable candidate SHA and provide matching decision receipts. No GH-116 promotion, Ready transition, or execution selection is allowed before both receipts and a complete rereview are recorded.

Frozen Git vectors: `worktree add --detach <owned-absolute-path> <40-lowercase-revision>`; `rev-parse --git-common-dir`; `rev-parse --absolute-git-dir`; `status --porcelain=v1 -z --untracked-files=all`; `for-each-ref --format=%(refname)%00%(objectname)%00%(symref)%00 --sort=refname`; `config --local --null --list`; `worktree list --porcelain`; `worktree remove --force <owned-absolute-path>`. Capture and compare canonical common-dir, source root, and direct-child admin FILE_ID_INFO identities; never manually remove admin data or prune.

Phase A receipts are split-only: both Qhatahet and Abood-essa review the same immutable planning SHA and post authenticated T2 dispositions; they authorize only split/spec/allowlists, not implementation review or a final gate. Phase B requires implementer self-review, Reviewer agent, dispositions, focused green, `ReviewRequired`, then one latest-head non-author human approval for the implementation SHA (B1 Qhatahet; replacement only by policy evidence), then the final gate. Phase B cannot amend Phase A. Before Ready, B2/B3 mechanically amend exact accepted predecessor merge/head and claims and rerun readiness audit; this is assigned issue readiness approval, not a new split decision absent scope/contract change.
