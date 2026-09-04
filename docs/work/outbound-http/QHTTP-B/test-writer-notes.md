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

## Seventh repair pass — second authoritative GitHub review (G1-G3), 2026-09-04

Acceptance-only, no production/semantic change. Edits confined to
`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` + this file. The frozen artifact
matrix is byte-identical to the sixth pass (POST md 3676 B / `20293c9d…`, POST mmd 353 B / `84621287…`,
GET md 2850 B / `43f4210f…`, GET mmd 289 B / `363e1698…`, `index.md` 2224 B / `0de88502…`,
`seqdoc.manifest.json` 6282 B / `b48eb3d7…`, complete-output digest `22045be6…`, diagnostics
ordered-record digest `2f62d6e3…`, ordered codes `BE1001, BE2010, BE2010, PRED001`, no `SEQHTTP001`).
The three items add assertions over the same generated output; no new analysis input.

### G1 — fail closed on malformed run identity

- New pure helper `OutboundHttpRunIdentity.ParseRuns(JsonElement data)` (static method on the new
  `OutboundHttpRunIdentity` record `(ProfileId, RunId, IndexFingerprint)`). Throws `XunitException`
  with an actionable, defect-naming message when: `data` has no `runs`; `runs` is not a JSON array;
  `runs` is empty; any entry is not a JSON object; any of `profileId` / `runId` / `indexFingerprint`
  is missing, non-string, or empty/whitespace. `RunCliAsync` now calls it instead of the old
  `if (entry.ValueKind != Object) continue;` skip + `string.Empty` fallback.
- `OutboundHttpLaneRun.IdentitySet` is now `ImmutableArray<OutboundHttpRunIdentity>` (carries `runId`,
  previously dropped). A computed `IdentityPairs` projects `(ProfileId, IndexFingerprint)` so every
  existing identity assertion is preserved unchanged (`SequenceEqual` run1==run2, amended pair present
  exactly once, `Assert.Single`, `IdentityPairs[0] == amendedPair`). New: `runId` on the single entry
  asserted non-empty and equal across runs. The amended frozen pair stays
  `(ProfileId, ProgramIndexFingerprint)`. The two duplicated identity blocks are folded into one
  `AssertRunIdentitySet` helper.
- New `[Fact] MalformedRunIdentityFailsClosed` — synthetic `JsonDocument` inputs for each malformed
  class above (message substring asserted per defect) plus one well-formed two-entry input that
  returns the ordered tuple list.
- `RunCliAsync` also captures `data.availableTargetFrameworks`; Claim 4 asserts it contains `net9.0`
  and is equal across runs. No per-run project/config/TFM field was invented — the CLI emits only
  `{ profileId, runId, indexFingerprint }` per run (confirmed `src/SeqDoc.Cli/CliHost.cs:344-363`).

### G2 — checkout-path and timestamp hygiene, asserted directly

- New pure helper `OutboundHttpArtifactHygiene.FindVolatileMarkers(text, forbiddenPaths)` returning
  `(Marker, Kind)` hits: fixture-owned absolute path in OS form and forward-slash form
  (`OrdinalIgnoreCase`); ISO-8601 date / date-time (`\b\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2})?)?\b`);
  `GMT`/`UTC`-suffixed clock; `Environment.MachineName` / `UserName` / `UserDomainName` (length >= 4,
  reported as KIND only, never the value).
- Fixture exposes `OwnedPathMarkers` (the `_ownedTempRoots` list + `_worktreePath`: worktree parent,
  both CLI `out` roots, both CLI `cache` roots, the YAML `cfg` root, and the Mermaid-render root).
- Claim 3 scans EVERY run-1 file (`run1.Files` — all `.md`, `.mmd`, `index.md`,
  `seqdoc.manifest.json`, `.seqdoc/**`); asserts zero markers, message names the file + marker KIND.
  Observed clean — the journal/manifest are timestamp-free and path-free by design
  (`OutputSetActivator` doc comment) and run1==run2 byte-equality already proves no wall-clock content.
- Deliberately-excluded patterns (documented): a bare 4-digit year and dotted version tokens
  (`net9.0`, `9.0.11`, `seqdoc.system-net-http.outbound:1.0.0`) are NOT matched — the literal
  `-NN-NN` shape is required, so package/reference version strings and 64-hex SHA digests do not
  false-positive.
