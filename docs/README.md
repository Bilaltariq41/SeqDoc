# SeqDoc Documentation

This page orients a new development session without relying on conversation history.

## Product authority

- `README.md` describes the product and entry commands.
- `docs/architecture.md` describes the compiler-evidence pipeline.
- `docs/decisions.md` records accepted architectural constraints.
- `docs/roadmap.md` is the ordered capability direction.
- Historical topology: `docs/project/parallel-workstreams.md` (not current authority).
- `docs/project/completion-roadmap.md` is the staged v1 strategy; its visual companion is the plain-English overview.
- `docs/project/completion-issue-map.md` contains the detailed future plan; its IDs are planning-only until registered.
- `docs/project/collaboration-model.md` defines decision rights, leases, receipts, review, and continuation.
- `docs/project/testing-policy.md` defines risk-based test selection; `docs/project/test-performance.md` records measurements.
- `docs/project/issue-readiness.md` defines the frozen issue contract.
- `docs/usage.md` contains reproducible CLI and external-corpus setup.

The non-negotiable invariants are static-analysis authority, explicit evidence and certainty, profile and target-
framework separation, deterministic output, typed Program Index → Method Flow → Scenario Graph → Diagram Plan
boundaries, and preservation of the previous valid state after failed analysis.

## Execution authority

`docs/project/work-items/` is the sole typed current-state authority. Read the applicable record before GitHub or any
projection. GitHub labels, `execution.json`, status, parallel topology, and checkpoint state are projections or history.
Use the governance `transition` command for lifecycle changes.

Repository-native preparation, activation, transactional recovery, review handoff, closeout, promotion, and bounded
GitHub projection are documented in [`docs/project/project-operations.md`](project/project-operations.md).

Read the selected record in `docs/project/work-items/`, then `docs/project/status.md`, `docs/project/workflow.md`, and `docs/project/execution.json` at session start.
`execution.json` identifies the selected active checkpoint when one exists. When it is idle, the root Orchestrator may
not delegate product work without owner activation; contributors may claim eligible `Ready` items, establish or update
their capsule and local canonical state on a branch, and begin implementation.

Raw agent session exports are local recovery artifacts and are not repository authority. Durable decisions and
the restart position are recorded in `docs/project/` so a fresh session can resume safely.
