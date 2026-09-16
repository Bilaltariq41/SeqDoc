# Collaborator and owner setup runbook

No changes are performed by this document. Follow it only after the owner and applicable decision records authorize
adoption. The rules are in [collaboration-model.md](collaboration-model.md).

## Owner UI/API checklist

1. Grant `AhmadKrarha`, `Qhatahet`, and `Abood-essa` Write access. Verify each collaborator can open a fork, branch,
   and draft PR.
2. Ask each account to confirm 2FA and recovery methods, fork/upstream setup, and the access acceptance sentence:
   **"I accept collaborator access to Bilaltariq41/SeqDoc, will use a fork and PR, protect credentials, preserve the
   frozen contract/allowlist, and record review receipts against exact SHAs."** Never place tokens in chat or files.
3. Apply the intended `main` rules: one latest-head non-author approval, dismiss stale approvals, require approval of
   the latest reviewable push, resolve all conversations, and require strict `validate`. The current
   [`work-state.yml`](../../.github/workflows/work-state.yml) runs `validate` on every `pull_request`.
4. Disable force pushes and deletion; keep auto-merge disabled for now. Retain the administrator PR-only bypass and
   direct-push restriction. No code-owner rule is used.
5. Inspect effective rules through settings/API read-only evidence before activation. Only Bilaltariq41 may change
   settings, rules, or bypass them; an emergency uses `OWNER-BYPASS v1`, then restores the normal rules and audits it.
6. Harden Actions with full-SHA pins, least-privilege read tokens, hosted ephemeral runners, and no public self-hosted
   runner or `pull_request_target`.
7. Keep Copilot review supplemental; it is not the independent human peer receipt.
8. Use the published issue/PR templates. They capture planning IDs, leases, receipts, SHAs, observables, and gates.

## Clone and fork setup

```powershell
git clone https://github.com/<account>/SeqDoc.git
cd SeqDoc
git remote add upstream https://github.com/Bilaltariq41/SeqDoc.git
git fetch upstream
git switch --create <checkpoint-branch> upstream/main
```

Use a fork PR to upstream; never store credentials in remotes, command history, chat, or repository files.

## Disposable smoke test and preflight

**A - unmerged probe PR.** Each collaborator opens a real disposable fork PR with a narrowly scoped probe. Verify
effective rules and bypass actors using settings/API read-only evidence. Prove `validate` runs on the `pull_request`,
with no repository secrets and a read-only token. A non-author human peer
invokes the Reviewer agent and posts an authenticated receipt containing the peer actor plus agent identity/version,
invocation, base/head, scope, findings/output hash/link, dispositions, and gates. Exercise stale approval after a new
push and a policy self-change PR that remains blocked. Close the probe PR unmerged.

**B - legitimate adoption rehearsal.** Use the first legitimate G-0/G-5 governance adoption PR as the compliant
collaborator merge rehearsal. Apply the normal peer review, receipt, checks, conversation resolution, and merge sequence;
do not merge no-op or junk documentation. Do not attempt a direct-main push or bypass dry run. Secret-using jobs are
trusted post-merge only, and no `pull_request_target` workaround is permitted.

## Rollback, offboarding, and emergency contact

Bilaltariq41 records the exact change, reason, affected paths, and follow-up before reverting permissions, protection,
rules, or invitations. Revoke collaborator access, review open PRs and leases, rotate affected secrets
through the owner-controlled mechanism, and preserve receipts and attribution. A collaborator reports suspected
compromise or unsafe merge through the repository issue/PR and directly to `@Bilaltariq41`; they must not bypass rules or
delete evidence. Emergency changes use `OWNER-BYPASS v1` with exact SHA, risk, reason, and follow-up, then applicable
review and gate evidence.
