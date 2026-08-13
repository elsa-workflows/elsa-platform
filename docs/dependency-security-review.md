# Dependency security review

Last reviewed: 2026-07-27

## npm remediation

The hosted console dependency graph was updated after reviewing the migration
branch's npm and Dependabot findings. The original `npm audit` result contained
11 findings: 1 critical, 4 high, 5 moderate, and 1 low. Updating React Router,
Vite, Vitest, and their transitive dependencies removed the reported
PostCSS, Babel, `form-data`, `ws`, esbuild, Vite, and Vitest findings.

The current audit result contains two high-severity records for one upstream
React Router advisory:

| Advisory | Affected dependency | Resolved version | Decision |
| --- | --- | --- | --- |
| [GHSA-qwww-vcr4-c8h2](https://github.com/advisories/GHSA-qwww-vcr4-c8h2), RSC-mode CSRF bypass | `react-router` and direct `react-router-dom` | `7.18.1` | Temporarily accepted as not reachable in this application |

Valence Control uses `createBrowserRouter` in a client-rendered Vite
application. It does not enable React Server Components, server actions, or
React Router's RSC mode, so the affected execution path is not present. npm's
suggested change to `react-router-dom@7.11.0` is an older release and would
reintroduce previously remediated React Router advisories; it was not applied.

Exception owner: Valence Control maintainers

Review date: 2026-10-27, or sooner when React Router publishes a non-vulnerable
release that supports the current client-rendered architecture.

## Release checks

Run these checks whenever dependency lockfiles change:

```bash
npm audit --prefix src/Hosting/ValenceControl.Console
npm audit --prefix tests/Hosting/ValenceControl.Console.E2E
dotnet list ValenceControl.sln package --vulnerable --include-transitive
```

This review documents technical reachability and remediation decisions. It is
not legal approval of dependency licences or a substitute for reviewing the
third-party inventory and the contents of the final distributable artefacts.
