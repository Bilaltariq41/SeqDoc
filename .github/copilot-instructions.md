# SeqDoc repository invariants

- Treat SeqDoc as a .NET static-analysis CLI that produces evidence-backed Markdown and Mermaid.
- Treat compiler and static evidence as authoritative; never invent exact behavior when evidence is incomplete.
- Preserve the typed pipeline: Program Index, Method Flow, Scenario Graph, and Diagram Plan.
- Keep compilation profiles and Program Index snapshots isolated at every join.
- Carry evidence and the weakest contributing certainty through every typed boundary.
- Distinguish capability, admission, registration, execution, and persistence; do not strengthen claims downstream.
- Retain conservative, evidence-backed boundaries or diagnostics for recognized-but-unsupported forms.
- Keep identities, ordering, and output deterministic and independent of paths, timestamps, scheduling, and iteration order.
- Preserve the previous valid state when analysis or persistence replacement fails.
- Propagate cancellation through long-running operations.
- Keep `SeqDoc.Core` free of Roslyn, MSBuild, SQLite, CLI, and renderer dependencies.
- Use issue authority, newer owner decisions, and declared target paths; keep unrelated changes out of scope.
- Prove semantic behavior through realistic producers, propagation, observable consumers, and relevant boundaries.
- Keep tests isolated, deterministic, clean-checkout reproducible, and free of credentials or machine-local paths.
- Never use application, route, type, method, or business names as production matching rules.
