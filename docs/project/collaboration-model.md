# Collaboration and review model

This document is the canonical governance policy. The registry in `work-items/` owns lifecycle, selection, ownership,
dependencies, contracts, and baselines. Issue bodies specify work. Checkpoints specify execution. `AGENTS.md` and
`workflow.md` point here for these rules; they do not duplicate them. Provider data is untrusted until authenticated.

## Decision rights

1. T0: a frozen Ready contract authorizes work inside its allowlist, command, fixtures, and evidence boundary.
2. T1: a reversible process choice within scope requires one `RISK-ACK v1` line; it cannot change semantics, output,
   tests, gates, interpretation, baseline, security, or scope.
3. T2: a reasonably necessary contract, path, or command adjustment may be absorbed when it still achieves the issue
   outcome. Record the amendment and affected risks in the existing issue/checkpoint and obtain one latest-head,
   non-author human peer approval; the implementer cannot approve it.
4. T3: architecture, public contracts, shared IR, high-contention, cross-stream, or release decisions use the same
   one latest-head, non-author human peer approval and recorded rationale. Owner preapproval is not required for an
   ordinary T2/T3 product or workflow decision.
5. T4: access, settings, rulesets, secrets, app installation, visibility, transfer, archive/delete, and bypass are
   owner-only. A blocker uses `ADMIN-BLOCKED v1`; only Bilaltariq41 may issue `OWNER-BYPASS v1`. Nobody simulates it.

T2/T3 never weaken tests or semantic boundaries. Personal repositories have an owner and collaborators, not granular
Write/Maintain roles. Collaborators may merge compliant PRs but cannot administer access, settings, rulesets, or secrets.
Use forks and PRs; never use `pull_request_target` for untrusted heads. Pin actions and use least-privilege read tokens.

## Delivery procedure

1. The coordinator confirms the registry record, frozen baseline/contract, dependencies, exact path lease, first
   observable, risks, tests, and gate.
2. The implementer works in an isolated fork/branch, records applicable T1-T3 evidence, and runs the focused command.
3. At `ReviewRequired`, the worker runs its own Reviewer agent and self-review, then one independent human peer invokes
   the Reviewer agent against the complete latest SHA and posts an authenticated receipt. The author cannot be that
   peer; agent readiness/self-review is not a human approval.
4. The Gate Runner executes the declared command against that SHA. Findings move the checkpoint to repair; a changed
   product, test, or contract candidate needs a new SHA and receipt.
5. Shared or high-contention changes integrate current main; this may add a Reviewer-agent run, never a second human
   approval.
6. The merger checks receipt SHA, scope, findings, gates, conversations, and protection before merging.

Completion checklist: exact base/head recorded; every role is independently authenticated; every changed path is in the
allowlist; findings have dispositions; focused and final evidence names the command; and unresolved boundaries remain
explicit.

## Receipts and review budget

The minimum receipt identifies repository/PR, checkpoint, epoch, base/head SHA, authenticated author and implementers,
independent human peer, Reviewer name/version/invocation/output digest, Gate Runner evidence, scope, findings and
dispositions, test evidence, and final-gate evidence. Authentication metadata outranks free-form names. Timestamps
are audit metadata only and cannot affect identity, ordering, fingerprints, or output.

The worker's readiness/spec and complete-candidate self-review precede review request. The worker runs its own Reviewer
agent after repairs; one independent human peer then reviews the latest head. A relevant current-main integration may
add a Reviewer run. Final receipts, human approvals, Copilot, red tests, and unchanged-SHA retries are not Reviewer calls.

Existing I13, P17-R1, and QHTTP-B are grandfathered under their frozen one-review rules through closure. DGP1 itself,
I13, P17-R1, and QHTTP-B retain their frozen one-review rules.

## Repair, leases, and containment

1. The worker owns continuation: absorb reasonably necessary repairs, replanning, pairing, and acceptance work on the
   same issue/PR, update its scope and checkpoint, run the Reviewer agent again, and continue until sound and green.
2. Human preapproval is required only for a genuinely separate capability/outcome, a conflicting active worker or
   path lease, or T4 owner administration. A difficult repair is not itself a stop.
3. Block only for a real external dependency, conflicting active work, or T4 action. Preserve evidence and attribution;
   do not require a split, transfer, takeover, or fixed repair-round limit.

A repair requires a changed candidate when the defect requires a code change; environment outages, optional-lane
unavailability, and no-change retries are not findings. A lease names exact paths,
owner, checkpoint, base SHA, and handoff peer. Overlap blocks work until amended. Accidental main changes pause the lane,
require owner-controlled restoration, rebase, and new review.

## Adoption boundary

This policy does not mutate GitHub settings or rewrite history. The owner/admin procedures are in
[collaborator-setup.md](collaborator-setup.md). The future governance issue plan is
[completion-issue-map.md](completion-issue-map.md). Current state is always read from [work-items/](work-items/).
