# The plan, in plain English

This is the plain-English companion to [the canonical roadmap](completion-roadmap.md). Lifecycle and assignments live in
the [work-item registry](work-items/); this visual is a readable orientation, not a second ledger.

The basic idea: Ahmad, Qais, and Abood work in separate lanes. Finished work is checked before later work can use it. The lanes meet only during integration.

## Starting point and firm lanes

The starting check feeds three separate firm lanes. There are no cross-lane arrows.

```
[CRITICAL, UNASSIGNED] Q starting baseline / quality check
                         |
               Shared checked starting point
                         |
                         +-- Ahmad: P-1 -> P-2 -> P-3 -> P-4 -> P-5
                         +-- Qais:  S-1 -> S-2 -> S-3 -> S-4
                         +-- Abood: C-1 -> C-2 -> C-3 -> C-4 -> C-5 -> C-6
```

## Actual merge toward v1

```
Accepted lane results
         |
    Stage 5 check
       /                 \
      v                   v
[CRITICAL, UNASSIGNED]   [CRITICAL, UNASSIGNED]
X integration             v1 supported-framework list
       \                 /
        v               v
    Stage 7 check
         |
       U / L / F
        |
[CRITICAL, UNASSIGNED] release R
```

These are scheduling suggestions, not assignments. Critical means someone must claim the work before progress can pass that checkpoint; it does not mean Bilaltariq41 owns it. Any qualified collaborator may coordinate, integrate, or merge when the independence and receipt rules permit it. Bilaltariq41 is unavailable for routine coordination and implementation; only repository access, settings, rulesets, secrets, and bypass actions are provider-restricted T4 work.

```
POST-V1 PARKING LOT (no arrow into v1 release)
  K: persisted later graph stages and incremental invalidation
     unassigned; suggested Ahmad only, open to change
  ongoing supported-framework additions and upkeep
     unassigned; evidence-triggered later work
```

## The whole plan

```
Prepare independently (Stage 1)
              |
Lock the starting point (Stage 2)
              |
Build independently (Stage 3)
              |
Prove each lane works (Stage 4)
              |
Lock the lane results (Stage 5)
              |
Combine the lanes (Stage 6)
              |
Lock the combined result (Stage 7)
              |
Finish wording, CLI, and performance (Stage 8)
              |
Lock release inputs (Stage 9)
              |
Package and release (Stage 10)
```

Lock stages are checks and records, not code projects. They stop a lane from building on unfinished work.

## Who does what

### Firm future assignments

- Ahmad owns P-1 through P-5.
- Qais owns S-1 through S-4.
- Abood owns C-1 through C-6.

Assignments and lifecycle are read from the work-item registry; the stable future lane assignments are listed above.

### Unassigned work

Everything else is unassigned. W and O exception work are suggested pickups for Abood after C; D and framework-list evidence for Qais after S; and state-related X work for Ahmad after P. G, Q, T, X, U, L, F, M, R, and O may be coordinated or implemented by any available qualified collaborator. Suggestions can change, and the first free qualified person may claim a Ready item.

## How claiming works

Claim only a Ready item whose dependencies and readiness self-review are complete and whose exact path lease is available. A suggestion is not a reservation. Keep one active owner or lease for shared paths and preserve author/reviewer independence. Normal decisions use one latest-head non-author human peer approval; T4 repository access, settings, rulesets, secrets, and bypass actions require Bilaltariq41.

## The dependency rule

1. During Prepare, Build, and Prove, lanes do not depend on one another's unfinished issues.
2. A person may depend on an earlier issue in their own lane.
3. Later work may use another lane only after its result passed the lock stage. If it is not ready, defer it or clearly withhold the claim.
4. Cross-lane behavior is combined only in Stage 6. Shared files have one active owner or lease at a time.

The A-2 to A-4 link is one worker lane, not cross-lane work. P and W readiness wait for their current frontier work to close. These are entry conditions, not implementation dependencies.

## What K actually means

SeqDoc already has SQLite storage in `src/SeqDoc.Persistence.Sqlite`, and the CLI already uses it. K is not "add a
cache." It would mean persisting later graph stages and incrementally invalidating and reusing unchanged analysis. It
would likely touch that SQLite project and `SeqDoc.Application`, with stable identity contracts added only if measurement
and readiness show they are safe.

K risks stale or wrongly joined evidence, profile/configuration leakage, corrupted activation, and confusing analysis reuse with application-level FusionCache semantics. Existing SQLite persistence, atomic activation, and previous-valid-state behavior remain required. Do not schedule K unless repeatable measurements show full analysis is too slow. It is not currently justified and is not required for v1.

## How many Reviewer calls?

The worker reviews readiness and the complete candidate, runs the Reviewer agent after repairs, and continues on the same
issue/PR. Then one latest-head non-author human peer reviews and approves. A relevant shared integration may add a run.
Run final tests and make the receipt; agent self-review is not human approval.

Copilot is separate. Disable auto-review-on-push after mandatory receipts are active to avoid duplicate cost.

## What happens after a failed repair?

The worker continues repairing, replanning, pairing, and absorbing necessary acceptance work on the same issue/PR,
updating the checkpoint and running focused tests and the Reviewer agent again. Block only as permitted by the
[collaboration model](collaboration-model.md), including `reviewer unavailable`; the owner is not an automatic fallback.
A separate capability/outcome requires a new issue.

## What the letters mean

- A: current work; C: control flow; S: services; P: persistence; W: workers; D: dependency injection
- O: outcomes and errors; T: traversal; X: integration; U: wording and rendering; L: CLI and configuration
- K: later incremental reuse; F: performance; R: release; G: governance; Q: test application quality; M: supported-framework list

## Bottom line

- Keep the work separate now.
- Combine accepted work later.
- Normal request-changes flow ends with one latest-head non-author human peer approval; there is no automatic takeover, and
  repair stops only at the blockers defined by the collaboration model.
