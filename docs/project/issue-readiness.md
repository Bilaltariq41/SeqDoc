# Issue readiness policy

The typed record under `docs/project/work-items/` is the sole current-state authority. Issue bodies remain the
specification authority. GitHub labels, `execution.json`, status, parallel topology, and checkpoint state are generated
projections or historical explanation; none is manual lifecycle authority.

## Readiness audit

An eligible unassigned contributor may claim the item, record and freeze the baseline and checkpoint capsule on their
branch, and run the readiness/self-review agent. No separate readiness PR or human approval is required to start.
Open dependencies, missing decisions, or incomplete proof keep the record `Blocked`; otherwise a `Ready` record is
contributor-claimable and permits implementation from the frozen baseline. `Active` means work has started. Only the
selected record authorizes the local Orchestrator.

The audit must preserve the applicable proof below:

* **Semantic:** complete the semantic-delivery brief with exact operation/type identity, assembly and overload
  admission, registration and callback mapping, supported and unsupported forms, negative lookalikes, evidence chain,
  joins, certainty, placement, and the first user-visible or persisted consumer. Establish a clean-baseline red producer
  signature and candidate green observable assertion.
* **Acceptance-only:** link the frozen semantic contract and name the exact external revision, profile, configuration,
   root, baseline diagnostic/artifact, candidate artifact matrix, hashes or links, Mermaid output, and repeat-byte
  evidence. This substitutes for initial producer proof. If it exposes a same-outcome semantic or contract defect, the
  worker records the reclassification/amendment, adds the necessary producer/boundary proof and tests, and repairs it in
  the same issue. A genuinely independent deliverable becomes a new issue.
* **Mechanical:** state the exact invariant and allowed paths, provide a reproducible defect or explicit no-behavior-
  change evidence, and name focused verification. No semantic brief is required unless semantics change.

## Assignment, freeze, and amendment

Assignment freezes the contract revision, baseline, dependencies, allowlist, negatives, and proof rows as the starting
plan. The worker may amend the existing issue/checkpoint and paths with anything reasonably needed for its outcome,
including production, tests, fixtures, docs, config, scripts, blocking defects, refactors, and contract adjustments.
Record the reason and affected risks, run self-review and the Reviewer agent, and continue. Human preapproval is needed
only for a genuinely separate capability/outcome, a conflicting lease/active worker, or T4 owner administration.

## Complete delivery and review

The smallest acceptable unit is a complete producer-to-observable vertical slice. Real production input must reach the
typed producer and its first observable assertion; hand-built intermediate facts are supplementary, not completion
evidence. Claims may only preserve or weaken compiler evidence, and identity, profile, snapshot, chronology, guards,
boundaries, and deterministic ordering remain explicit.

Use the review and continuation rules in [collaboration-model.md](collaboration-model.md). DGP1 and grandfathered
I13/P17-R1/QHTTP-B retain their frozen one-review rules. New work gets one latest-head non-author human peer approval;
agent readiness and self-review are not additional human approvals. Continue repairing on the same issue/PR; block only
for an external dependency, conflicting active work, or T4 action.

## Status transitions and acceptance boundary

The lifecycle is `Draft` → `Blocked` → `Ready` → `Active` → `ReviewRequired` → `ResolvingFindings` → `Verifying` →
`Closed`, with only explicitly legal transitions and `Cancelled`/`Blocked` stop paths. Use
`tools/governance/work_state.py transition`; do not hand-edit projections. Lifecycle labels are remote projections and
generated execution is a local projection.

When closing or otherwise making the selected item unselectable, use `transition --id <target> --state <state>` to leave
the Orchestrator idle, or add `--select-id <existing-item>` for an atomic handoff. The recipient must already be
selectable and complete; do not hand-edit selection or lifecycle fields.

Acceptance-only work proves its contract against an exact external checkout and configuration. If that evidence exposes a
same-outcome semantic or contract defect, record the reclassification/amendment, add producer/boundary proof and tests,
and repair it in the same issue/checkpoint. Only a genuinely independent deliverable becomes a new issue. Closure
records the baseline, exact commands/results, observable or invariant evidence, remaining boundaries, recorded scope
changes, and passing declared gates.
