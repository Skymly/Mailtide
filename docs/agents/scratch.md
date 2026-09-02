# Agent scratch

`.scratch/` is the local trash can for agent patches, issue drafts, one-off scripts, and PR bodies. It is gitignored (`/.scratch/`).

Rules:

- Write throwaway files only under `.scratch/`. Do not commit them.
- Prefer deleting a scratch file once the change has landed on a branch.
- Do not store secrets, OAuth client secrets, keystores, or live mail in `.scratch/`.
- Remote `codex/*` and `cursor/*` branches are disposable after the PR merges to `main`. Delete the merged remote branch instead of leaving a graveyard.

If `.scratch/` is missing, create it locally as needed. Absence is not an error.
