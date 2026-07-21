# Repository-transfer checklist

This checklist is for a later, manually reviewed transfer. It does not authorize or perform a GitHub transfer. Replace placeholders only after the destination organization and repository name have been approved.

## Before transfer

- [ ] Confirm the final company-controlled GitHub organization: `<destination-organization>`.
- [ ] Confirm the destination repository name: `<destination-repository>`.
- [ ] Confirm organization ownership, administrator access, billing, and recovery contacts.
- [ ] Record the current repository URL: `https://github.com/elsa-workflows/elsa-platform`.
- [ ] Record the current default branch: `main`.
- [ ] Record the current revision and working-tree state:
  ```bash
  git remote -v
  git branch --show-current
  git rev-parse HEAD
  git status --short
  ```
- [ ] Record repository visibility, archived state, merge settings, required checks, branch protections, rulesets, security settings, and notification settings.
- [ ] Confirm secrets and environment configuration, including Azure OIDC variables, Feedz credentials, NuGet credentials, deployment environments, and reviewers.
- [ ] Confirm GitHub Apps, webhooks, deploy keys, machine users, external integrations, and callback URLs.
- [ ] Confirm package ownership and publication destinations for Feedz.io, NuGet.org, npm, and any future registry.
- [ ] Confirm container registry ownership, image names, retention policies, pull permissions, and deployment identities.
- [ ] Confirm GitHub Pages implications. No Pages configuration is currently present in this repository.
- [ ] Confirm release automation and release/tag ownership.
- [ ] Confirm external documentation, support, monitoring, and status-page links.
- [ ] Confirm trademark, branding, and the spelling of `Skywalker Digital B.V., trading as Valence Works` with the legal owner.
- [ ] Back up repository metadata where appropriate, including rulesets, environments, variables, integrations, and package settings.
- [ ] Inspect the current repository-transfer audit in `docs/commercial-transition-audit.md` and resolve its confirmation items.
- [ ] Push the final MIT tag only after the final revision and tag have been approved:
  ```bash
  git tag -a last-mit-licensed-revision <approved-commit> -m "Final repository revision intended to remain published as the last MIT-licensed revision before the planned commercial licensing transition. Previously published versions remain available under their original terms."
  git push origin last-mit-licensed-revision
  ```

## Transfer

- [ ] Use GitHub’s supported repository-transfer mechanism.
- [ ] Do not recreate the repository manually as a substitute for transfer.
- [ ] Do not transfer until the destination organization accepts the repository and has the required billing and administrator controls.
- [ ] Verify that issues, pull requests, releases, stars, repository history, branch references, and discussions transfer as expected.
- [ ] Verify redirects from the original repository URL.
- [ ] Verify destination permissions, teams, Apps, webhooks, deploy keys, rulesets, and environments.
- [ ] Do not change the default branch, delete refs, delete packages, or rewrite history as part of the transfer.

## After transfer

- [ ] Update local remotes:
  ```bash
  git remote set-url origin https://github.com/<destination-organization>/<destination-repository>.git
  git remote -v
  ```
- [ ] Update badges and repository links.
- [ ] Update NuGet, npm, container, and package metadata where the old organization is embedded.
- [ ] Update documentation links, issue links, examples, and generated metadata.
- [ ] Update CI references and verify workflow permissions.
- [ ] Recreate or reauthorize secrets, variables, environments, OIDC trust, and environment reviewers.
- [ ] Verify GitHub Pages, if enabled later.
- [ ] Verify Feedz.io and NuGet.org package publishing with a non-production dry run.
- [ ] Verify container build, push, registry retention, and deployment pull access.
- [ ] Verify release and tag publishing.
- [ ] Verify webhooks, GitHub Apps, deploy keys, and external integrations.
- [ ] Verify branch protections, rulesets, required checks, and merge policy.
- [ ] Verify `CODEOWNERS`, security policy, issue templates, and pull-request templates.
- [ ] Verify external documentation, support, monitoring, and status links.
- [ ] Verify that the old URL redirects correctly and that links do not silently point to an unintended repository.
- [ ] Run a complete build, test, package, container, and release dry run.
- [ ] Record the transfer date, operator, destination, verification results, and unresolved follow-up items.
