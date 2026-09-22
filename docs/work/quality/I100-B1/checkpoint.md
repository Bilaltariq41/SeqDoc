# I100-B1 fixture authority and exact Git cleanup

## State

`Blocked`

This package becomes immutable only at its commit SHA. It is planning-only at baseline
`18aae5e0364c0b13549bd0c5333ba48f8116bc9a`; it does not select, activate, or authorize an owner bypass. Issue [#116](https://github.com/Bilaltariq41/SeqDoc/issues/116), the GH107/I100-B split, the parent correction [comment](https://github.com/Bilaltariq41/SeqDoc/issues/107#issuecomment-5774557010), Abood's findings [comment](https://github.com/Bilaltariq41/SeqDoc/issues/116#issuecomment-5774337600), `docs/project/collaboration-model.md` lines 57–60, and `docs/project/issue-readiness.md` are the decision sources. Promotion requires both non-author peers Qhatahet and Abood-essa to decide the same immutable SHA; their receipts are still pending.

## Authored/generated boundaries and handoff

Authored package paths are this checkpoint, ledger, and `docs/project/work-items/GH-116.json`; implementation paths are `tests/SeqDoc.AcceptanceTests/FixtureCleanup.cs` and `tests/SeqDoc.AcceptanceTests/FixtureCleanupTests.cs`. Generated execution projection is `docs/project/execution.json`, expected unchanged and idle. `SeqDoc.AcceptanceTests.csproj` is excluded. Current-main planning baseline is exactly `18aae5e0364c0b13549bd0c5333ba48f8116bc9a`; no current-main test or acceptance result is claimed.

Prospective activation claims, acquired only on activation after GH-106, review, and the predecessor conditions are satisfied:
`path:tests/seqdoc.acceptancetests/fixturecleanup.cs`, `path:tests/seqdoc.acceptancetests/fixturecleanuptests.cs`, `path:docs/work/quality/i100-b1`, `path:docs/project/work-items/gh-116.json`, `fixture:fixturecleanup-authority`, `governance-tool:tools/governance/work_state.py`, `exclusive:acceptance-git-worktree-metadata`. No csproj claim. Successor claims require predecessor claims released/closed.

## Objective and dependency

Establish fixture authority, rooted Git admission, exact sentinel/receipt identity, safe worktree administration, and byte-stable repository cleanup while consuming accepted GH-106 only. Dependency: GH-106 at public ProcessOwnership head `182ea35533482cebdfc070b368f3a7fa247a1735`, merge `227d2e9f8b49ce6a414795b16bb0408ed213012a`. No command, test, activation, GitHub operation, commit, or push is part of planning.

## Target paths and non-goals

Initial implementation allowlist is exactly `tests/SeqDoc.AcceptanceTests/FixtureCleanup.cs` and `tests/SeqDoc.AcceptanceTests/FixtureCleanupTests.cs`, plus this capsule, its ledger, and generated canonical records/projection through `work_state.py`. `SeqDoc.AcceptanceTests.csproj` is not initially allowed; a new peer-approved amendment may add it only after a concrete compiler/build dependency proves SDK wildcard inclusion and the current ProcessOwnership stub staging insufficient. No package, project, stub, product, external corpus, or workflow changes; no PATH/cwd Git search, prune, manual admin deletion, global kill, or cross-platform claim.

## Frozen contract and risks

* The test admission helper may resolve only `%ProgramFiles%\Git\cmd\git.exe` and `%ProgramFiles(x86)%\Git\cmd\git.exe`; zero candidates fails, multiple distinct FILE_ID candidates fails. Production receives one canonical absolute existing non-reparse regular file with captured Windows volume and file ID; it never searches PATH/cwd.
* Before worktree creation and every metadata mutation, capture and revalidate the source common Git directory and source root. Every component is non-reparse and carries volume serial plus 128-bit FILE_ID_INFO identity. Mutations use exact common identity; a digest is not authority. The specific admin is a direct child of `<common>\worktrees`, same volume and identity.
* The sentinel is exact UTF-8 without BOM, one JSON line plus LF, with property order `schemaVersion`, `token`, `revision`, `commonDirectoryDigest`, `roles`; token is 32 random bytes base64url without padding, revision is 40 lowercase hex, digest is local `sha256:` plus 64 lowercase hex, and sorted roles are exactly `cache`, `output`, `quarantine`, `worktree` with ASCII relative values. Unknown/missing/duplicate properties, escapes, absolute roles, dot segments, alternate bytes, and role escape fail closed.
* Local paths are drive-local, full, separator-trimmed except volume roots, OrdinalIgnoreCase with separator-boundary containment; device, UNC, alternate-stream, reparse, replacement, and cross-volume forms fail. Role children are direct under the control root. The local authority receipt contains token, canonical paths, exact sentinel bytes/hash, common/source/control/sentinel/role/worktree/specific-admin FILE_ID identities, registration, and revision; it is never persisted or emitted.
* Git uses only accepted #106 `ContainedProcess`: exact vector `worktree add --detach <owned-absolute-path> <40-lowercase-revision>`; rooted paths cannot begin option syntax. Capture `rev-parse --absolute-git-dir`. Parse exact porcelain registration, reject malformed/duplicate rows, require owned registration and admin absence after removal, preserve any residual admin, and never infer or delete it manually.
* No mutation/deletion/diagnostic begins until every Git process is disposed and `HasObservedActiveProcessZero` has accepted teardown evidence. Immediately before create and after cleanup, compare exact bytes of `status --porcelain=v1 -z --untracked-files=all`, `for-each-ref --format=%(refname)%00%(objectname)%00%(symref)%00 --sort=refname`, `config --local --null --list`, and `worktree list --porcelain`, removing only the owned registration vector. Also freeze `rev-parse --git-common-dir`, `rev-parse --absolute-git-dir`, `worktree remove --force <owned-absolute-path>`, and the exact canonical common-dir/admin identity checks.
* Stable evidence contains only schema, stage, role, attempt, classification, count, and certainty, sorted chronologically; it excludes token, paths/digests, PID/FILETIME, wall time, checkout data, and credentials.

Risks are authority confusion, sentinel ambiguity, reparse/identity replacement, Git-admin damage, registration residuals, active-process races, unrelated repository mutation, false capability skips, and unstable evidence. Existing GH-106 has 76/76 ProcessOwnership coverage; I100-B has no complete sentinel/Git-authority contract coverage.

## Tests and budget

Add exactly three `[Fact]` methods in `FixtureCleanupAuthorityTests`: (1) rooted Windows x64/Git capability admission and exact sentinel/receipt/identity/containment/reparse/negative partitions; (2) real disposable-repo registration/admin capture and exact successful cleanup, including malformed/duplicate/owned residual registrations; (3) active-family gating, lifecycle disposal, and byte-identical unrelated vectors before/after. The focused filter must discover exactly 3: `3 passed/0 failed/0 skipped`; capability absence is failure, never skip. This is the minimum reliable contract layer; the live integration belongs to B3. No budget exception.

## Verification and review

Focused command (not run while planning):
```powershell
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter FullyQualifiedName~FixtureCleanupAuthorityTests
```
Final post-review gate, once only: `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~FixtureCleanupAuthorityTests|FullyQualifiedName~ProcessOwnershipTests"`, exactly `79 passed/0 failed/0 skipped` (accepted GH-106 count 76), with SDK, OS architecture/version, rooted Git identity classification, `RM capability not required for B1`, and SHA in the receipt. Worker self-review precedes one complete-candidate Reviewer; findings are Fixed, Rejected with evidence, or Deferred only by the required dual-peer decision. Latest-head human reviewer is reserved to Qhatahet; Abood replacement needs policy evidence. Green focused verification moves the lifecycle to ReviewRequired; no unchanged-SHA retry.

Preparation recorded the pre-existing Issues #116–#118 and parent/reviewer comments; it did not create them. No product/test command, activation, implementation, commit, or push occurred in package preparation. Both peers must review this same package commit SHA; no Ready now.

Phase A is the split decision: both Qhatahet and Abood-essa review this immutable planning SHA and post authenticated T2 receipts; that authorizes only split/spec/allowlists, not implementation review or a final gate. Phase B is per-issue: implementer self-review, Reviewer agent, dispositions, focused green, `ReviewRequired`, then one latest-head non-author human approval for the implementation SHA (B1 Qhatahet; replacement only by policy evidence), followed by the final gate. Phase B cannot amend Phase A. Before Ready, B2/B3 mechanically amend the exact accepted predecessor merge/head and claims and rerun readiness audit; this needs assigned issue readiness approval, not a new split decision unless scope/contract changes.

## Stop conditions and peer decision

Stop and remain Blocked for any missing identity, malformed sentinel, unsafe Git syntax, residual admin, active family, byte-vector drift, capability absence, allowlist expansion, or focused failure. The T2 repair disposition is proposed, not authorized: Qhatahet **and** Abood-essa must independently decide the same immutable candidate SHA before GH-116 may be promoted or amended toward Ready. Shared paths with B2/B3 require strict sequential leases, never parallel or stacked implementation.
