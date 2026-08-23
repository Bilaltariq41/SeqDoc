## Linked work

Closes #
Parent workstream: #

## Problem and design

Describe the evidence-backed problem, accepted design, non-goals, and important trade-offs.

## Changed paths

- Approved target paths:
- Production:
- Tests/fixtures:
- Documentation (only when explicitly assigned):
- Unexpected changed paths and maintainer approval links:

## Semantic admission table

Complete this section for compiler, framework, persistence, worker, or IR semantics. Otherwise write `Not applicable`.

| Item | Accepted evidence |
|---|---|
| Roslyn operation shape | |
| Exact type/member/assembly and supported overloads | |
| Registration or scenario admission proof | |
| Argument/callback mapping | |
| Recognized but unsupported forms | |
| Same-shaped negative | |
| First observable consumer | |
| Profile/snapshot and control-placement proof | |

## Acceptance evidence

| Acceptance criterion | Named test | Layer | Negative or boundary |
|---|---|---|---|
| | | | |

## Risk and self-review

- [ ] I re-read the issue and met every acceptance criterion.
- [ ] I inspected the full diff from `main` and removed unrelated changes.
- [ ] I checked evidence/certainty, profile isolation, deterministic ordering, and conservative boundaries.
- [ ] I added risk-based positive and negative tests without duplicate assertions.
- [ ] Every new semantic fixture is consumed through its production extractor.
- [ ] Hand-built facts are backed by a separate producer test where the producer changed.
- [ ] Every unexpected path has linked maintainer approval.
- [ ] I committed no external source, secrets, local paths, caches, generated output, or build artifacts.
- [ ] Generated Mermaid was actually rendered when diagram layout changed.

## Verification

Focused command and result:

```text

```

Final gate and result:

```text

```

External acceptance/output inspected:

## Remaining boundaries

List honest unsupported behavior, unavailable external lanes, or follow-up issues.