- New `[Fact] ArtifactHygieneScannerCatchesInjectedVolatileMarkers` — synthetic strings each carrying
  one leak class (OS temp path; its slash form; ISO timestamp; `GMT` clock; machine-name token) plus
  a clean boundary-phrase string that yields no hits.

### G3 — full sensitive fixture corpus

- New helpers `OutboundHttpSensitiveCorpus.Build(configValues)` / `.FindLeaks(text, corpus)` over
  `OutboundHttpCorpusEntry(Label, Kind, Token)`. `StructuralVocabulary` is a separate, explicit list.
- Forbidden corpus (token names only):
  - `request-path`: `threeThirty/complaint/addComplaint`, `threeThirty/lookup/updateType/all`,
    `threeThirty/notification/update`, `threeThirty/callBypass/add`, `threeThirty`.
  - `header` / `media-type`: `Authorization`, `application/json`.
  - `config-key`: `TCCBaseAddress`, `TCCAPIKey`, `_baseAddress`, `_APIKey`.
  - `bcl-token`: `ByteArrayContent`, `HttpResponseMessage`, `IsSuccessStatusCode`, `ReadAsStringAsync`,
    `HttpClientHandler`, `ServerCertificateCustomValidationCallback`, `MediaTypeHeaderValue`,
    `MediaTypeWithQualityHeaderValue`, `SerializeObject`, `DeserializeObject`.
  - `payload-identifier`: `reporterNumber`, `reportedIdentity`, `typeOfComplaint`, `operatorTcn`,
    `serviceRating`, `serviceFeedback`, `tccTcn`, `updateType`.
  - `config-value` (in-memory only, from the existing `ReadSensitiveConfigValues`, length >= 8; base64
    payload after `Basic ` and the absolute-URI host also added): the TCC base-address URLs, the URI
    host, the API-key value and its base64 payload. Failure messages carry the field LABEL only —
    never the value, hash, or snapshot.
- Excluded (documented false-positive risks, NOT in the corpus): generic English words
  `message`, `code`, `description`, `reason`, `contentType`, `list`, `value`, `status`, `tcn`; and
  `PostAsync` / `GetAsync` / `HttpClient` / the behavior phrases (structural).
- Structural-vs-forbidden split rationale: the accepted behavior phrases literally contain
  `HttpClient.PostAsync` / `HttpClient.GetAsync`, and `HTTP boundary` / `HTTP POST request` /
  `HTTP GET request` / `HTTP` / `POST` / `GET` / `request` / `boundary` / `outbound` are the
  intended conservative vocabulary — putting any of these in the forbidden set would contradict the
  accepted output and `AssertNoGenericHttpClientPresentation` (kept, Mermaid side).
- Scope split (judgment call, see below): `request-path` / `header` / `media-type` / `config-key` /
  `config-value` / `payload-identifier` are scanned document-wide over every `run1.Files` entry.
  `bcl-token` is scanned scoped to the boundary line + Mermaid message only.
- Per-`.md` check: `Count("HttpClient.PostAsync")` == `Count(PostBehaviorPhrase)` and likewise for
  GET — the BCL call token never appears beyond the accepted phrase.
- New `[Fact] SensitiveCorpusCatchesForbiddenTokensButNotStructuralVocabulary` — corpus is non-empty,
  has a base-address entry and an API-key entry, catches a synthetic doc with a payload id + request
  path + config host, and does NOT flag a doc containing only structural vocabulary + the accepted
  behavior phrases.

### Judgment call — `bcl-token` scope (G3)

The first focused run flagged `MediaTypeHeaderValue`, `SerializeObject`, `DeserializeObject` in the
POST flow `*.md`. These are unrelated Method Flow steps of `TCCService.AddComplaint`
(`request.Content.Headers.ContentType = new MediaTypeHeaderValue(...)`,
`JsonConvert.SerializeObject/DeserializeObject`) that the typed pipeline renders as ordinary call
steps — not the HTTP boundary presentation and not a secret. Removing them document-wide would need a
production Method Flow change, which this checkpoint forbids, and would drift the frozen matrix. The
maintainer's G3 wording is "must not surface as the boundary vocabulary", so `bcl-token` entries are
asserted absent from the boundary line + Mermaid message (same scoping precedent as
`boundaryForbiddenWords` and `ServiceClientExternalCorpusTests`). All genuinely secret / request-
specific classes remain document-wide. If the maintainer intends document-wide BCL-token exclusion,
that is a production Method Flow scope decision outside this allowlist.

