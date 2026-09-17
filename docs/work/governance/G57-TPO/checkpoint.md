# G57-TPO checkpoint

## State

`ResolvingFindings`

## Authority

- Issue: https://github.com/Bilaltariq41/SeqDoc/issues/57
- Contract revision: current Issue #57 body at claim receipt
  https://github.com/Bilaltariq41/SeqDoc/issues/57#issuecomment-5711611308
- Baseline: `ab6e3e1cf16213ee5346506b16949fa32c4ddfa4`
- Branch: `feature/issue-57-transactional-project-operations`
- Owner: `ahmad`

## Objective

Provide deterministic, transactional, repository-native commands for checkpoint preparation, activation, review handoff,
closeout, dependent promotion, recovery, compatible parallel execution, and bounded GitHub projection. A Write
collaborator must be able to reconstruct and operate the workflow from a fresh clone without private notes or local
session history.

## Target paths

- `tools/governance/work_state.py`
- `tests/governance/test_work_state.py`
- `docs/project/work-state.schema.json`
- `.github/workflows/work-state.yml`
- `AGENTS.md`
- `docs/README.md`
- `docs/project/workflow.md`
- `docs/project/issue-readiness.md`
- `docs/project/delegated-contribution-workflow.md`
- `docs/project/project-operations.md`
- `docs/project/work-items/GH-57.json`
- generated `docs/project/execution.json`
- `docs/work/governance/G57-TPO/checkpoint.md`
- `docs/work/governance/G57-TPO/ledger.md`

No other path may change without a recorded T2 amendment and an updated risk assessment.

## Path and resource claims

This checkpoint owns the target paths above on its branch. Current `main` has no selected execution. Preserved PRs
#99, #108, and #109 overlap with execution or workflow records but are blocked or superseded and hold no selected
canonical lease on the frozen baseline. Their branches, commits, evidence, and attribution remain untouched.

Exclusive resources are the canonical work-state registry, execution projection, governance tool, and lifecycle-label
projection command. Tests must simulate GitHub writes and must not mutate live issues.

## Non-goals

- Product behavior or any file under `src/**`.
- External-project source, fixtures, generated output, or caches.
- SDK, package, or build-system selection outside the named workflow.
- OpenCode configuration.
- GitHub access, settings, rulesets, secrets, apps, branch protection, or bypass.
- Rewriting or repairing PRs #99, #108, or #109.
- Automatic activation of promoted dependents.
- New product or compiler semantics.

## Risk inventory

- A failed multi-file write may leave records, capsules, and projections inconsistent.
- A stale baseline or changed contract may activate work against the wrong authority.
- Path normalization may differ by platform or permit overlapping claims to appear distinct.
- Multiple sessions may claim the same path, fixture, governance tool, or exclusive resource.
- Migration from singular execution state may lose the current selected item or break existing commands.
- Handoff or closeout may accept the wrong PR, commit, reviewer, or verification receipt.
- Dependent promotion may activate work before its own readiness is complete.
- GitHub projection may drift, partially fail, or exceed collaborator permissions.
- Packets may depend on timestamps, machine paths, iteration order, credentials, or raw session data.
- Recovery may overwrite a newer valid state or fail to explain the smallest safe next action.

## Existing coverage

`tests/governance/test_work_state.py` contains 15 tests for schema validation, dependency cycles, frozen baseline shape,
singular selection, transition legality, execution projection, dry-run label commands, basic multi-file rollback,
selection handoff, idle deselection, and lifecycle-label projection. It does not model execution instances, path or
resource claims, checkpoint scaffolding, stale Git baselines, deterministic operation packets, interrupted recovery,
review identity, full closeout, or dependent promotion.

## Test budget

Add or materially rewrite about 20 to 28 grouped governance claims. The budget exceeds the normal 15-claim guideline
because Issue #57 explicitly requires separate activation, transaction, concurrency, recovery, projection, and closeout
failure modes. Use complete synthetic registries and capsules. Keep one read-only assertion for the real registry and
execution projection.

## Focused verification

```powershell
python -B -m unittest tests.governance.test_work_state
```

## Final gate

```powershell
python -B tools/governance/work_state.py validate --root .; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B tools/governance/work_state.py project-execution --root . --check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; python -B -m unittest tests.governance.test_work_state; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; git diff --check
```

## Review boundary

The implementer runs the focused command, inspects the complete diff, and runs one complete-candidate Reviewer pass.
Every finding must be fixed, rejected with evidence, or deferred with explicit approval. A latest-head non-author human
peer then reviews the same SHA. The Gate Runner runs the final gate once after all findings are resolved.

## Acceptance proof

Synthetic tests must cover successful activation; incomplete capsules; unresolved dependencies; stale baselines;
selection, path, fixture, and governance-tool conflicts; dry-run nonmutation; deterministic packets; rollback and
recovery; idempotent projection; closeout identity mismatch; legal review handoff; dependent promotion and
non-promotion; idle and atomic-recipient closeout; compatible concurrent executions; and the real registry's current
projection. Documentation must give a fresh-clone operator enough commands and authority rules to complete the same
workflow without private state.

## Stop conditions

Stop for a real external dependency, an active conflicting path or exclusive-resource lease, or an owner-only T4
operation. Do not weaken transaction, recovery, deterministic-output, or review-identity tests to finish the issue.
