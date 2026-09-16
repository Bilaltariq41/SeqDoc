# Semantic proof checklist

Use concrete evidence from the changed producer to its first observable consumer.

- **Capability is not execution.** Good: a discovered `IService` capability is reported separately from a registered callback and an executed root. Bad: seeing a matching operation claims registration or execution.
- **Identity is exact.** Good: match the original symbol definition, containing type, and assembly plus supported overload/argument positions. Bad: match a method-name string, a lookalike type, or mix ASP.NET/ WCF/EF framework identities.
- **Profile and snapshot are confined.** Good: join only facts with the exact compilation profile and Program Index snapshot. Bad: reuse a same-named symbol or flow from another target framework or analysis snapshot.
- **Certainty weakens, never strengthens.** Good: a composite fact carries the least-confident contributing certainty and its evidence. Bad: inferred syntax becomes an exact runtime or persistence claim.
- **Producer reaches an observable.** Good: realistic source reaches the production extractor and an assertion checks wording, Mermaid, persistence, or CLI output. Bad: a hand-built intermediate fact or compiling fixture is called proof.
- **Topology places claims safely.** Good: retain guards, terminals, exception regions, and chronology, or withhold placement when topology is unknown. Bad: attach a call/predicate to a branch merely because it exists in the method.
- **Composite identities are deterministic.** Good: derive a stable identity from canonical profile, containing symbol, source position, and role with collision handling. Bad: use hash iteration, timestamps, checkout paths, or display text alone.
- **Unsupported forms stay conservative.** Good: recognized-but-unsupported overloads or lookalikes produce a boundary/diagnostic and do not enter the positive graph. Bad: silently treat them as supported or invent behavior.
- **Previous valid state survives failure.** Good: a failed analysis leaves the prior valid result intact and reports failure. Bad: clear or partially overwrite valid output before the failed replacement completes.

For framework semantics, record operation shape, exact framework symbol and assembly, supported overloads, registration requirement, callback mapping, unsupported forms, negative lookalikes, and first consumer.