### Seventh-repair-pass focused result

`$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path '../SeqDoc-TestProjects').Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpusTests" --logger "console;verbosity=detailed"`
→ **GREEN — Passed: 7, Failed: 0, Skipped: 0** (~1 min). New tests: `MalformedRunIdentityFailsClosed`,
`ArtifactHygieneScannerCatchesInjectedVolatileMarkers`,
`SensitiveCorpusCatchesForbiddenTokensButNotStructuralVocabulary` (3 pure/synthetic, no fixture).
Matrix `[QHTTP-B matrix]` console lines byte-identical to the sixth pass (see values above); shared
repo `git status` + `git worktree list` equal before/after; `HEAD = 7aabfef9…`. Distinct new claims:
malformed run-identity fail-closed + runId capture (G1); direct fixture-path / timestamp hygiene over
all artifacts (G2); explicit forbidden-corpus leak scan with structural split (G3). Final gate
(complete AcceptanceTests Release suite) not run by this pass — left for the orchestrator after one
independent review.

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

## Fifth repair pass — authoritative GitHub review (F1-F7), 2026-09-03

The single authoritative complete-candidate review is the GitHub "Request Changes" review on PR #67.
Earlier contributor-invoked `reviewer-medium` passes (first through fourth repair passes above) are
pre-submission advisory / self-review evidence, not additional authoritative checkpoint reviews. This
pass is ONE batched contributor repair round covering findings F1-F7, applied inside the allowlist
(`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` + this file only). No `src/**` /
production / fixture-project / build / `.github/**` / `docs/project/**` / `checkpoint.md` change.

The owner's frozen-contract amendment (issue #53 comment 5523887517) supersedes the in-place approach:
the lane now materialises FraudManagement `7aabfef9…` in an isolated detached `git worktree` under a
short OS temp path with `core.autocrlf=false` / `core.eol=lf` set BEFORE checkout, analyses ONLY that
normalised checkout, and the amended Program Index fingerprint is
`df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`. Every in-place code path and every
fallback to the shared `Provided/FraudManagement` tree is removed. The historical `f9a36fd5…`
fingerprint and every prior in-place artifact hash are superseded; the entire matrix below is
regenerated from the normalised checkout for BOTH CLI runs.

### Disposition per finding

- **F1 (Critical) — reproducible normalised checkout: FIXED.** `OutboundHttpExternalCorpusFixture`
  resolves the corpus git toplevel from
  `ExternalCorpusResolver.Current.RequireGroup(Provided).Root` → `FraudManagement` →
  `git rev-parse --show-toplevel`, then
  `git worktree add --no-checkout --detach <shortTemp>/fm 7aabfef9…`,
  `git config core.longpaths true|core.autocrlf false|core.eol lf`,
  `sparse-checkout set --no-cone "/*" "!obj/" "!bin/" "!packages/" "!.vs/" "!PackageTmp/"`,
  `git checkout`, `dotnet restore <wt>/FraudManagement.sln`. The CLI runs twice against that worktree
  only. `RunCliAsync` captures the COMPLETE ordered `data.runs[]` identity set (every entry's
  `profileId` + `indexFingerprint` in array order) for BOTH runs; the tests assert
  `run1.IdentitySet.SequenceEqual(run2.IdentitySet)` (not `Assert.Equal` — `ImmutableArray<T>` equality
  is by underlying-array reference, so `SequenceEqual` is required) and that the amended pair
  `(profile:v1:f874be7e…, df23b372…)` is present exactly once; `run[0]` is never silently taken. The
  full normalised candidate artifact matrix (POST md/mmd, GET md/mmd, `index.md`,
  `seqdoc.manifest.json` — byte length + SHA-256 + run1/run2 equality) plus a complete-output digest
  over every file under the output root (including `.seqdoc/**`) is emitted via `ITestOutputHelper` and
  recorded below.
