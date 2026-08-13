# Commercial-licence legal review

The repository now contains the Valence Control Commercial License, but it is
not ready for commercial distribution. Every item below requires qualified
legal review.

## Release blockers

- [x] Confirm the legal licensor as Skywalker Digital B.V., trading as Valence
  Works, and record it in `LICENSE` and project metadata.
- [ ] Confirm copyright ownership and relicensing authority for every
  substantial contribution, imported/shared source, generated source, and
  bot-assisted change.
- [ ] Review employment, contractor, customer, and other IP-assignment records.
- [ ] Confirm that prior MIT-licensed revisions and notices are described
  accurately.
- [ ] Approve the licence's proprietary-use restriction, warranty disclaimer,
  and limitation of liability.
- [ ] Decide whether additional evaluation, development, production, hosted
  service, affiliate, contractor, termination, or commercial agreement terms
  are required in separate documents.

## Third-party review

- [x] Inventory direct and transitive NuGet and npm dependencies, Docker base
  images, and GitHub Actions in `THIRD-PARTY-INVENTORY.md`.
- [x] Removed Fluent Assertions and migrated the test suite to xUnit's built-in
  assertions; no Fluent Assertions commercial licence is required.
- [x] Review and remediate the console dependency audit findings recorded
  during migration. The remaining React Router RSC advisory is documented as
  unreachable in `docs/dependency-security-review.md`, with an owner and review
  date.
- [ ] Review open-source, source-available, proprietary, copyleft, attribution,
  patent, trademark, and notice obligations, including the json-everything
  binary-release follow-up in `docs/third-party-licence-review.md`.
- [ ] Review Docker base images, operating-system packages, build tools, and
  generated artefacts.
- [x] Produce the third-party dependency and attribution inventory.
- [ ] Verify that the required licence and notice texts ship with every
  applicable package, container, source archive, and distribution.

## Controlled release

- [ ] Approve the effective commercial revision and any final historical MIT
  revision marker.
- [ ] Review package, container, release, and contributor metadata.
- [ ] Configure company-controlled registries and credentials.
- [ ] Verify that no CI workflow can publish or deploy without explicit approval.
- [ ] Obtain written legal approval before publishing any commercial release.
