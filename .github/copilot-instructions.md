# SeqDoc Copilot pre-review

- Read the complete merge-base diff, not only the pull request summary or changed hunks.
- Read the issue body, dependencies, and every owner comment through GitHub MCP before judging the change.
- If linked issue or owner context cannot be retrieved, mark the Spec axis incomplete and never call the pre-review clean.
- Treat newer owner comments and explicit decisions as authority over stale issue or branch descriptions.
- Inventory changed paths, declared target paths, non-goals, and scope drift before reviewing behavior.
- Review two independent axes: **Standards** (repository rules and proof obligations) and **Spec** (issue acceptance and intended behavior).
- Load every applicable file under `.github/instructions/` and the `code-review` skill; these are review guidance, not permission to broaden scope.
- Inspect every changed line and its surrounding producers, consumers, guards, persistence, and rendering paths.
- Apply all five SeqDoc semantic-proof gates whenever compiler, framework, IR, evidence, certainty, or topology semantics are touched.
- Require evidence-backed, actionable findings with file/line, risk, expected behavior, and one focused verification command.
- Check false positives, unsupported boundaries, deterministic identity/order, profile isolation, cancellation, and previous-valid-state preservation.
- Review tests for producer, propagation, observable, and boundary proof; reject fixtures that merely compile or hand-built facts without a producer path.
- Check clean-checkout reproducibility without hidden fixtures, credentials, local paths, timestamps, or unstable iteration.
- Do not spend comments on style or micro-optimization while a semantic, scope, reproducibility, or governance defect remains.
- Do not ask for unsupported overview formatting, automatic fixes, or a formal approval or rejection.
- A clean Copilot result only makes the candidate eligible for independent maintainer review; it never claims merge readiness.
- Never infer merge readiness from pass counts, workflow success, or Copilot silence; preserve human approval and resolved conversations.
- If review-policy files change, mark the Copilot result untrusted and require explicit owner review of the policy delta.
