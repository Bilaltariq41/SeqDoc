## Linked work

Confirm the linked item's canonical record under `docs/project/work-items/` before relying on GitHub labels or issue state.
Record ID and checkpoint ID/path:
Worker/coordinator: transition the canonical record with `work_state.py`; do not hand-edit projections.
Local planning ID, if applicable:
Decision tier and exact path lease:
Baseline SHA / candidate head SHA:

Closes #
Parent workstream: #

## Problem and design

Describe the evidence-backed problem, accepted design, non-goals, and important trade-offs.

## Changed paths

- Approved target paths:
- Production:
- Tests/fixtures:
- Documentation reasonably needed for the existing outcome:
- Additional paths and reason they are needed for the existing outcome:

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
- [ ] I recorded necessary scope changes in the existing issue/checkpoint; separate outcomes, lease conflicts, and T4
      actions have the required decision.
- [ ] I committed no external source, secrets, local paths, caches, generated output, or build artifacts.
- [ ] Generated Mermaid was actually rendered when diagram layout changed.

## Verification

Focused command and result:

```text

```

Reviewer receipt and first observable:

One latest-head non-author human peer approval (agent self-review is not approval):

Reserved reviewer at claim/readiness and eligibility replacement record:

Final gate and result:

```text

```

External acceptance/output inspected:

## Remaining boundaries

List honest unsupported behavior, unavailable external lanes, or follow-up issues.

## Copilot pre-review routing

- GitHub MCP issue/owner context retrieved, or Spec axis explicitly marked incomplete and review not clean:
- Latest-head Copilot findings and focused verification:
- Review-policy files changed? Copilot remains untrusted supplemental evidence:
