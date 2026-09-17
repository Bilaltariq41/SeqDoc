# Repository project operations

The registry in `docs/project/work-items/` is authoritative. `execution.json`, checkpoint state, and GitHub labels are
derived views. A fresh clone reconstructs state with `validate`, then `project-execution`; operators must not hand-edit
those projections. The governance tool accepts only repository-relative claims, normalizes slash style, dot segments,
and Windows case, and rejects ancestor overlap and absolute paths.

## Authority and permissions

| Operation | Write permission | Source of truth |
|---|---|---|
| `prepare`, `activate`, `handoff`, `closeout`, `promote`, `recover` | assigned Write collaborator on the branch | registry and capsule |
| `validate`, `project-execution --check`, `check-github` | read-only | registry / observed remote |
| `project` and `sync-github` | maintainer-approved issue-label permission | registry, then remote |
| T4 access/settings, rulesets, secrets, apps, visibility, transfer, archive/delete, or bypass | owner only | GitHub controls |

Branch protection remains outside this tool. GitHub projection is secondary, preserves unrelated labels, and must fail
loudly on permission errors, malformed remote data, drift, or partial writes. Tokens, local paths, and session exports
are never packets or comments.

## Fresh-clone command sequence

```text
python -B tools/governance/work_state.py validate --root .
python -B tools/governance/work_state.py project-execution --root . --check
python -B tools/governance/work_state.py prepare --root . --id GH-57
python -B tools/governance/work_state.py activate --root . --id ITEM --execution-id EXEC --expected-baseline SHA --current-head SHA --current-branch BRANCH --clean --worktree-id WORKTREE --claim path/to/file --dry-run
python -B tools/governance/work_state.py handoff --root . --id ITEM --execution-id EXEC --pr PR_URL --head SHA --peer PEER --epoch EPOCH --finding "Fixed: receipt"
python -B tools/governance/work_state.py closeout --root . --id ITEM --execution-id EXEC --pr PR_URL --head SHA --peer PEER --findings resolved --focused-receipt FOCUSED --final-receipt FINAL --attribution AUTHOR --merge-sha SHA
```

`activate` observes Git directly whenever the root is a checkout and compares supplied values as expectations. Synthetic
registries must supply equivalent observed evidence. `prepare --scaffold` derives `docs/work/checkpoints/<id>` only
when identity is absent, or accepts a relative `--checkpoint-id`/`--checkpoint-path`; it rejects absolute, parent, and
root-level capsule paths and writes the record and placeholder atomically.

## Readiness, claims, and activation

`prepare --id ITEM` validates one complete canonical capsule: objective, targets, non-goals, risks, existing coverage,
budget, focused and final commands, review boundary, and acceptance proof. Unknown evidence is a blocking placeholder;
it is not silently inferred. `activate` requires an eligible item, closed dependencies, exact frozen baseline, observed
HEAD/branch/clean worktree identity, a stable execution and worktree ID, and normalized typed claims. Use `--dry-run`
first. Claims may cover fixtures, governance tools, and exclusive resources; equal exclusive claims conflict.

Each operation validates the complete candidate before writing. Payloads are sorted and journaled with a deterministic
generation identity. In-process failures restore every replaced file. `recover` never overwrites a newer generation;
when evidence is insufficient it refuses and tells the operator to inspect the journal and rerun or restore from version
control.

## Parallel work and review

Execution instances are independent records in the sorted `executions` projection. Legacy root selection fields remain
for compatibility. Disjoint claims may run concurrently; closeout releases only its instance. `handoff` requires the
authenticated current PR author, current head, non-author peer, and a review epoch; stale SHA and author-as-peer are
rejected. Findings are sorted and must receive deterministic dispositions before closure.

`handoff` invokes authenticated `gh pr view` internally and requires an open, non-draft PR, current head, observed author, and a non-author peer. The first handoff from `Active` uses epoch 1; a repair handoff from `ResolvingFindings` requires a larger integer epoch, an advanced PR head, the same peer (or explicit `--allow-peer-change`), and complete dispositions stored in sorted order. It replaces the authenticated `requestHead` boundary and returns the capsule to `ReviewRequired`. Observed caller fields are test seams only; handoff requests review and does not claim approval.

`closeout` invokes authenticated PR and paginated review observations, requiring a merged PR, its actual final head and merge SHA, and an exact-final-head `APPROVED` review by the stored peer. The handoff request head is retained only as a historical boundary; the caller's closeout head must match the authenticated final head. It requires matching execution/PR identity, focused and final receipts, attribution, resolved findings,
and review evidence. It closes atomically and either leaves the root idle or selects an already-complete recipient.
`promote` changes only a blocked or draft dependent to `Ready` after every dependency is `Closed` and its capsule is
complete; it never activates or selects the dependent.

## Recovery, cancellation, and projection

Cancellation uses the legal `transition` operation and follows the same journaled transaction. An interrupted process
leaves its untracked worktree-local journal for `recover`; a successfully rolled-back operation removes it. Run
`project --dry-run` to show exact bounded label commands; apply only
with the required permission. The workflow validates on pull requests and synchronizes labels only after a protected
push to `main`; it never mutates pull-request heads.

Review identities and attribution are supplied from authenticated observed data, not PR text. Periodically review the
Write collaborator list, GitHub token scopes, branch protection, and owner-only T4 access; remove unused access and
confirm that projection credentials remain least-privilege.

`project --dry-run` first reads issue state, lifecycle labels, and comments. A state mismatch aborts before any write.
Marker comments use an exact `seqdoc-state-v1:<item>:<lifecycle>` marker; start and closure packets are distinct and only an exact duplicate is suppressed. Unrelated labels are never
removed. `sync-github` retains lifecycle-label creation and update behavior. Neither operation changes registry files or
PR heads. `recover` accepts only a fully validated relative-path journal whose current bytes match an original or target
hash; malformed, outside-root, newer, or partially described journals remain untouched and require manual inspection.