- **F2 (Major) — fail-closed isolation + cleanup: FIXED.** The shared repository tracked + untracked
  `git status --porcelain` and `git worktree list --porcelain` are recorded before the lane and again
  after `CleanupAsync`; Claim 4 asserts exact equality (porcelain lines only; file contents never
  printed). All restore/build/CLI artifacts live inside the detached worktree or an OS temp root. ONE
  cleanup owner (`CleanupAsync`, invoked from `InitializeAsync`'s `finally` and from `DisposeAsync`
  behind a `_cleanedUp` guard) tracks every owned temp root (worktree parent, CLI cache dirs, CLI
  output dirs, temp YAML dir, Mermaid-render dir — each registered via `RegisterOwnedTempRoot` /
  `NewOwnedTempRoot`), removes the detached worktree (`git worktree remove --force` + `git worktree
  prune`, one bounded retry with delay for the known OneDrive `.git/worktrees/<id>` lock), then
  deletes every owned root (bounded retry). A final failure THROWS
  `InvalidOperationException` — no catch-and-ignore of `IOException` / `UnauthorizedAccessException`.
  The production CLI opens the SQLite cache with pooling on, so `CleanupAsync` first calls
  `Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()` (via reflection, no csproj change) +
  `GC.Collect()` / `WaitForPendingFinalizers()` to release the `cache-v1.db` handle before deleting the
  cache roots; without this the deletion loses a genuine handle race (observed once, now fixed). The
  cleanup path runs on both the success and the failure path (the `finally` in `InitializeAsync`).
- **F3 (Major) — no skip in this required lane: FIXED.** Every `SkipException` / `Skip.If` path is
  removed from the fixture and its tests. Missing Provided corpus, missing `FraudManagement`, a
  revision absent from the corpus repo, `git worktree` failure, `dotnet restore` failure, a non-
  `succeeded` CLI outcome, and missing `node`/`npx`/Mermaid-CLI are all `XunitException` /
  `Assert.True(false, …)` with an actionable "QHTTP-B is BLOCKED; this is not a skip" message.
- **F4 (Major) — sensitive-value + single-representation proof: FIXED.** `ReadSensitiveConfigValues`
  scans the normalised checkout's `*.config`, `appsettings*.json`, and `web.config` files in memory
  for `<add key="TCC…" value="…"/>` and `"TCC…": "…"` entries, keeping every non-empty value for keys
  matching BaseAddress / APIKey / Address / Url / Uri / Key / Secret / Password plus the URI host of
  any absolute-URI value. Claim 3 asserts the collected set is non-empty and contains at least one
  BaseAddress and one APIKey field, then asserts every collected value is ABSENT from every generated
  Markdown/Mermaid file; on failure it reports ONLY the field label
  (`sensitive config value for field '<key>' present in '<file>'`) — never the value, never a hash of
  it. The hard-coded request-path checks (`threeThirty/`, `updateType/all`, `complaint/addComplaint`,
  `_baseAddress`, `_APIKey`, `Authorization`, `StringContent`, `ByteArrayContent`,
  `IsSuccessStatusCode`, `HttpResponseMessage`, `ReadAsStringAsync`) are kept. The "literal-count
  proxy" rationale is replaced with direct observables: `AssertNoGenericHttpClientPresentation`
  asserts each named root's flow diagram has NO `participant …HttpClient…` line and NO `->>`/`-->>`
  message carrying `PostAsync`/`GetAsync`, alongside exactly one behaviour phrase, exactly one
  `HTTP boundary` participant, and exactly one `HTTP POST request` / `HTTP GET request` message.
- **F5 (Major) — fail closed on CLI identity + version evidence: FIXED.** Complete ordered
  `data.runs[]` identity comparison as in F1. The resolved SDK/toolchain version is read from the CLI
  `--json` payload at `data.toolchainVersion` (confirmed in `CliHost.CreateAnalyzeData`; `dotnet
  --version` is not consulted) and asserted non-empty and equal across both runs — observed
  `10.0.302`. A non-empty SeqDoc CLI version is read from `SeqDoc.Cli`'s
  `AssemblyInformationalVersionAttribute` — observed `1.0.0+f602665394f0664538a308e34e608b8611859682`.
  The real Mermaid CLI version is captured by
  `npx --yes @mermaid-js/mermaid-cli@11.16.0 --version` and asserted to equal `11.16.0`. Every one of
  these is a hard failure when unavailable — no "unavailable" fallback. CLI stderr is captured for
  BOTH runs, asserted equal, and asserted empty (no approved non-empty baseline is pinned for this
  lane); it is never discarded. The hard failure when
  `data.configuration.diagramBudget.maxMermaidCharacters.value` is absent is kept
  (`MermaidBudgetResolvedFromJson`).
