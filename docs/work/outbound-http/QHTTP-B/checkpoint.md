# FraudManagement direct outbound HTTP acceptance checkpoint

## State

`ReviewRequired`

GitHub Issue #53. The frozen implementation baseline is merged PR #59 at
`0b8e4b7a91cf52e4a98542bcc307f9262414efdf`.

Work started 2026-09-03 on contributor branch `acceptance/issue-53-outbound-http`, forked from upstream `main` at `08cb735` (issue #54 / PR #59 semantics already merged).

A `test-writer-medium` pass landed the sole writable acceptance test `tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` and evidence note `test-writer-notes.md`; its focused lane was 4/4 green. One independent `reviewer-medium` pass returned four Major and three lesser findings (no blocking defect); all were resolved on-branch in one repair round with no production change.

Final gate passed once: `dotnet build SeqDoc.slnx -c Release` = 0 warnings / 0 errors, then `dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --no-build --filter \"FullyQualifiedName~OutboundHttpExternalCorpusTests\"` = 4 passed / 0 failed / 0 skipped. Awaiting owner decision on push / PR.

Owner review 2026-09-03 held the candidate before maintainer review: the issue #53 "Required candidate artifact matrix" was only partly recorded (missing per-artifact byte/length/SHA-256 for both runs, ordered-record digest, manifest content-hash cross-check, SDK/Mermaid-CLI versions), the `certainty` assertion was too loose, doc comments falsely described a `git worktree`, and the owner rejected the in-place-pinned-checkout deviation. A second repair pass is reworking corpus isolation to a real detached `git worktree` (sparse checkout excluding committed build output, `core.autocrlf=false`, fingerprint-faithfulness proof) and closing the matrix gaps, all inside the test-file + evidence allowlist.

BLOCKED 2026-09-03: the worktree rework is not achievable without re-baselining issue #53's frozen contract. The frozen `indexFingerprint` `f9a36fd5662f01eead94779eb243f489d5bc1c6e1b7333d2f987b76e30d8146c` is only reproducible from the shared in-place `Provided/FraudManagement` checkout, whose working tree has an inconsistent mix of LF and CRLF line endings (verified: `BLL/Logging.cs`, `BLL/DTOs/EligibleForTerminationDTO.cs`, several `FraudManagementWindowsService/Timers/*.cs` are LF in git but CRLF on disk, while sibling files in the same directory are LF on disk). Git stores every one as LF, so no clean checkout or single `core.autocrlf`/`core.eol` setting reproduces the mix. A normalised detached worktree (`core.autocrlf=false`, `core.eol=lf`, sparse checkout excluding committed build output) builds and analyses cleanly and deterministically but yields `indexFingerprint` `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`. A prior `core.autocrlf=true` attempt yielded a third value (`464030ca…`). No production change is implicated. Awaiting owner decision: (1) re-baseline issue #53's frozen fingerprint + artifact hashes against a normalised worktree, (2) renormalise the shared corpus checkout then re-baseline once, (3) escalate to the upstream maintainer who owns issue #53, or (4) accept the in-place-pinned-checkout approach. The prior repair pass (F1–F7) remains applied and the test file is otherwise unchanged from that green state.

Unblocked 2026-09-03 by owner decision: keep the in-place-pinned-checkout approach and drop the `git worktree` requirement (the frozen fingerprint is inseparable from the shared checkout's line-ending state; a normalised worktree is deterministic but produces `df23b372…`). A third repair pass keeps the in-place fixture and applies the remaining owner-review items inside the test-file + evidence allowlist: full run-1/run-2 candidate artifact matrix (per-artifact byte length + SHA-256 + equality, complete-output digest), diagnostics ordered-record digest, manifest content-hash cross-check, `certainty: Exact` pinned, and doc comments corrected to describe the in-place mechanism. The fingerprint fragility is recorded as a known boundary for the PR body and maintainer follow-up.

Third repair pass applied and verified 2026-09-03 (test file + `test-writer-notes.md` only): full run-1/run-2 candidate artifact matrix with per-artifact byte length + SHA-256 + equality, complete-output digest `8fce2d7e…`, diagnostics ordered-record digest `2f62d6e3…`, manifest content-hash cross-check (35 entries, listed set == generated non-operational set), `certainty: Exact` pinned, doc comments corrected to the in-place mechanism. Focused lane 4/4 green; final gate `dotnet build SeqDoc.slnx -c Release` 0/0 then filtered AcceptanceTests 4/4 green. SDK 10.0.302, node v24.20.0, mermaid-cli 11.16.0; 17 links resolve; 17 Mermaid within budget 45000 (largest 434) and CLI-rendered exit 0. Profile ID, Program Index fingerprint, and frozen blob hashes all match issue #53; in-place `git status` (scoped) empty before/after; no corpus or temp delta. Awaiting owner decision on commit / push / PR. Deviations to disclose in the PR: (a) in-place checkout instead of `git worktree` (owner-accepted; frozen fingerprint tied to the shared checkout's LF/CRLF mix, normalised worktree yields `df23b372…`); (b) `docs/work/outbound-http/QHTTP-B/` evidence paths authorized by the owner's 'QHTTP-B ready' issue comment rather than the issue body's target-path list; (c) environment prerequisites (corpus NuGet-restored for net9.0, node/npx for Mermaid CLI) which fail loud, never skip, per issue #53.

Second independent review (reviewer-medium) of the final candidate, 2026-09-03: verdict = acceptable to open as a maintainer PR with specific fixes first; NOT merge-ready as an acceptance lock until the upstream maintainer who owns issue #53 decides the frozen-`indexFingerprint` reproducibility question (the frozen `f9a36fd5…` is reproducible only from the contributor's in-place checkout; a clean/normalised worktree deterministically yields `df23b372…`; the repo has no .NET CI, so the issue's 'lane required before merge' is a maintainer-run local gate that would fail on a clean-cloned corpus). One Major finding (bare fingerprint assertion gives a misleading 'semantic regression' failure) and several Minor/Observation findings. A fourth repair pass is applying them inside the test-file + evidence allowlist: actionable fingerprint-mismatch message pointing at the known boundary, stale MAX_PATH comment corrected, complete (unscoped, tracked-only) external git status also recorded, `SEQHTTP001`-absence checks pointed at the diagnostic stream, `seqdoc.stale` added to the operational-path filter, run-2 identity captured + cross-run equality asserted, Mermaid-budget JSON path asserted present, and the boundary-wording denylist extended to every generated flow that contains an HTTP boundary phrase. No production change. After this pass the candidate is review-ready; the fingerprint decision is escalated to the maintainer in the PR body, not buried.

Fourth repair pass applied and verified 2026-09-03 (test file + notes only, no production change): F1 (actionable fingerprint/profile mismatch message naming the LF/CRLF cause + the normalised value `df23b372…` + the issue #53 owner decision + notes pointer), F2 (stale MAX_PATH comment corrected), F3 (complete unscoped tracked `git status` also recorded, scoped gate unchanged), F4 (`SEQHTTP001`-absence checks re-pointed at the diagnostic stream), F5 (`operationalPaths` = non-`.md`/`.mmd`), F7 (run-2 identity captured, run1==run2 asserted), F10 (Mermaid-budget JSON path presence hard-asserted), F11 (boundary-wording denylist applied to every generated flow carrying an HTTP boundary phrase). F6/F8/F9 acknowledged unchanged. Still 4 tests. Focused lane 4/4 green; final gate `dotnet build SeqDoc.slnx -c Release` 0/0 then filtered AcceptanceTests 4/4 green. Matrix values unchanged from the third pass. Candidate is review-ready. Remaining gate to merge-ready is the upstream maintainer's decision on the non-reproducible frozen `indexFingerprint` — outside the contributor allowlist. Awaiting owner instruction on opening the PR.

Submitted for maintainer review 2026-09-03 as PR https://github.com/Bilaltariq41/SeqDoc/pull/67 (ready-for-review, not draft; base `main`, head `Qhatahet:acceptance/issue-53-outbound-http`, commit `75ac05e`; 3 files, +1648/-1; `Closes #53`). The PR body carries the blocking maintainer question (frozen `indexFingerprint` not reproducible from a clean checkout; decide re-baseline / renormalise-corpus / accept-in-place), the full candidate artifact matrix, the `docs/work/**` path authorization (the owner's 'QHTTP-B ready' comment), and the environment prerequisites. Awaiting maintainer review; no further contributor implementation until findings return.

Authoritative maintainer review received 2026-09-03: PR #67 got a GitHub "Request Changes" review from the owner (Bilaltariq41) at `f602665`, plus a frozen-contract amendment (issue #53 comment 5523887517) that supersedes the in-place mixed-line-ending fingerprint. Per the amendment the acceptance input becomes an isolated normalized checkout of revision `7aabfef98fa4d47781bd8a98b9061ddcafb88836` (`core.autocrlf=false`, `core.eol=lf`), the amended Program Index fingerprint is `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`, and the profile / two roots / revision / committed-blob hashes are unchanged. This GitHub review is recorded as the single authoritative complete-candidate review; the earlier contributor-invoked `reviewer-medium` passes are reclassified as pre-submission advisory / self-review evidence. One batched contributor repair round is in progress covering findings F1-F7: reproducible normalized checkout plus complete ordered `data.runs[]` identity (F1); fail-closed source isolation and single-owner cleanup with `git worktree list` and shared-repo status equality (F2); no skip on missing prerequisites for this required lane (F3); in-memory sensitive-value scan and per-root direct single-representation assertions (F4); fail-closed CLI identity/version evidence with stderr capture (F5); the frozen final gate is the complete AcceptanceTests Release suite, unfiltered (F6); durable-record correction with no `docs/project/**` edits (F7). Writable paths remain `tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` and `docs/work/outbound-http/QHTTP-B/test-writer-notes.md`. Checkpoint state -> `ResolvingFindings`.

Repair round applied and verified 2026-09-03 (test file + `test-writer-notes.md` only; `checkpoint.md` is the sole other change and is maintainer/orchestrator-owned capsule state). F1-F7 all `Fixed`: the fixture now materialises FraudManagement `7aabfef9…` in an isolated normalised detached `git worktree` (`core.autocrlf=false`/`core.eol=lf` before checkout, sparse-checkout excluding committed build output), analyses only that checkout, reproduces the amended Program Index fingerprint `df23b372…`, captures the complete ordered `data.runs[]` identity for both runs (`SequenceEqual` + `Assert.Single` + entry == amended pair), reads the SDK/toolchain version from CLI `--json` `data.toolchainVersion` (`10.0.302`), asserts the real `mermaid-cli --version` `11.16.0`, captures and compares CLI stderr (empty), records the shared-repo tracked+untracked `git status` and `git worktree list` before/after and asserts equality, runs a single fail-closed cleanup owner that throws on any residual, has no skip path, reads TCC base-address/API-key literals from the normalised config in memory and asserts they are absent from every rendered doc (field label only on failure), asserts the typed HTTP boundary is the only representation per root, and re-asserts `manifest listed-file count == 35` + manifest byte length `6282`. Regenerated normalised matrix (run1 == run2): POST md `20293c9d…` (3676 B), POST mmd `84621287…` (353 B), GET md `43f4210f…` (2850 B), GET mmd `363e1698…` (289 B), `index.md` `0de88502…` (2224 B), `seqdoc.manifest.json` `b48eb3d7…` (6282 B), complete-output digest `22045be6…`; ordered `--json` diagnostics `BE1001, BE2010, BE2010, PRED001` (digest `2f62d6e3…`, no `SEQHTTP001`); frozen blob SHA-256 solution `67d6b9f1…` / `BLL/BLL.csproj` `c38a35ee…` / `TCCService.cs` `eff26121…` all match. Focused lane 4/4 green. F6 frozen final gate (complete AcceptanceTests Release suite, unfiltered) run once: Failed 6 / Passed 38 / Skipped 0 / Total 44 — all 4 `OutboundHttpExternalCorpusTests` pass; the 6 failures are pre-existing and unrelated (`CorpusMediatRTests` SD1102 host MSBuild/SDK 10.0.302-vs-10.0.400 mismatch; five `ServiceClientExternalCorpusTests` on CreditTransfer `SD4011` frozen-MethodId drift and SMS `SD1101` NuGetAudit known-vulnerability build errors), in test files this branch does not touch (the only `tests/`+`src/` change vs `main` is the new `OutboundHttpExternalCorpusTests.cs`). Untouched-`main` comparison: Failed 24 / Passed 16 / Total 40 — every branch failure also fails on `main`, and this branch has strictly fewer. No checkpoint regression. Candidate is repaired and gate-verified; awaiting owner approval to push the repaired candidate to the PR #67 branch and request the maintainer's re-review.

Repaired candidate pushed to the PR #67 branch 2026-09-03 as commit `7cb8a55b814696e3b6b47cb8f742a4191ce8de5c` (base `08cb735`, `f602665..7cb8a55`; 3 files vs the previous head, exactly `tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` + the two `docs/work/outbound-http/QHTTP-B/` evidence files). The PR #67 body was rewritten to drop the now-resolved in-place fingerprint question and carry the normalised-checkout artifact matrix, and a full F1-F7 repair-handback comment was posted (https://github.com/Bilaltariq41/SeqDoc/pull/67#issuecomment-5525515632) with the seven items the maintainer requested: repaired/base SHA, disposition per finding, normalised fingerprint + run1/run2 matrix, focused command + result (4/4), complete Acceptance command + result (6 failed / 38 passed, all 6 pre-existing and reproduced on `main` at 24 failed / 16 passed), shared-repo status + worktree cleanup evidence, and diff-scope confirmation. Awaiting the maintainer's approval review. Merge remains gated on three maintainer-owned items outside the contributor allowlist: the approval review itself, the canonical GH-53 lifecycle synchronisation, and the `validate` CI check (which the maintainer stated fails on a GH-53 capsule-state mismatch that they will sync separately).

## Objective

Provide acceptance-only proof that the frozen direct `HttpClient` GET/POST semantics reach the production CLI and
visible conservative Markdown/Mermaid output for FraudManagement revision
`7aabfef98fa4d47781bd8a98b9061ddcafb88836`, `Release/net9.0`, using exactly these roots:

- `AddComplaint` — `method:v1:c3310b12f1a331d7ee9871a964209e89da0a0dcb84b086e4b62cbbbdc2a66417`
- `Lookups` — `method:v1:b7a44d4b1128669b35cda87326e73098991a24dbd0b975b9986c9050b8b45504`

The proof must remain conservative: it may establish only the compiler-evidenced direct GET/POST boundary. It must
not claim URI, content, headers, credentials, status, success, retry, remote execution, or other request/outcome
details.

## Target paths

The exact writable implementation path is:

- `tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs`

Checkpoint evidence may be recorded only under:

- `docs/work/outbound-http/QHTTP-B/**`

No other path is authorized. In particular, do not edit `src/**`, semantics, framework/scenario/planner/wording/
rendering contracts, fixtures, checked-in configuration, build/workflow files, or external-project source.

## Non-goals

- No production semantic repair or contract change.
- No claims about URI/content/header/credential/status/success/retry/remote execution.
- No fixture, configuration, build, workflow, or external-source changes.
- No application-name matching, invented evidence, or strengthened certainty.

## Risk inventory

1. The lane uses the wrong external revision, project, TFM, profile, or root fingerprint.
2. Stale output, dependency state, or cache produces false evidence.
3. GET or POST is falsely positive, missing, duplicated, or confused with a generic call.
4. URI, content, credentials, request values, or remote outcome leak into artifacts.
5. Evidence or certainty is strengthened beyond the frozen compiler semantics.
6. Required Markdown/Mermaid output is missing or invalid, links break, budgets are exceeded, or Mermaid fails.
7. Repeated clean runs are nondeterministic.
8. Acceptance pressure causes an unauthorized semantic repair or conceals identity drift.

## Existing coverage and test plan

Merged PR #59 supplies producer-to-CLI focused coverage for the frozen semantics. Reuse the isolated-worktree and
artifact-validation patterns from `ServiceClientExternalCorpusTests.cs`. The Test Writer must add only the minimum
acceptance-critical coverage in the sole writable test file for external identity, false-positive boundaries,
evidence/certainty, and deterministic output. Group the checks under one expensive fixture and use 2–4 distinct tests
within that soft budget; do not duplicate producer or unit assertions already covered by PR #59.

## Focused verification

Set `SEQDOC_TEST_PROJECTS_ROOT` to the supplied corpus and run:

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpus"
```

The lane must use FraudManagement revision `7aabfef98fa4d47781bd8a98b9061ddcafb88836`, `Release/net9.0`, and the
two exact roots above. Stop on identity drift or any required semantic change.

## Completion assertions

- The production CLI runs the pinned FraudManagement lane and visibly presents conservative direct GET and POST
  boundaries in Markdown/Mermaid.
- No sensitive/request values or remote outcome claims occur in the artifacts.
- Artifacts are complete, valid, linked, within budget, and Mermaid-valid.
- Two clean repeated runs are byte-identical.

## Review boundary

Stop at `ReviewRequired` after implementation and focused verification. Run one independent review, record every
finding as `Fixed`, `Rejected` with evidence, or `Deferred` with explicit owner approval, then run the final gate.
Acceptance pressure does not authorize semantic repair.

## Final gate

After the independent review and findings resolution, run the complete AcceptanceTests Release suite once:

```powershell
$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path "../SeqDoc-TestProjects").Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release
```

## First independent review and repair trace (F1–F7)

Reviewer: one `reviewer-medium` pass against the real diff, the capsule, and the `AGENTS.md` proof gates. Verdict: acceptable to proceed to the final gate with the four Major findings fixed on-branch; not a must-block. All findings resolved in one repair round, focused lane re-run 4/4 green, no `src/**` or other out-of-allowlist change. A subsequent owner review, a fourth repair pass, and a second independent `reviewer-medium` review followed — see the `## State` section above for the full chronological trace and the second review's F1–F11 (a separate numbering from the F1–F7 table below).

| Finding | Severity | Disposition | Resolution (test-file only) |
|---|---|---|---|
| QHTTP-B-F1 | Major | Fixed | `OutputIsEvidenceBoundedValueSafeAndDeterministic` now asserts `run1.DiagnosticCodes` equals the exact ordered merged-A baseline `["BE1001","BE2010","BE2010","PRED001"]` (named constant); drift is a stop condition. |
| QHTTP-B-F2 | Major | Fixed | `globalLeakTokens` gained `Authorization`, `StringContent`, `ByteArrayContent` (issue #53 objective 6); none appear in generated output. |
| QHTTP-B-F3 | Major | Fixed | Profile-ID and Program-Index-fingerprint checks are now unconditional: `Assert.NotNull` (message: proof BLOCKED, not degraded) then unconditional equality to the frozen constants. `CaptureIdentity` reads the exact `data.runs[].profileId` / `data.runs[].indexFingerprint` path (confirmed against `CliHost.CreateAnalyzeData` + CamelCase policy) with typed `TryGetProperty` instead of a loose all-property scan. |
| QHTTP-B-F4 | Major | Fixed | `RunGit` returns stdout and stderr separately; only stdout feeds value comparisons, stderr appears only in exception text. The pre-run clean-tree gate is now `git status --porcelain --untracked-files=no -- FraudManagement.sln BLL` (tracked-only, scoped to the analysed paths) so an unrelated `obj/` artifact or a benign CRLF warning cannot force a false BLOCKED. `HEAD == CorpusRevision`, `git show <rev>:<file>` blob SHA-256, and before/after frozen-file hash + scoped-status equality are unchanged as the non-mutation proof. |
| QHTTP-B-F5 | Minor | Fixed | `FrozenIdentityIsolationAndArtifactValidityHold` now asserts every generated top-level flow `*.md` (all except `index.md`) is a link target inside `index.md`. |
| QHTTP-B-F6 | Minor | Fixed | Same test parses `seqdoc.manifest.json`: listed-file count `== 35` (merged-A baseline) and every listed path is relative (not rooted, no `:`, no UNC). |
| QHTTP-B-F7 | Observation | Acknowledged, no logic change | One-line comment added at the `Count(markdown,"PostAsync"/"GetAsync") == 1` assertions noting it is a proxy for issue #53 objective 4 that holds for this frozen lane (behaviour phrase is the only carrier of the literal; `SC-DIRECT-BODY-UNAVAILABLE` counts and "no `SEQHTTP001`" corroborate). No brittle generic-phrase matching added. |

## Authoritative maintainer review (GitHub Request Changes, PR #67) — F1–F7

Reviewer: upstream owner Bilaltariq41, GitHub "Request Changes" on PR #67 @ `f602665`, 2026-09-03, plus frozen-contract amendment (issue #53 comment 5523887517). This is the single authoritative complete-candidate review for QHTTP-B. The prior contributor-invoked `reviewer-medium` passes are advisory / self-review evidence only.

| Finding | Severity | Disposition | Summary |
|---|---|---|---|
| F1 | Critical | Fixed | Replace the machine-specific in-place input with a reproducible normalized checkout (`core.autocrlf=false`, `core.eol=lf`) of `7aabfef9…`; amended fingerprint `df23b372…`; capture the complete ordered `data.runs[]` identity for both runs; regenerate the full normalized artifact matrix. |
| F2 | Major | Fixed | Fail-closed source isolation + cleanup: record shared-repo tracked+untracked status and `git worktree list` before/after and require equality; single cleanup owner; cleanup failure fails the test; never mutate the shared checkout. |
| F3 | Major | Fixed | No skip in this required lane — missing corpus / checkout failure / restore failure / node-or-Mermaid unavailability / identity drift are loud failures. |
| F4 | Major | Fixed | In-memory read of the frozen config; assert every non-empty base-address / API-key / URI / header / body literal is absent from all output without exposing a secret; per-root direct assertion that the typed HTTP boundary is the only representation. |
| F5 | Major | Fixed | Fail-closed CLI identity + version evidence: ordered `data.runs[]` identity, SDK version from the CLI JSON, non-empty SeqDoc CLI + Mermaid CLI versions, capture and compare CLI stderr. |
| F6 | Major | Fixed | Run the frozen final gate = the complete AcceptanceTests Release suite (unfiltered), after the focused lane; compare any unrelated failure against untouched `main`. |
| F7 | Major | Fixed | Durable-record correction: no `docs/project/**` edits; relabel prior reviews as advisory; record this as the one authoritative review plus one batched repair round; refresh the matrix + PR body with normalized evidence; no merge-readiness claim before the F6 gate runs. |

All seven resolved in one batched contributor repair round (`test-writer-medium` rework + `test-writer-low` follow-up), verified against the real working-tree diff. One independent `reviewer-medium` pass on the reworked candidate returned two Major items — IR-1 (this capsule still described the superseded in-place approach; fixed in Edit 2 below) and IR-2 (the rework dropped the explicit `manifest listed-file count == 35` assertion; re-added in the test file with a frozen manifest byte-length `6282` assertion) — plus an IR-1/F1 tightening (`Assert.Single(IdentitySet)` + entry-equals-amended-pair in both determinism/identity tests). IR-3/IR-4/IR-5 are Observations, no change.

### Corpus-pinning mechanism (adopted per the owner amendment)

Resolved by the owner's frozen-contract amendment (issue #53 comment 5523887517). The lane materialises FraudManagement revision `7aabfef98fa4d47781bd8a98b9061ddcafb88836` in an isolated detached `git worktree` under a short OS temp path, with `core.autocrlf=false` and `core.eol=lf` set BEFORE checkout and a sparse-checkout list excluding committed build output (`obj/`, `bin/`, `packages/`, `.vs/`, `PackageTmp/`). Only that line-ending-normalised checkout is analysed — every in-place code path and every fallback to the shared `Provided/FraudManagement` tree is removed. The amended Program Index fingerprint for the normalised checkout is `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`; the historical in-place `f9a36fd5…` value and its artifact hashes are superseded (retained in `test-writer-notes.md` as audit history). The shared supplied repository is never mutated: tracked + untracked `git status --porcelain` and `git worktree list --porcelain` are recorded before the lane and again after cleanup and asserted exactly equal; a single cleanup owner removes the detached worktree and every owned temp root and throws on any residual.

### Environment prerequisites (loud failure, never skip — per issue #53)

- The Provided corpus must be NuGet-restored for `net9.0`; a fresh unrestored checkout yields `BuildFailure`.
- `node` + `npx` must be runnable for the `@mermaid-js/mermaid-cli@11.16.0` render (first run needs network to populate the npm cache).
