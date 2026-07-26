# Commercial-licence legal review

The repository now contains the Valence Control Commercial License, but it is
not ready for commercial distribution. Every item below requires qualified
legal review.

## Release blockers

- [ ] Verify the legal licensor and replace `[LEGAL ENTITY NAME]` in `LICENSE`
  and project metadata.
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

- [ ] Inventory direct and transitive NuGet and npm dependencies.
- [ ] Obtain the required commercial licence for Fluent Assertions or replace
  it before commercial use. The test runner currently emits its commercial-use
  licence warning.
- [ ] Review and remediate the console dependency audit findings recorded
  during migration (`1` low, `5` moderate, `4` high, and `1` critical) before
  release; do not apply breaking dependency upgrades without review.
- [ ] Review open-source, source-available, proprietary, copyleft, attribution,
  patent, trademark, and notice obligations.
- [ ] Review Docker base images, operating-system packages, build tools, and
  generated artefacts.
- [ ] Produce and ship the required third-party notice inventory with every
  applicable package, container, source archive, and distribution.

## Controlled release

- [ ] Approve the effective commercial revision and any final historical MIT
  revision marker.
- [ ] Review package, container, release, and contributor metadata.
- [ ] Configure company-controlled registries and credentials.
- [ ] Verify that no CI workflow can publish or deploy without explicit approval.
- [ ] Obtain written legal approval before publishing any commercial release.
