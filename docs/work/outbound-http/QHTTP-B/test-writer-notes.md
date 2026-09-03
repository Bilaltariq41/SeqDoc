# QHTTP-B acceptance Test Writer notes

Scope: designed and landed the sole writable acceptance test
`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs`. No `src/**`, no existing test, no
config/build/fixture/`docs/project/**` change. `docs/work/outbound-http/QHTTP-B/checkpoint.md` not
touched (its `M` in `git status` is the orchestrator's own State line edit).

## Review findings F1-F7 applied (repair pass 2026-09-03)

All seven independent-review findings applied inside the allowlist
(`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` only). No production/`src/**`
change was needed.

- **F1** — `OutputIsEvidenceBoundedValueSafeAndDeterministic` now asserts
  `run1.DiagnosticCodes` equals the named `ExpectedDiagnosticCodeBaseline`
  `["BE1001", "BE2010", "BE2010", "PRED001"]` (new `static readonly string[]`). Observed sequence
  matches the baseline exactly; no drift. Existing "no SEQHTTP001" / `NotEmpty` checks kept.
- **F2** — `globalLeakTokens` extended with `"Authorization"`, `"StringContent"`,
  `"ByteArrayContent"`. Focused run still green — none appear in any generated Markdown/Mermaid.
- **F3** — profile-id / index-fingerprint checks are now unconditional. First `Assert.True(... is not
  null, "... BLOCKED, not degraded")`, then unconditional equality to the frozen constants.
  **Review premise was wrong:** the production CLI *does* surface both. Confirmed against
  `CliHost.CreateAnalyzeData` — exact JSON path is `data.runs[].profileId` and
  `data.runs[].indexFingerprint` (CamelCase naming policy in `CliOutput`). `CaptureIdentity`
  tightened to read that exact path (typed `TryGetProperty` + `JsonValueKind.String`), no longer a
  loose all-property scan. Observed values still match the frozen constants.
- **F4** — `RunGit` now returns `(int ExitCode, string StdOut, string StdErr)` separately; only
  `StdOut` feeds value comparisons, `StdErr` only appears in exception text. New `FrozenScopeStatus`
  helper runs `git status --porcelain --untracked-files=no -- FraudManagement.sln BLL` for both the
  pre-run gate and the before/after capture. `HEAD == CorpusRevision`, `git show <rev>:<file>` blob
  SHA-256, and before/after file-hash + status equality are unchanged as the real non-mutation proof.
  Scoped status is empty before and after.
- **F5** — `FrozenIdentityIsolationAndArtifactValidityHold` now asserts every generated top-level
  flow `*.md` (all `.md` except `index.md`, no subdirectory) is a link target in `index.md`.
- **F6** — the same test parses `seqdoc.manifest.json` from run 1: asserts `files` count == `35`
  and every `relativePath` is relative (not rooted, no `:` drive letter, no `\\`, no `//` UNC).
  Observed listed-file count: **35** (matches merged-A baseline).
- **F7** — comment-only: the `Count(markdown, "PostAsync"/"GetAsync") == 1` assertions now carry a
  one-line note that they are a proxy for issue #53 objective 4. No logic change, no generic-phrase
  matching added.

### Repair-pass focused result

`SEQDOC_TEST_PROJECTS_ROOT=<...>/SeqDoc-TestProjects dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpus" --logger "console;verbosity=detailed"`

**GREEN — Passed: 4, Failed: 0, Skipped: 0** (~1.4 min; `FrozenIdentityIsolationAndArtifactValidityHold`
~32 s incl. the Mermaid CLI 11.16.0 render). External corpus `git status` (scoped) empty before and
after; `HEAD` = `7aabfef98fa4d47781bd8a98b9061ddcafb88836`.

### Refreshed run1 == run2 matrix

| Field | Value | run1 == run2 |
|---|---|---|
| Ordered `--json` diagnostic codes | `BE1001, BE2010, BE2010, PRED001` (no `SEQHTTP001`) | yes (SequenceEqual) |
| Profile id | `profile:v1:f874be7e6b51bea2038f6cfac77ab510fc73e7208e9e47b4475e6a17896aaef1` | frozen match |
| Program Index fingerprint | `f9a36fd5662f01eead94779eb243f489d5bc1c6e1b7333d2f987b76e30d8146c` | frozen match |
| Manifest listed-file count | `35` | yes (byte-identical file) |
| Frozen blob SHA-256 (solution / BLL.csproj / TCCService.cs) | `67d6b9f1…` / `c38a35ee…` / `eff26121…` | frozen match |
| Generated file set + bytes | 17 md + 17 mmd + `index.md` + `seqdoc.manifest.json` | byte-identical |

## Focused verification result

Command:
`SEQDOC_TEST_PROJECTS_ROOT=<...>/SeqDoc-TestProjects dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpus" --logger "console;verbosity=detailed"`

Result: **GREEN — Passed: 4, Failed: 0, Skipped: 0.**
- `PostRootPresentsExactlyOneConservativePostBoundary`
- `GetRootPresentsExactlyOneConservativeGetBoundary`
- `OutputIsEvidenceBoundedValueSafeAndDeterministic`
- `FrozenIdentityIsolationAndArtifactValidityHold` (~34 s; includes the Mermaid CLI 11.16.0 render of all 17 run-1 diagrams)

External corpus before and after: `git status --porcelain` empty, `HEAD` = `7aabfef98fa4d47781bd8a98b9061ddcafb88836`. No repo or temp delta left behind.

## Test shape (one expensive fixture, 4 tests — within the 2–4 soft budget)

`OutboundHttpExternalCorpusFixture` (`IAsyncLifetime`, `DisableParallelization`) runs the production CLI
twice (`CliHost.RunAsync` in-process, `analyze FraudManagement.sln --repository-root <corpus> --config
<tmp two-root yaml> --configuration Release --framework net9.0 --cache <fresh> --output <fresh> --json`),
captures every generated byte, the ordered `--json` diagnostic records, the Mermaid budget, and the
`data.runs[]` profile id / index fingerprint. The four tests assert against captured artifacts without
re-running analysis.

Distinct failure modes covered (not Cartesian variants):
1. POST boundary: exactly one behaviour phrase + one `HTTP boundary` participant + one `HTTP POST request`
   message in the `AddComplaint` flow; single presentation (no duplicate generic `MethodCall`); no
   `SEQHTTP001`; unrelated `SC-DIRECT-BODY-UNAVAILABLE` count still `2`.
2. GET boundary: same for `Lookups`; `SC-DIRECT-BODY-UNAVAILABLE` count still `1`.
3. Value-safety + evidence-bounding + determinism: global leak-token scan (URI paths, `TCCBaseAddress`,
   `TCCAPIKey`, `_baseAddress`, `_APIKey`, `HttpResponseMessage`, `IsSuccessStatusCode`,
   `ReadAsStringAsync`); the boundary sentence + its Mermaid message carry a certainty token, name the
   source evidence, and carry no URI/host/header/body/credential/response/status/success/retry/
   resilience/remote/completion wording; run1 vs run2 byte-identical files + diagnostic records compared
   in emitted order (never sorted).
4. Frozen identity + isolation + artifact validity: `HEAD` == frozen revision; `git show <rev>:<file>`
   SHA-256 of the three frozen files == frozen constants; frozen source files unchanged before/after;
   observed profile id / fingerprint == frozen constants; POST/GET Markdown + Mermaid + `index.md` +
   `seqdoc.manifest.json` all present; every Markdown link resolves; every `.mmd` within budget and
   `MermaidValidator`-valid; real `npx --yes @mermaid-js/mermaid-cli@11.16.0` render of every run-1
   `.mmd` (exit 0 + non-empty SVG).

## Captured candidate-matrix values (run 1 == run 2, byte-identical)

- Outcome `Succeeded`. Ordered `--json` diagnostics: `BE1001, BE2010, BE2010, PRED001` (matches the issue
  #53 baseline; no `SEQHTTP001`; no global diagnostic suppressed).
- Profile id `profile:v1:f874be7e6b51bea2038f6cfac77ab510fc73e7208e9e47b4475e6a17896aaef1` — matches frozen.
- Program Index fingerprint `f9a36fd5662f01eead94779eb243f489d5bc1c6e1b7333d2f987b76e30d8146c` — matches frozen.
- Frozen file SHA-256 (git blob at `7aabfef9…`): solution
  `67d6b9f15be05f86c06ea17fa92dd7474b8886b876d1d69cf11552741ffaaca1`, `BLL/BLL.csproj`
  `c38a35ee7b3acf227fb9988ced35dceb2dec36165e8f8f01bb6b82ee6f658a06`, `BLL/TCCIntegration/TCCService.cs`
  `eff261211900578a493d40900cd0de5418dbbd132bbc4f806f684b31e184dfce` — all match frozen.
- Output set: 17 flow Markdown + 17 Mermaid + `index.md` + `seqdoc.manifest.json` (35 listed files) —
  matches the issue #53 baseline count.
- POST flow `bll-tccintegration-tccservice-addcomplaint-bll-tccintegration-addcomplaintrequest-f1cc2038.md`:
  behaviour bullet `The method calls HttpClient.PostAsync at an outbound HTTP POST request boundary.`
  `_(certainty: Exact; evidence: BLL/TCCIntegration/TCCService.cs, seqdoc.system-net-http.outbound:1.0.0)_`;
  Mermaid `action->>http-boundary: HTTP POST request`; `SC-DIRECT-BODY-UNAVAILABLE` ×2.
- GET flow `bll-tccintegration-tccservice-lookups-94a25a61.md`: behaviour bullet
  `The method calls HttpClient.GetAsync at an outbound HTTP GET request boundary.`
  `_(certainty: Exact; evidence: BLL/TCCIntegration/TCCService.cs, seqdoc.system-net-http.outbound:1.0.0)_`;
  Mermaid `action->>http-boundary: HTTP GET request`; `SC-DIRECT-BODY-UNAVAILABLE` ×1.
- Mermaid CLI: `@mermaid-js/mermaid-cli@11.16.0` via `npx` (node v24.20.0) rendered every run-1 `.mmd`
  to a non-empty SVG, exit 0.

No semantic/production gap found. The merged issue #54 implementation reaches the production CLI output
conservatively (boundary existence only; no URI/host/header/body/credential/response/status/success/
retry/remote-completion claim; `certainty: Exact` on the boundary-existence claim, which is not a
strengthening — the call provably occurs).

## Decision points for the orchestrator

1. **Corpus pinning method (deviation from "git worktree add --detach").** A detached worktree checkout
   of FraudManagement fails on this Windows host: the repo has committed long-path build output
   (`obj/.../PackageTmp/...`, `packages/...`) that overflows `MAX_PATH` on a fresh checkout
   (`fatal: Could not reset index file`), and `core.autocrlf=true` rewrites `.cs`/`.sln` line endings so
   the worktree produced a *different* Program Index fingerprint (`464030ca…` vs the frozen
   `f9a36fd5…`). The Provided FraudManagement checkout is already sitting exactly on the frozen revision
   with a clean tree, so the fixture instead **asserts** `HEAD == 7aabfef9…` and an empty
   `git status --porcelain` as hard blockers (loud failure, never skip), reads frozen-file identity from
   `git show <rev>:<file>` (filter-independent), analyses that checkout directly (the pattern the
   existing `ServiceClientExternalCorpusTests` FraudManagement lane already uses), and hashes the three
   frozen source files before/after to prove they are untouched. All CLI caches/output/YAML/Mermaid
   renders stay under a fresh OS temp root that is deleted on success and failure. If the maintainer
   requires a literal detached worktree, that needs a shorter checkout root and `core.autocrlf=false`
   in the corpus repo (or a corpus that does not commit build output).

2. **Environment prerequisite: the Provided corpus must be NuGet-restored for `net9.0`.** On a fresh
   checkout the run is `BuildFailure` (`Microsoft.EntityFrameworkCore.Analyzers 9.0.11 was not found …
   NuGet restore might have only partially completed`). Per issue #53 this is BLOCKED — the fixture
   raises it as a loud failure, never a skip/pass. A one-off `dotnet restore FraudManagement.sln`
   cleared it here; CI must ensure the corpus is restored (same assumption the existing service-client
   FraudManagement lane relies on).

3. **`.git/worktrees` cruft from earlier worktree experiments.** Superseded — the final fixture never
   creates a worktree. Any residual `.git/worktrees/seqdoc-qhttpb-corpus-*` admin dirs in the corpus
   repo were left by intermediate debugging (OneDrive locked `.git` against `git worktree prune`) and
   have been removed manually; `git worktree list` and `git status` on the corpus are clean.

4. **Value-safety scope.** Hard leak tokens (URI path literals, config keys, `_baseAddress`/`_APIKey`,
   BCL response types) are scanned across ALL generated Markdown/Mermaid. The softer
   response/status/success/retry/remote wording denylist is scoped to the HTTP boundary sentence and its
   Mermaid message only — unrelated pre-existing generic caller-syntax on other lines (e.g.
   `The service assigns: BaseAddress = System.Uri.`, which carries no value) is out of scope for this
   HTTP-family lane and matches the `ServiceClientExternalCorpusTests` boundary-clause scoping
   precedent.

## Residual risks

- The lane depends on a restored, revision-pinned Provided corpus (blocker if absent — by design).
- Mermaid CLI is fetched via `npx --yes …@11.16.0`; first run needs network to populate the npm cache
  (genuine unavailability is a loud failure, per issue #53, not a skip).
- Best-effort temp cleanup: a SQLite cache file held open by the OS can leak a temp directory (tolerated
  by the existing acceptance-lane pattern; not a test failure).

## Second repair pass (2026-09-03) — BLOCKED: frozen Program Index fingerprint is not reproducible from a git worktree

The owner required the corpus isolation reworked to a real detached `git worktree` and the analysed
worktree to reproduce the frozen `data.runs[].indexFingerprint`
`f9a36fd5662f01eead94779eb243f489d5bc1c6e1b7333d2f987b76e30d8146c`, with an explicit instruction to
STOP (no in-place fallback) if it does not.

Exact worktree mechanism attempted (git.exe argv, no shell mangling):

```
git -C <corpus> worktree add --no-checkout --detach <wt> 7aabfef98fa4d47781bd8a98b9061ddcafb88836
git -C <wt> config core.longpaths true
git -C <wt> config core.autocrlf false
git -C <wt> config core.eol lf
git -C <wt> sparse-checkout set --no-cone /*  '!obj/' '!bin/' '!packages/' '!.vs/' '!PackageTmp/'
git -C <wt> checkout
dotnet restore <wt>/FraudManagement.sln     # PackageReference only; fast, cache-warm
```

MAX_PATH and the sparse exclusion both worked (283 source files, longest on-disk path 135 chars, every
`*.csproj` + `FraudManagement.sln` present, `git status` clean). The three frozen blob SHA-256 values
(`FraudManagement.sln` / `BLL/BLL.csproj` / `BLL/TCCIntegration/TCCService.cs`) matched exactly, and the
CLI run succeeded with `profileId` == frozen and ordered diagnostics `BE1001, BE2010, BE2010, PRED001`.

**But `indexFingerprint` was `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`, not the
frozen `f9a36fd5…`.** The prior attempt (worktree inheriting `core.autocrlf=true`) produced yet a third
value, `464030ca…`.

Root cause: the frozen fingerprint was baselined against the shared **in-place** `Provided/FraudManagement`
working tree, whose line endings are a non-reproducible historical artifact of `* text=auto` +
`core.autocrlf=true` applied inconsistently across file history. Confirmed byte-level:

| File | git blob | in-place working tree | faithful (`eol=lf`) worktree | `autocrlf=true` worktree |
|---|---|---|---|---|
| `BLL/Logging.cs` | LF | **CRLF** | LF | CRLF |
| `BLL/DTOs/EligibleForTerminationDTO.cs` | LF | **CRLF** | LF | CRLF |
| `BLL/DTOs/SMSReminderDTO.cs` | LF | **CRLF** | LF | CRLF |
| `FraudManagementWindowsService/Timers/Reminding.cs` (+4 more) | LF | LF | LF | **CRLF** |

No single `core.autocrlf` / `core.eol` value reproduces the in-place mix (some tracked `.cs` are CRLF
in-place, others LF in-place). The in-place tree additionally carries untracked files that feed nothing
here but confirm it is a mutable checkout (`BLL/Properties/PublishProfiles`, `.vs/`, `*.csproj.user`).
Running the current-branch CLI against the **in-place** checkout does reproduce `f9a36fd5…` exactly.

Per the STOP instruction, no in-place fallback was written and the fixture was **not** modified. G1–G4
(candidate-matrix byte/SHA capture, diagnostics ordered-record digest, manifest content-hash cross-check,
certainty pin) were not applied because they depend on the reworked fixture's run1/run2 and the owner
wants them in the same reworked file.

Smallest decision needed from the owner (one of):
1. Re-baseline the frozen `indexFingerprint` (and the POST/GET/index/manifest artifact hashes in issue
   #53) against a **clean, line-ending-normalised git worktree** of `7aabfef9…`
   (`core.autocrlf=false`, `core.eol=lf`): the reproducible value is
   `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`. Then the worktree rework is
   straightforward.
2. Have the corpus repo itself re-checked-out / re-normalised so the shared in-place tree equals its git
   content, then re-baseline once.
3. Explicitly accept in-place assertion for QHTTP-B (the current committed-fixture approach) and drop the
   worktree requirement.

Cleanup: experiment worktree removed (`git worktree remove --force` + `worktree prune`; the
`.git/worktrees/sqb6030` admin dir needed a retried manual `rm` due to the known OneDrive lock). Corpus
left clean — `git status --porcelain` empty, `git worktree list` back to its original two entries
(`Provided/FraudManagement` + the pre-existing `seqdoc-i8-corpus` entry, which is not mine). All temp
roots deleted.

## Third repair pass (in-place accepted) — 2026-09-03

Owner decision: keep the in-place-pinned-checkout approach and drop the worktree requirement (worktree
rework is BLOCKED — frozen `indexFingerprint` is tied to the shared in-place checkout's LF/CRLF mix; a
normalised worktree yields `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`). The
F1–F7 test file was unchanged by the blocked pass. Changes applied inside the allowlist
(`OutboundHttpExternalCorpusTests.cs` + this file) only — no `src/**`, no fixture/config/build change,
no production change implicated.

- **G1** — `FrozenIdentityIsolationAndArtifactValidityHold` now computes byte length + SHA-256 for each
  named matrix artifact for run 1 AND run 2 and asserts equality, plus a complete-output digest
  (SHA-256 over the ordered `"<relativePath>\t<sha256(content)>"` list for every file under the output
  root, including operational `.seqdoc/**`) for both runs, asserted equal. Values printed via
  `ITestOutputHelper` and recorded below. (`OutboundHttpExternalCorpusTests.cs` `namedArtifacts` block
  + `CompleteOutputDigest`/`Sha256Hex` helpers.)
- **G2** — `OutputIsEvidenceBoundedValueSafeAndDeterministic` now computes a normalized digest =
  SHA-256 over the concatenation of the ordered raw `diagnostics[]` JSON records for run 1 and run 2
  and asserts equality. Existing `ExpectedDiagnosticCodeBaseline` `["BE1001","BE2010","BE2010","PRED001"]`
  equality and the "no SEQHTTP001" checks kept.
- **G3** — same test parses `seqdoc.manifest.json` (run 1) and for every `{ relativePath, sha256 }`
  entry asserts a captured output file with that path exists and its SHA-256 equals the listed hash;
  then asserts the manifest's listed-path set equals the set of all generated files except operational
  ones (`.seqdoc/**` and `seqdoc.manifest.json`). Existing count `== 35` and relative-path assertions
  kept. Manifest hash field name confirmed as `sha256` (`OutputSetActivator.ManifestEntry`,
  CamelCase policy).
- **G4** — the loose `Assert.Matches(@"certainty: (Exact|Probable|Conservative)", ...)` is replaced
  with `Assert.Contains("certainty: Exact", boundaryLine, StringComparison.Ordinal)` for both
  boundaries, so a regression that WEAKENED or STRENGTHENED the certainty fails. `evidence:
  BLL/TCCIntegration/TCCService.cs` assertion kept. Observed value is `Exact` (matches prior notes).
- **G5** — class XML doc, fixture XML doc, and the "isolated worktree" source-preservation comment now
  accurately describe the real mechanism: the shared `Provided/FraudManagement` checkout is asserted at
  frozen revision `7aabfef9…` with a clean scoped working tree and analysed IN PLACE (matching
  `ServiceClientExternalCorpusTests`), never mutated (frozen-file SHA-256 before/after; all CLI
  cache/output/YAML/render under a fresh OS temp root), with a one-line note on why a normalised
  worktree is not used (fingerprint tied to in-place line endings — owner-accepted boundary).

### G1 required candidate artifact matrix (observed; in-place checkout, `Release/net9.0`)

| Artifact | run 1 byte length | run 1 SHA-256 | run 2 byte length | run 2 SHA-256 | equal? |
|---|---|---|---|---|---|
| POST Markdown `bll-tccintegration-tccservice-addcomplaint-bll-tccintegration-addcomplaintrequest-f1cc2038.md` | 3676 | `20293c9d5237691a195bf7901c577794c1ebe1fe93d173ece1c55d93ce572dfe` | 3676 | `20293c9d5237691a195bf7901c577794c1ebe1fe93d173ece1c55d93ce572dfe` | yes |
| POST Mermaid `…-f1cc2038.mmd` | 353 | `8462128742ac7768fc8c0a075b32399e489b2a3500f917af10ab6dc458fb052b` | 353 | `8462128742ac7768fc8c0a075b32399e489b2a3500f917af10ab6dc458fb052b` | yes |
| GET Markdown `bll-tccintegration-tccservice-lookups-94a25a61.md` | 2850 | `43f4210f413d2c4cf8a2ec883f9abc922f19a1d1e6a150f173d19d8469ddb072` | 2850 | `43f4210f413d2c4cf8a2ec883f9abc922f19a1d1e6a150f173d19d8469ddb072` | yes |
| GET Mermaid `…-94a25a61.mmd` | 289 | `363e169808a83c3a5df148619e706882922f2321b17e04b25a5ba4b56e039dbd` | 289 | `363e169808a83c3a5df148619e706882922f2321b17e04b25a5ba4b56e039dbd` | yes |
| `index.md` | 2224 | `fca7088b5f34a18ff5822424ca04cd9f3e20bddb6354ef2e2f62fd37e54932e1` | 2224 | `fca7088b5f34a18ff5822424ca04cd9f3e20bddb6354ef2e2f62fd37e54932e1` | yes |
| `seqdoc.manifest.json` | 6282 | `d51d2436094dfa97d0ae18a171faacb3081749461c2677e655eb95eab3de57ae` | 6282 | `d51d2436094dfa97d0ae18a171faacb3081749461c2677e655eb95eab3de57ae` | yes |
| Complete-output digest (all files incl. `.seqdoc/**`) | — | `8fce2d7e59cfb82bec4ccb60079b3297f2277de1673337cb36064c9357c4d972` | — | `8fce2d7e59cfb82bec4ccb60079b3297f2277de1673337cb36064c9357c4d972` | yes |

Notes:
- The issue #53 "Measured clean-baseline artifacts" hashes/lengths are pre-#54 RED baseline
  (`manifest` 6282 B matches; POST/GET Markdown+Mermaid hashes/lengths now differ because the merged
  #54 HTTP boundary is present — expected). Manifest byte length is unchanged at 6282 (35 listed
  files, sorted paths + content hashes).
- **G2 diagnostics ordered-record digest:** run 1 = run 2 =
  `2f62d6e3193c4926ff6b3a25388b2dc51d275bc716a2d5009c6c18b5d2eae3fc`. Ordered codes
  `BE1001, BE2010, BE2010, PRED001` (no `SEQHTTP001`).
- **G3:** every manifest entry cross-checked (path present + content SHA-256 equal); manifest listed
  set == generated files minus `.seqdoc/**` and `seqdoc.manifest.json`. Count 35.
- **G4:** both boundary lines carry `certainty: Exact` and `evidence: BLL/TCCIntegration/TCCService.cs`.
- Resolved .NET SDK: `dotnet --version` = `10.0.302` (build/test host; lane target TFM `net9.0`).
- `@mermaid-js/mermaid-cli` version `11.16.0`; node `v24.20.0`.
- Markdown link-check: pass, 17 links checked (every link resolves to a generated file).
- Mermaid budget `45000` chars; every one of the 17 run-1 `.mmd` well within budget (largest 434) and
  `MermaidValidator`-clean; real `npx --yes @mermaid-js/mermaid-cli@11.16.0` render of all 17 = exit 0
  + non-empty SVG.
- Profile ID `profile:v1:f874be7e6b51bea2038f6cfac77ab510fc73e7208e9e47b4475e6a17896aaef1`, Program
  Index fingerprint `f9a36fd5662f01eead94779eb243f489d5bc1c6e1b7333d2f987b76e30d8146c` — both match
  frozen. Frozen blob SHA-256 (solution / `BLL/BLL.csproj` / `TCCService.cs`) `67d6b9f1…` / `c38a35ee…`
  / `eff26121…` — match frozen.
- In-place `git status --porcelain --untracked-files=no -- FraudManagement.sln BLL`: empty before and
  empty after both runs. `HEAD` = `7aabfef98fa4d47781bd8a98b9061ddcafb88836`. No corpus or temp delta
  left behind.

### Third-repair-pass verification

- Focused (once): `SEQDOC_TEST_PROJECTS_ROOT=<...>/SeqDoc-TestProjects dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpus" --logger "console;verbosity=detailed"`
  → **GREEN — Passed: 4, Failed: 0, Skipped: 0** (~1.4 min; `FrozenIdentityIsolationAndArtifactValidityHold` ~33 s incl. Mermaid CLI render).
- Final gate (once): `dotnet build SeqDoc.slnx -c Release` → **0 warnings / 0 errors**, then
  `dotnet test … --no-build --filter "FullyQualifiedName~OutboundHttpExternalCorpusTests"` →
  **GREEN — Passed: 4, Failed: 0, Skipped: 0** (34 s).

## Fourth repair pass (second independent review) — 2026-09-03

All findings are test-file-only; no `src/**` / production change implicated. Edits confined to
`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` + this file.

- **F1 (Major) — actionable frozen-fingerprint failure message.** The bare
  `Assert.Equal(ProgramIndexFingerprint, ObservedIndexFingerprint)` (and `ProfileId`) in
  `FrozenIdentityIsolationAndArtifactValidityHold` are now `Assert.True(string.Equals(...), "<message>")`
  hard failures. The fingerprint message states a mismatch most likely means the local
  `Provided/FraudManagement` working-tree line endings differ from the frozen baseline (LF/CRLF), gives
  the clean/normalised-worktree value `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`,
  says this is a corpus-normalisation / frozen-baseline decision for the issue #53 owner (not a semantic
  regression), and points at this file's "Known boundary for the PR". Both observed and known values are
  also emitted via `ITestOutputHelper` before the assert. Still a hard failure; never skips.
- **F2 (Minor) — stale MAX_PATH comment.** The `InitializeAsync` revision-pinning comment no longer cites
  MAX_PATH; it now states the frozen Program Index fingerprint is tied to the shared in-place checkout's
  inconsistent historical LF/CRLF working tree and a normalised worktree yields a different fingerprint
  (owner-accepted boundary), matching the class / fixture XML docs.
- **F3 (Minor) — complete external git status recorded.** `FrozenScopeStatus`
  (`… -- FraudManagement.sln BLL`) is unchanged as the hard pre-run/post-run gate. New
  `UnscopedTrackedStatus` runs `git status --porcelain --untracked-files=no` (whole corpus) before and
  after; captured to `ExternalGitStatusUnscopedBefore/After` and emitted via `ITestOutputHelper`
  ("recorded, not gated"). Observed empty before and after.
- **F4 (Minor) — `SEQHTTP001`-absence stream.** The per-flow `Assert.DoesNotContain(UnsupportedDiagnosticCode, run.AllText)`
  in the POST and GET claim tests now target `run.DiagnosticCodes` (the CLI `--json` code stream where
  the code would actually appear). Claim 3's `run1.DiagnosticCodes` / baseline checks unchanged.
- **F5 (Observation) — operational-path exclusion.** The manifest listed-set vs generated-set equality
  now defines "operational" as any file that is NOT `*.md` and NOT `*.mmd`, so it robustly excludes
  `.seqdoc/**`, `seqdoc.manifest.json`, and `seqdoc.stale` (written at the output root by
  `OutputSetActivator` on a failed run; absent on success today). Count `== 35` and the per-entry
  content-hash cross-check are unchanged.
- **F7 (Observation) — run-2 identity + cross-run equality.** `CaptureIdentity` is now a pure
  `(string?, string?)` function called on BOTH runs; run 2 populates
  `ObservedProfileIdRun2` / `ObservedIndexFingerprintRun2`. Claim 4 asserts run1 == run2 for both and
  emits the result. Observed `profileId=True indexFingerprint=True`.
- **F10 (Observation) — `ReadMermaidBudget` silent default.** `ReadMermaidBudget` is now an instance
  method that sets `MermaidBudgetResolvedFromJson = true` only when
  `data.configuration.diagramBudget.maxMermaidCharacters.value` is actually found. Claim 4 hard-asserts
  that flag so a future `--json` shape change fails loud instead of silently using the `45000` fallback.
  Observed: resolved from JSON, value `45000`.
- **F11 (Observation) — boundary-wording denylist across every HTTP flow.** In addition to the two
  configured roots' boundary lines and the all-17-flow `globalLeakTokens` value scan, Claim 3 now
  applies `boundaryForbiddenWords` to every generated flow `*.md` that carries `PostBehaviorPhrase` or
  `GetBehaviorPhrase`, scoped to that phrase's line plus its matching Mermaid message line. For this
  frozen lane only the two known flows carry a phrase; scan passes.

Acknowledged, not changed: **F6** (`certainty: Exact` pin stays — defensible acceptance lock),
**F8** (`docs/work/**` evidence path authorised by the owner's "QHTTP-B ready" comment; PR body
discloses it), **F9** (objective-4 `Count(… "PostAsync") == 1` proxy acceptable).

### Fourth-repair-pass verification

- Focused (once): `SEQDOC_TEST_PROJECTS_ROOT=<...>/SeqDoc-TestProjects dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpus" --logger "console;verbosity=detailed"`
  → **GREEN — Passed: 4, Failed: 0, Skipped: 0** (1.42 min; `FrozenIdentityIsolationAndArtifactValidityHold` ~32 s incl. Mermaid CLI 11.16.0 render of all 17 run-1 diagrams).
- Final gate (once): `dotnet build SeqDoc.slnx -c Release` → **Build succeeded, 0 Warning(s) / 0 Error(s)**, then
  `dotnet test … --no-build --filter "FullyQualifiedName~OutboundHttpExternalCorpusTests" --logger "console;verbosity=detailed"`
  → **GREEN — Passed: 4, Failed: 0, Skipped: 0** (1.49 min).

Matrix values unchanged from the third pass: profileId `profile:v1:f874be7e…`, indexFingerprint
`f9a36fd5…`, complete-output digest `8fce2d7e59cfb82bec4ccb60079b3297f2277de1673337cb36064c9357c4d972`
(run1 == run2), diagnostics ordered-record digest
`2f62d6e3193c4926ff6b3a25388b2dc51d275bc716a2d5009c6c18b5d2eae3fc`, ordered codes
`BE1001, BE2010, BE2010, PRED001` (no `SEQHTTP001`), manifest 35 files / 6282 B, mermaid budget 45000
(resolved from `--json`), frozen-scope and whole-corpus `git status` empty before and after,
`HEAD = 7aabfef98fa4d47781bd8a98b9061ddcafb88836`.

## Known boundary for the PR

The frozen `indexFingerprint` `f9a36fd5662f01eead94779eb243f489d5bc1c6e1b7333d2f987b76e30d8146c` and
the baseline artifact hashes in issue #53 are reproducible **only** from the in-place
`Provided/FraudManagement` checkout, whose working tree carries an inconsistent historical LF/CRLF mix
(`BLL/Logging.cs`, `BLL/DTOs/EligibleForTerminationDTO.cs`, several
`FraudManagementWindowsService/Timers/*.cs` are LF in git but CRLF on disk, while siblings in the same
directory are LF on disk). Git stores every one as LF, so no clean checkout or single
`core.autocrlf`/`core.eol` setting reproduces the mix. A clean/normalised worktree of `7aabfef9…`
(`core.autocrlf=false`, `core.eol=lf`) builds and analyses deterministically but yields
`indexFingerprint` `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`. The QHTTP-B
lane therefore asserts the in-place checkout identity (`HEAD == 7aabfef9…`, clean scoped tree, frozen
blob SHA-256) and analyses it in place, matching the existing `ServiceClientExternalCorpusTests`
FraudManagement lane. Recommendation for the maintainer who owns issue #53: either renormalise the
corpus checkout and re-baseline the frozen fingerprint + artifact hashes once, or accept this boundary
as documented.
