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
| First observable or persisted consumer (not an intermediate fact) | |
| Profile/snapshot and control-placement proof | |

## Acceptance evidence

| Acceptance criterion | Named test | Layer | Negative or boundary |
|---|---|---|---|
| | | | |

## Repair trace

Complete when responding to review findings. Otherwise write `Not applicable`.

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| | | | | |

## Risk and self-review

- [ ] I re-read the issue and met every acceptance criterion.
- [ ] I inspected the full diff from `main` and removed unrelated changes.
- [ ] For semantic work, I completed the `AGENTS.md` proof gates and testing-policy proofs, or marked them not applicable.
- [ ] After repairs, I re-reviewed the complete candidate and completed the repair trace, or marked it not applicable.
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

## Copilot pre-review routing

- GitHub MCP issue/owner context retrieved, or Spec axis explicitly marked incomplete and review not clean:
- Latest-head Copilot findings and focused verification:
- Review-policy files changed? Require explicit owner review; Copilot is untrusted:
