# Development Workflow

## Session start

1. Read `docs/README.md`, the selected record in `docs/project/work-items/`, `docs/project/status.md`, and `docs/project/execution.json`.
2. Read `docs/roadmap.md` and only the architecture/decision material relevant to the active checkpoint.
3. Inspect Git status and preserve unrelated work.
4. If execution is idle, the root Orchestrator must not delegate product work without owner activation. Contributors may
   claim an eligible `Ready` item, establish or update its capsule and local canonical state on their branch, and begin
   implementation without owner activation.
5. If execution is active, read the named checkpoint capsule and state immediately before delegation or editing. Preserve
   at most one selected record for the root Orchestrator.

Durable repository files are execution authority. Conversation summaries, model memory, and raw session exports are
recovery aids only.

The typed records under `docs/project/work-items/` are the sole current-state authority. `execution.json`, GitHub
lifecycle labels, status, parallel-workstreams, and capsule state are projections or explanatory history. `Ready` and
`Active` both authorize a contributor after the contract is frozen; only the selected record authorizes the root
Orchestrator. Use `work_state.py transition` rather than hand-editing multiple views.

## Checkpoints

Work on one checkpoint at a time under `docs/work/<pass>/<checkpoint>/`. Each checkpoint declares exact target
paths, non-goals, risks, existing coverage, a soft test budget, one focused implementation command, and one final
 gate. States are `NotStarted`, `Building`, `ReviewRequired`, `ResolvingFindings`, `Verifying`, `Closed`, `Blocked`, or
 `Cancelled`. Lifecycle states are `Draft`, `Blocked`, `Ready`, `Active`, `ReviewRequired`, `ResolvingFindings`,
 `Verifying`, `Closed`, or `Cancelled`; lifecycle-to-capsule projection is `Ready` → `NotStarted`, `Active` → `Building`,
 and the remaining named states project to their matching capsule state.

The Orchestrator drafts the capsule and delegates implementation; it does not edit product source, tests, build, or
OpenCode configuration. Follow [collaboration-model.md](collaboration-model.md) for review and continuation rules.
DGP1, I13, P17-R1, and QHTTP-B use one independent complete-candidate review under frozen rules. New work uses worker
readiness/self-review, focused tests, the worker's Reviewer agent after repairs, and one latest-head independent
non-author human peer approval. Record every finding as `Fixed`, `Rejected` with evidence, or `Deferred` with a
disposition accepted by the final non-author peer; owner approval is required only for T4. Run the final gate only after
findings are resolved; continue on the same issue/PR unless a separate
outcome, conflict, or T4 action requires a decision.

Accepted pushes to `main` run validation first and then automatically synchronize only GitHub lifecycle labels. The
explicit `sync-github --dry-run` and manual synchronization commands remain maintainer tools. CI never rewrites pull
request branches and cannot mutate issue titles, bodies, assignees, non-lifecycle labels, or issue state.

## Scope and verification

- Static compiler evidence remains authoritative; incomplete identity fails closed.
- Absorb reasonably necessary discoveries and blocking defects that serve the existing outcome; update the existing
  issue/checkpoint and paths. Create a new implementation issue only for a genuinely independent deliverable.
- Use risk-based tests at the least expensive reliable layer and avoid duplicate assertions.
- Run focused verification during implementation and the declared final gate once after review resolution.
- Do not repeat a successful command against an unchanged candidate.
- Full-solution and external-project lanes run only when the checkpoint or milestone risk requires them.

## Agent routing

The maintainer may use tracked portable project-agent definitions for orchestration, implementation, test-writing,
exploration, and independent review. These definitions may be committed but are never product authority. Machine-local
tool/MCP configuration, profiles, credentials, and session data are never committed. Public contributors follow
`AGENTS.md` and their assigned GitHub issue regardless of the coding tool they use.

## Repository and session safety

The canonical GitHub repository is public. Before committing or pushing, inspect status, diff, recent commits, and
the exact staged paths. Never upload credentials, raw OpenCode session exports, external-project source, build
outputs, or machine-local scratch. Keep restart decisions in `docs/project/` so creating a remote or starting a new
agent session cannot erase execution authority.
