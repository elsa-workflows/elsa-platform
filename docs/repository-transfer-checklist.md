# Repository-transfer verification

The GitHub repository was transferred on 26 July 2026 from
`elsa-workflows/elsa-platform` to the private repository
`valence-works/valence-control` using GitHub's supported transfer and rename
operations.

## Verified

- [x] Canonical repository URL is
  `https://github.com/valence-works/valence-control`.
- [x] Default branch remains `main`.
- [x] Git commit history was preserved without rewriting.
- [x] Nine remote branches and zero tags remained available.
- [x] Eleven issues and 67 pull requests remained associated with the
  repository.
- [x] No releases existed before or after transfer.
- [x] GitHub environment names `copilot`, `development`, and `production`
  remained present.
- [x] Repository secrets, repository variables, webhooks, and deploy keys were
  absent. No secret values were read or printed.
- [x] The `development` environment retained its existing non-secret variable
  names; the other environments exposed no variables or secrets.
- [x] Local `origin` was updated and fetched successfully.
- [x] The destination repository is private.

## Manual follow-up

- [ ] Verify environment reviewers, secrets, variables, branch policies, and
  Azure OIDC subject claims before enabling deployment.
- [ ] Reconfigure the previous Copilot code-review ruleset or upgrade the GitHub
  plan; rulesets are unavailable for this private repository on the current
  plan.
- [ ] Verify organisation/team permissions, GitHub App installations, callback
  URLs, webhooks, deploy keys, and external automation.
- [ ] Select company-owned NuGet and container publication destinations.
  Publication is disabled during this migration.
- [ ] Verify external links, monitoring, support, registry pull permissions, and
  any package permissions before release.

The public-to-private visibility change detached the public fork network and
removed public star/watcher relationships where GitHub requires it. Those
relationships cannot be restored by this repository.