- **F6 (Major) — run the frozen final gate: see "Fifth-repair-pass verification" below.**
- **F7 (Major) — durable record: this section.** All prior in-place matrix values are superseded by
  the normalised-checkout evidence below (both runs). No final-gate or merge-readiness claim is made
  beyond the recorded real results.

### Regenerated normalised-checkout candidate artifact matrix (detached worktree, `core.autocrlf=false` / `core.eol=lf`, `Release/net9.0`)

Profile ID `profile:v1:f874be7e6b51bea2038f6cfac77ab510fc73e7208e9e47b4475e6a17896aaef1`;
Program Index fingerprint (amended) `df23b372a30754898787988bdf0c0537b2bdb19d14b3724c4314588281935117`.
`data.runs[]` identity set run1 == run2 = `[(profile:v1:f874be7e…, df23b372…)]`; amended pair present
exactly once.

| Artifact | run1 len | run1 SHA-256 | run2 len | run2 SHA-256 | equal |
|---|---|---|---|---|---|
| POST md `…addcomplaintrequest-f1cc2038.md` | 3676 | `20293c9d5237691a195bf7901c577794c1ebe1fe93d173ece1c55d93ce572dfe` | 3676 | same | yes |
| POST mmd `…-f1cc2038.mmd` | 353 | `8462128742ac7768fc8c0a075b32399e489b2a3500f917af10ab6dc458fb052b` | 353 | same | yes |
| GET md `…tccservice-lookups-94a25a61.md` | 2850 | `43f4210f413d2c4cf8a2ec883f9abc922f19a1d1e6a150f173d19d8469ddb072` | 2850 | same | yes |
| GET mmd `…-94a25a61.mmd` | 289 | `363e169808a83c3a5df148619e706882922f2321b17e04b25a5ba4b56e039dbd` | 289 | same | yes |
| `index.md` | 2224 | `0de88502807073ea1f77e383c7276b39855ae29962f2950477e6db8d7a2e3d11` | 2224 | same | yes |
| `seqdoc.manifest.json` | 6282 | `b48eb3d7204492bbb9b1d779779d19679d99551c99aa8cdec33ded3b893714c8` | 6282 | same | yes |
| complete-output digest (all files incl. `.seqdoc/**`) | — | `22045be6f4613ce7180cbaac155bfc59cdbf5e864d9a194c43920c3656a0e748` | — | same | yes |

