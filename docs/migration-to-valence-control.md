# Migration to Valence Control

On 26 July 2026, GitHub's repository-transfer and rename operations moved
`elsa-workflows/elsa-platform` to the private repository
`valence-works/valence-control`. The existing repository object was transferred;
Git history was not rewritten or recreated.

The product name changed from Elsa Platform to Valence Control. Product-owned
.NET projects, assemblies, packages, and namespaces changed from
`Elsa.Platform.*` to `ValenceControl.*`. Product-owned infrastructure and
configuration use the `valence-control` slug.

Elsa Workflows remains a separate, MIT-licensed, vendor-neutral open-source
project. Upstream package names, namespaces, schemas, and runtime concepts that
describe Elsa Workflows interoperability retain their Elsa identities.

The codebase was unreleased at the time of migration. Product-owned persisted
identifiers, wire contracts, routes, configuration keys, package IDs, and
infrastructure names therefore make a clean break without compatibility aliases.
Existing development databases, environment configuration, clients, and local
infrastructure must be recreated or updated for the new identifiers.

The repository licence changed from the MIT licence present in earlier revisions
to the Valence Control Commercial License. Earlier revisions remain governed by
the terms under which they were published. The current licence contains
`[LEGAL ENTITY NAME]` because the legal licensor could not be verified from
authoritative documentation; qualified legal counsel must replace and approve
that placeholder before commercial distribution.

Changing the transferred repository from public to private detached its public
fork network and removed public star/watcher relationships where GitHub requires
that behaviour. Repository rulesets also became unavailable under the
destination organisation's current GitHub plan and require plan or policy
follow-up.