Notes on the delta from the superseded in-place matrix: the POST/GET Markdown + Mermaid bytes and
hashes are unchanged (the merged #54 HTTP boundary content does not depend on the LF/CRLF mix), but
`index.md` (`fca7088b…` → `0de88502…`) and therefore `seqdoc.manifest.json` (`d51d2436…` →
`b48eb3d7…`) change because line-ending normalisation of unrelated flow source shifts other flow-file
content that `index.md` links and the manifest content-hashes. Byte length of `index.md` (2224) and
the manifest (6282) is unchanged.

- Ordered `--json` diagnostic codes: `BE1001, BE2010, BE2010, PRED001` (no `SEQHTTP001`); ordered-
  record digest run1 == run2 = `2f62d6e3193c4926ff6b3a25388b2dc51d275bc716a2d5009c6c18b5d2eae3fc`.
- CLI `data.toolchainVersion` = `10.0.302` (run1 == run2). `SeqDoc.Cli` informational version
  `1.0.0+f602665394f0664538a308e34e608b8611859682`. `mermaid-cli --version` = `11.16.0`.
- Frozen blob SHA-256 (git `show 7aabfef9:<path>`): solution
  `67d6b9f15be05f86c06ea17fa92dd7474b8886b876d1d69cf11552741ffaaca1`, `BLL/BLL.csproj`
  `c38a35ee7b3acf227fb9988ced35dceb2dec36165e8f8f01bb6b82ee6f658a06`, `BLL/TCCIntegration/TCCService.cs`
  `eff261211900578a493d40900cd0de5418dbbd132bbc4f806f684b31e184dfce` — all match frozen.
- Normalised worktree HEAD `7aabfef98fa4d47781bd8a98b9061ddcafb88836`.
- Mermaid budget `45000` (resolved from `--json`); 17 Markdown links resolve; every `.mmd` within
  budget and `MermaidValidator`-clean; real `npx --yes @mermaid-js/mermaid-cli@11.16.0` render of all
  run-1 diagrams = exit 0 + non-empty SVG.
- Shared supplied repository: tracked + untracked `git status --porcelain` and
  `git worktree list --porcelain` byte-identical before and after the lane (incl. after cleanup). The
  pre-existing unrelated `seqdoc-i8-corpus` worktree entry is left untouched. All owned temp roots and
  the detached worktree are removed by `CleanupAsync`; a residual OneDrive lock on
  `.git/worktrees/<id>` (admin dir only, already delisted by `prune`) does not affect
  `git worktree list` equality.

### Fifth-repair-pass verification

- Focused (once):
  `SEQDOC_TEST_PROJECTS_ROOT=<abs>/SeqDoc-TestProjects dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpusTests" --logger "console;verbosity=detailed"`
  → **GREEN — Passed: 4, Failed: 0, Skipped: 0** (~1.7 min; `FrozenIdentityIsolationAndArtifactValidityHold` ~38 s incl. the Mermaid CLI 11.16.0 render).
- Full acceptance gate (F6, once, NO filter):
  `SEQDOC_TEST_PROJECTS_ROOT=<abs>/SeqDoc-TestProjects dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release`
  → **Failed: 6, Passed: 38, Skipped: 0, Total: 44** (4 m 23 s). All 4 `OutboundHttpExternalCorpusTests`
  pass. The 6 failures are pre-existing and unrelated to this checkpoint (this branch only changes one
  acceptance test file + this note):
  - `CorpusMediatRTests.OrderingDraftRouteReachesExactMediatRHandlerWithoutPipelineClaim` —
    `SD1102: MSBuild 10.0.302 is already registered, but this repository selects SDK 10.0.400` (host
    SDK-version mismatch; different-SDK repos need separate processes).
  - `ServiceClientExternalCorpusTests.ConfiguredRootsResolveAndProduceTheAcceptedDocumentSet`,
    `.PositiveLanesRenderTheJoinedOutboundClientMessageExactlyOnce`,
    `.PositiveLaneWordingIsEvidenceBoundedAndCredentialSafe`,
    `.WindowsHostLaneKeepsTheUiWebServiceClientOutOfItsProfile`,
    `.SmsUiWebLaneCompletesEndToEndWithANonFatalWithholdClassBehaviorDiagnostic` — CreditTransfer
    `SD4011` (frozen MethodId in `credit-transfer.yaml` no longer matches the floating external corpus)
    and SMS `SD1101` MSBuild `NuGetAudit` failures (`CoreWCF.Primitives 1.6.0` /
    `Microsoft.AspNetCore.Authentication.Negotiate 9.0.0` known-vulnerability build errors on this host).
- Untouched-`main` comparison (`git worktree` of `origin/main` @ `08cb735`, same command, same
  `SEQDOC_TEST_PROJECTS_ROOT`): **Failed: 24, Passed: 16, Skipped: 0, Total: 40** (2 m 30 s). Every one
  of the 6 failures seen on this branch also fails on untouched `main` (`CorpusMediatRTests.Ordering…`
  and all five `ServiceClientExternalCorpusTests` above). `main` additionally cascades 18 further
  `BehaviorDocumentation*` / `EntityFramework6EdmxProductionTests` / `PersistenceAcceptanceTests` /
  `BehaviorDocumentationGetTests` failures (the `CorpusMediatRTests` `SD1102` MSBuild-registration
  poisoning propagates differently under `main`'s test ordering). This branch has strictly fewer
  acceptance failures than `main`; the checkpoint introduces no regression. The `origin/main` worktree
  was removed and pruned after the run.

Still 4 tests, one expensive fixture (two CLI runs against the normalised worktree). Changed paths for
this repair round: `tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` and this file only
(`docs/work/outbound-http/QHTTP-B/checkpoint.md`'s working-tree `M` is the orchestrator's own state
edit, untouched here).

### Sixth repair pass addendum — IR-2 + IR-1/F1 tightening, 2026-09-03

Two fully-specified assertion additions inside the existing allowlist
(`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` + this file only); no `src/**` /
other test / build / `.github/**` / `docs/project/**` / `checkpoint.md` change.

- **IR-2 — manifest completeness regression restored.** In
  `FrozenIdentityIsolationAndArtifactValidityHold`, after `manifestEntries` is fully populated:
  `Assert.Equal(35, manifestEntries.Count)` (issue #53 frozen matrix: 17 flow `.md` + 17 `.mmd` +
  `index.md`; objective 8 / risk #6 "incomplete manifest") and a frozen byte-length assertion on
  `seqdoc.manifest.json` (`Content.Length == 6282`). Set-equality alone would not catch a silent
  automatic-root drop that shrinks both the manifest and the generated set. Observed on the focused
  lane: entry count = 35, byte length = 6282 (both match; no expected-number edits needed).
- **IR-1/F1 — `data.runs[]` identity-set tightened.** In BOTH
  `OutputIsEvidenceBoundedValueSafeAndDeterministic` and `FrozenIdentityIsolationAndArtifactValidityHold`,
  after the "present exactly once" assertion: `Assert.Single(run1.IdentitySet)` and
  `Assert.Equal(amendedPair, run1.IdentitySet[0])`. Closes the reviewer gap where an extra
  automatic-root run with a different fingerprint would still pass. Observed: `IdentitySet` has exactly
  one entry, equal to the amended `(ProfileId, ProgramIndexFingerprint)` pair.

Focused lane re-run (`--filter FullyQualifiedName~OutboundHttpExternalCorpusTests`, Release):
**Passed: 4, Failed: 0, Skipped: 0** (37 s). Distinct claims added: 2 (manifest completeness/size;
single-entry identity set). Still 4 tests, one expensive fixture.

## Eighth repair pass — independent review of the seventh-pass G1/G2/G3 hardening (F8-F10), 2026-09-04

Bounded repair round, acceptance-only, zero production/semantic change. Edits confined to
`tests/SeqDoc.AcceptanceTests/OutboundHttpExternalCorpusTests.cs` + this file. The frozen artifact
matrix is byte-identical to the seventh pass (POST md 3676 / `20293c9d…`, POST mmd 353 / `84621287…`,
GET md 2850 / `43f4210f…`, GET mmd 289 / `363e1698…`, index.md 2224 / `0de88502…`, manifest 6282 /
`b48eb3d7…`, complete-output digest `22045be6…`, diagnostics digest `2f62d6e3…`, ordered codes
`BE1001, BE2010, BE2010, PRED001`, no `SEQHTTP001`). No new analysis input.

### F8 (Major) — document-wide BCL-token coverage restored

The seventh pass narrowed all ten BCL tokens to boundary-scope and dropped `StringContent`
altogether. Independent review found only three of them genuinely appear document-wide:
`MediaTypeHeaderValue`, `SerializeObject`, `DeserializeObject` — rendered as Method Flow step names of
`new MediaTypeHeaderValue("application/json")`, `JsonConvert.SerializeObject(request)`,
`JsonConvert.DeserializeObject<…>` in `BLL/TCCIntegration/TCCService.cs` (`AddComplaint` / `Lookups`
step list). The other seven, plus `StringContent`, were green document-wide in every prior pass;
narrowing them was an unjustified weakening against checkpoint Risk 4 / the non-goal of no
status/success/response claims.

Applied:
- `StringContent` was briefly dropped from the corpus in the seventh pass and is now **restored**.
- `OutboundHttpSensitiveCorpus.Build` now emits two kinds:
  - `Kind == "bcl-token"` (boundary-scoped, 3 tokens): `MediaTypeHeaderValue`, `SerializeObject`,
    `DeserializeObject`.
  - `Kind == "bcl-response-token"` (document-wide, 8 tokens): `ByteArrayContent`,
    `HttpResponseMessage`, `IsSuccessStatusCode`, `ReadAsStringAsync`, `StringContent`,
    `HttpClientHandler`, `ServerCertificateCustomValidationCallback`, `MediaTypeWithQualityHeaderValue`.
- `OutputIsEvidenceBoundedValueSafeAndDeterministic`: `docWideCorpus =
  forbiddenCorpus.Where(e => e.Kind != "bcl-token")` now covers `bcl-response-token` too;
  `boundaryScopedCorpus` stays `Kind == "bcl-token"` (the 3). All 8 document-wide BCL response tokens
  are expected absent from every generated artifact — focused lane stays green.
- `SensitiveCorpusCatchesForbiddenTokensButNotStructuralVocabulary` updated: the synthetic leak string
  now also carries `HttpResponseMessage.IsSuccessStatusCode` + `JsonConvert.SerializeObject`, and the
  test asserts both `bcl-response-token` and `bcl-token` kinds are caught, that `StringContent` is a
  `bcl-response-token` corpus entry, and that `HttpResponseMessage` is not a `bcl-token`.

Evidence / non-removability: the three boundary-scoped tokens are BCL **type/method names in the
Method Flow step list** of `TCCService.AddComplaint` / `Lookups` — not URI/credential/status/success/
outcome claims, and not the outbound-HTTP boundary vocabulary. They are not removable document-wide
without a forbidden production Method Flow change (and that would drift the frozen artifact matrix).
The acceptance claim for them is only that they never become the boundary line / Mermaid message
vocabulary (same scoping precedent as `boundaryForbiddenWords` and `ServiceClientExternalCorpusTests`).

Escalation for the maintainer: the Method Flow step list for `TCCService.AddComplaint` / `Lookups`
surfaces those BCL type/method names in the same generated document as the HTTP boundary presentation.
If document-wide BCL-token exclusion is intended, that is a production Method Flow scope decision
outside this acceptance allowlist.

### F9 (Minor) — fail closed with the actionable message when `data` is absent

Seventh-pass `RunCliAsync` only called `OutboundHttpRunIdentity.ParseRuns` inside
`if (root.TryGetProperty("data", …) && data.ValueKind == Object)`, so a response with a missing or
non-object `data` left `identitySet` empty and the lane failed later on a bare `Assert.NotEmpty`
instead of the named `XunitException`. Fixed: `ParseRuns` now runs **unconditionally** —
`ParseRuns(hasData ? data : root)` — so a missing/non-object `data` (root object has no `runs`) throws
`"CLI --json 'data.runs' is absent; run identity cannot be verified fail-closed."`. The
`availableTargetFrameworks` / `toolchainVersion` reads keep their own `hasData` guard.
`MalformedRunIdentityFailsClosed` gains a local `ParseData` helper mirroring the fixture path and three
cases: top-level object with no `data`; `data` object with no `runs`; `data` that is not an object —
each asserted to carry `'data.runs' is absent`.

### F10 (Observation) — hygiene-detector CI/revision coupling recorded

The artifact-hygiene detectors (`Environment.UserName` / `MachineName` / `UserDomainName` substring
match, the ISO-8601 date regex, and the document-wide `Authorization` token) are tuned to the current
CI account and FraudManagement revision `7aabfef98fa4d47781bd8a98b9061ddcafb88836`; a different CI
account name or a future corpus revision may require revisiting them. Notes-only, no code change.

### Eighth-repair-pass focused result

`$env:SEQDOC_TEST_PROJECTS_ROOT = (Resolve-Path '../SeqDoc-TestProjects').Path; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~OutboundHttpExternalCorpusTests" --logger "console;verbosity=detailed"`
→ **GREEN — Passed: 7, Failed: 0, Skipped: 0** (1.60 min;
`FrozenIdentityIsolationAndArtifactValidityHold` ~35 s incl. the Mermaid CLI 11.16.0 render). All
`[QHTTP-B matrix]` console lines byte-identical to the seventh pass (POST md 3676 / `20293c9d…`,
POST mmd 353 / `84621287…`, GET md 2850 / `43f4210f…`, GET mmd 289 / `363e1698…`, index.md 2224 /
`0de88502…`, manifest 6282 / `b48eb3d7…`, complete-output digest `22045be6…`, diagnostics digest
`2f62d6e3…`, codes `BE1001, BE2010, BE2010, PRED001`); shared repo `git status` + `git worktree list`
equal before/after; `HEAD = 7aabfef9…`. Distinct claims added/consolidated: 2 (document-wide
`bcl-response-token` absence incl. restored `StringContent`; unconditional fail-closed run-identity
parse with the named message). Final gate not run by this pass — left for the orchestrator after one
independent review.
