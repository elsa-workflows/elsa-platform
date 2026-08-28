#!/usr/bin/env node

import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const outputPath = join(repositoryRoot, "THIRD-PARTY-INVENTORY.md");
const nugetPackages = collectNuGetPackages();
const npmPackages = collectNpmPackages();
const containerImages = collectContainerImages();
const githubActions = collectGitHubActions();

writeFileSync(outputPath, renderInventory(), "utf8");
console.log(`Wrote ${relative(repositoryRoot, outputPath)}`);

function collectNuGetPackages() {
  const output = execFileSync(
    "dotnet",
    [
      "list",
      "ElsaControl.sln",
      "package",
      "--include-transitive",
      "--format",
      "json",
      "--no-restore"
    ],
    { cwd: repositoryRoot, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }
  );
  const report = JSON.parse(output);
  const packages = new Map();

  for (const project of report.projects ?? []) {
    const projectPath = relative(repositoryRoot, project.path);
    for (const framework of project.frameworks ?? []) {
      addNuGetPackages(packages, framework.topLevelPackages, projectPath, true);
      addNuGetPackages(packages, framework.transitivePackages, projectPath, false);
    }
  }

  return [...packages.values()]
    .map((item) => ({ ...item, ...readNuGetMetadata(item.id, item.version) }))
    .sort(comparePackages);
}

function addNuGetPackages(packages, candidates = [], projectPath, direct) {
  for (const candidate of candidates) {
    const version = candidate.resolvedVersion;
    const key = `${candidate.id.toLowerCase()}@${version}`;
    const item = packages.get(key) ?? {
      id: candidate.id,
      version,
      direct: false,
      projects: new Set()
    };
    item.direct ||= direct;
    item.projects.add(projectPath);
    packages.set(key, item);
  }
}

function readNuGetMetadata(id, version) {
  const packageDirectory = join(homedir(), ".nuget", "packages", id.toLowerCase(), version.toLowerCase());
  if (!existsSync(packageDirectory))
    return unknownNuGetMetadata();

  const nuspecName = readdirSync(packageDirectory).find((name) => name.endsWith(".nuspec"));
  if (!nuspecName)
    return unknownNuGetMetadata();

  const nuspec = readFileSync(join(packageDirectory, nuspecName), "utf8");
  const licenseElement = nuspec.match(/<license(?:\s+type="([^"]+)")?[^>]*>([\s\S]*?)<\/license>/i);
  const licenseType = licenseElement?.[1] ?? (matchElement(nuspec, "licenseUrl") ? "url" : "unknown");
  const license = licenseElement ? decodeXml(licenseElement[2].trim()) : matchElement(nuspec, "licenseUrl") || "UNKNOWN";
  const projectUrl = matchElement(nuspec, "projectUrl");
  const repositoryUrl = nuspec.match(/<repository\b[^>]*\burl="([^"]+)"/i)?.[1] ?? "";
  const metadataPath = join(packageDirectory, ".nupkg.metadata");
  const packageMetadata = existsSync(metadataPath)
    ? JSON.parse(readFileSync(metadataPath, "utf8"))
    : {};
  const noticeFiles = readdirSync(packageDirectory)
    .filter((name) => /^(license|notice|copying)(\.|$)/i.test(name))
    .sort();

  return {
    license,
    licenseType,
    authors: matchElement(nuspec, "authors"),
    copyright: matchElement(nuspec, "copyright"),
    projectUrl,
    repositoryUrl: decodeXml(repositoryUrl),
    source: packageMetadata.source ?? "",
    contentHash: packageMetadata.contentHash ?? "",
    noticeFiles
  };
}

function unknownNuGetMetadata() {
  return {
    license: "UNKNOWN",
    licenseType: "unknown",
    authors: "",
    copyright: "",
    projectUrl: "",
    repositoryUrl: "",
    source: "",
    contentHash: "",
    noticeFiles: []
  };
}

function matchElement(xml, elementName) {
  const match = xml.match(new RegExp(`<${elementName}(?:\\s[^>]*)?>([\\s\\S]*?)</${elementName}>`, "i"));
  return match ? decodeXml(match[1].trim()) : "";
}

function decodeXml(value) {
  return value
    .replaceAll("&amp;", "&")
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&quot;", "\"")
    .replaceAll("&apos;", "'");
}

function collectNpmPackages() {
  const lockfiles = findFiles(repositoryRoot, (name) => name === "package-lock.json")
    .map((path) => relative(repositoryRoot, path))
    .sort();
  const packages = new Map();

  for (const lockfile of lockfiles) {
    const lock = JSON.parse(readFileSync(join(repositoryRoot, lockfile), "utf8"));
    const root = lock.packages?.[""] ?? {};
    const directRuntime = new Set(Object.keys(root.dependencies ?? {}));
    const directDevelopment = new Set(Object.keys(root.devDependencies ?? {}));

    for (const [packagePath, metadata] of Object.entries(lock.packages ?? {})) {
      if (!packagePath || !metadata.version)
        continue;

      const id = metadata.name ?? packageNameFromPath(packagePath);
      const key = `${id.toLowerCase()}@${metadata.version}`;
      const item = packages.get(key) ?? {
        id,
        version: metadata.version,
        direct: false,
        runtime: false,
        manifests: new Set(),
        license: metadata.license ?? "UNKNOWN",
        projectUrl: `https://www.npmjs.com/package/${encodeURIComponent(id)}`,
        resolved: metadata.resolved ?? "",
        integrity: metadata.integrity ?? "",
        authors: new Set(),
        noticeFiles: new Set()
      };

      item.direct ||= directRuntime.has(id) || directDevelopment.has(id);
      item.runtime ||= metadata.dev !== true;
      item.manifests.add(lockfile);
      addInstalledNpmMetadata(item, lockfile, packagePath);
      if (item.license === "UNKNOWN" && metadata.license)
        item.license = metadata.license;
      packages.set(key, item);
    }
  }

  return [...packages.values()].sort(comparePackages);
}

function addInstalledNpmMetadata(item, lockfile, packagePath) {
  const packageDirectory = join(repositoryRoot, dirname(lockfile), packagePath);
  const packageJsonPath = join(packageDirectory, "package.json");
  if (!existsSync(packageJsonPath))
    return;

  const packageJson = JSON.parse(readFileSync(packageJsonPath, "utf8"));
  const author = typeof packageJson.author === "string"
    ? packageJson.author
    : packageJson.author?.name;
  if (author)
    item.authors.add(author);
  if (item.license === "UNKNOWN" && packageJson.license)
    item.license = packageJson.license;

  for (const name of readdirSync(packageDirectory)) {
    if (/^(license|notice|copying)(\.|$)/i.test(name))
      item.noticeFiles.add(`${dirname(lockfile)}/${packagePath}/${name}`);
  }
}

function packageNameFromPath(packagePath) {
  const marker = "node_modules/";
  return packagePath.slice(packagePath.lastIndexOf(marker) + marker.length);
}

function collectContainerImages() {
  const images = new Map();
  for (const dockerfile of findFiles(repositoryRoot, (name) => name === "Dockerfile" || name.endsWith(".Dockerfile"))) {
    const contents = readFileSync(dockerfile, "utf8");
    for (const match of contents.matchAll(/^\s*FROM\s+(?:--platform=\S+\s+)?(\S+)/gim)) {
      const image = match[1];
      addContainerImage(images, image, relative(repositoryRoot, dockerfile), "Dockerfile base");
    }
  }
  for (const composeFile of findFiles(
    repositoryRoot,
    (name) => /^docker-compose.*\.ya?ml$/i.test(name) || /^compose.*\.ya?ml$/i.test(name)
  )) {
    const contents = readFileSync(composeFile, "utf8");
    for (const match of contents.matchAll(/^\s*image:\s*["']?([^"'\s#]+)["']?/gim)) {
      addContainerImage(images, match[1], relative(repositoryRoot, composeFile), "Compose service");
    }
  }
  return [...images.entries()]
    .map(([image, references]) => ({ image, references }))
    .sort((left, right) => left.image.localeCompare(right.image, "en"));
}

function addContainerImage(images, image, file, use) {
  const references = images.get(image) ?? new Map();
  references.set(`${file}:${use}`, { file, use });
  images.set(image, references);
}

function collectGitHubActions() {
  const actions = new Map();
  const workflowsDirectory = join(repositoryRoot, ".github", "workflows");
  if (!existsSync(workflowsDirectory))
    return [];

  for (const workflow of findFiles(workflowsDirectory, (name) => /\.ya?ml$/i.test(name))) {
    const contents = readFileSync(workflow, "utf8");
    for (const match of contents.matchAll(/^\s*uses:\s*["']?([^"'\s#]+)["']?/gim)) {
      if (match[1].startsWith("./"))
        continue;
      const files = actions.get(match[1]) ?? new Set();
      files.add(relative(repositoryRoot, workflow));
      actions.set(match[1], files);
    }
  }

  return [...actions.entries()]
    .map(([action, files]) => ({ action, files }))
    .sort((left, right) => left.action.localeCompare(right.action, "en"));
}

function findFiles(directory, predicate) {
  const ignored = new Set([".git", "bin", "obj", "node_modules", "dist", "artifacts", "design-mockups"]);
  const files = [];
  for (const entry of readdirSync(directory)) {
    if (ignored.has(entry))
      continue;
    const path = join(directory, entry);
    const stats = statSync(path);
    if (stats.isDirectory())
      files.push(...findFiles(path, predicate));
    else if (predicate(entry))
      files.push(path);
  }
  return files;
}

function renderInventory() {
  const nugetLicenses = countBy(nugetPackages, (item) => item.license);
  const npmLicenses = countBy(npmPackages, (item) => item.license);
  const directNuGet = nugetPackages.filter((item) => item.direct).length;
  const directNpm = npmPackages.filter((item) => item.direct).length;

  return `# Third-party dependency and attribution inventory

This generated inventory records third-party dependencies resolved by the
Elsa Control solution and its npm lockfiles. It is evidence for release
review, not legal advice or a substitute for reviewing the actual licence and
notice text shipped by each dependency.

Regenerate it after dependency changes:

\`\`\`bash
dotnet restore ElsaControl.sln
npm ci --prefix src/ElsaControl.Console
npm ci --prefix tests/ElsaControl.Console.E2E
node scripts/generate-third-party-inventory.mjs
\`\`\`

## Scope and distribution surfaces

| Surface | Inventory basis | Distribution consideration |
| --- | --- | --- |
| .NET applications and packages | Restored direct and transitive NuGet graph | NuGet dependencies normally remain separately licensed packages; bundled generator task assemblies require their notices to travel with the package. |
| Hosted console | Locked runtime and development npm graph | Production JavaScript is bundled into the API container; development-only tooling is recorded but is not shipped in that bundle. |
| Console E2E tests | Locked npm graph | Test-only tooling is not a production distribution component. |
| API container | Dockerfile base-image references | The final image also contains operating-system and .NET runtime components supplied by the base image; retain the base-image notices and generate an image SBOM for each release digest. |
| Generated artefacts | Package and container build inputs | Re-run this inventory and inspect the actual package/image contents for every release candidate. |

## Summary

- NuGet: ${nugetPackages.length} unique package/version records (${directNuGet} used directly).
- npm: ${npmPackages.length} unique package/version records (${directNpm} used directly).
- Container base images: ${containerImages.length}.
- GitHub Actions: ${githubActions.length} pinned action/version references.
- Unknown NuGet licence metadata: ${nugetPackages.filter((item) => item.license === "UNKNOWN").length}.
- Unknown npm licence metadata: ${npmPackages.filter((item) => item.license === "UNKNOWN").length}.
- File-based NuGet licences requiring packaged-text review: ${nugetPackages.filter((item) => item.licenseType.toLowerCase() === "file").length}.

### NuGet licence metadata

${renderCounts(nugetLicenses)}

### npm licence metadata

${renderCounts(npmLicenses)}

## NuGet packages

| Package | Version | Use | Projects | Licence metadata | Attribution metadata | Provenance |
| --- | --- | --- | --- | --- | --- | --- |
${nugetPackages.map((item) =>
  `| ${escapeMarkdown(item.id)} | ${escapeMarkdown(item.version)} | ${item.direct ? "Direct" : "Transitive"} | ${[...item.projects].sort().map(escapeMarkdown).join("<br>")} | ${escapeMarkdown(`${item.licenseType}: ${item.license}`)}${renderFiles(item.noticeFiles)} | ${escapeMarkdown(formatAttribution(item))} | ${link("project", item.repositoryUrl || item.projectUrl)}${item.source ? `<br>${link("source", item.source)}` : ""}${item.contentHash ? `<br>\`${escapeMarkdown(item.contentHash)}\`` : ""} |`
).join("\n")}

## npm packages

| Package | Version | Use | Scope | Manifests | Licence metadata | Attribution/provenance |
| --- | --- | --- | --- | --- | --- | --- |
${npmPackages.map((item) =>
  `| ${link(item.id, item.projectUrl)} | ${escapeMarkdown(item.version)} | ${item.direct ? "Direct" : "Transitive"} | ${item.runtime ? "Runtime" : "Development"} | ${[...item.manifests].sort().map(escapeMarkdown).join("<br>")} | ${escapeMarkdown(item.license)}${renderFiles([...item.noticeFiles])} | ${escapeMarkdown([...item.authors].sort().join("; "))}${item.resolved ? `<br>${link("source", item.resolved)}` : ""}${item.integrity ? `<br>\`${escapeMarkdown(item.integrity)}\`` : ""} |`
).join("\n")}

## Container base images

| Image | Use | Source files |
| --- | --- | --- |
${containerImages.map((item) =>
  `| ${escapeMarkdown(item.image)} | ${[...item.references.values()].map((reference) => escapeMarkdown(reference.use)).join("<br>")} | ${[...item.references.values()].map((reference) => escapeMarkdown(reference.file)).join("<br>")} |`
).join("\n")}

## GitHub Actions

These build-time tools are not product runtime components, but they are part of
the repository supply chain.

| Action reference | Workflow files |
| --- | --- |
${githubActions.map((item) =>
  `| ${escapeMarkdown(item.action)} | ${[...item.files].sort().map(escapeMarkdown).join("<br>")} |`
).join("\n")}

## Release review still required

- Preserve licence and NOTICE files required by bundled or redistributed components.
- Review non-permissive, source-available, unknown, custom, or file-based licence entries individually.
- Confirm attribution text and copyright statements for packages whose licence requires them.
- Generate an SBOM from the final container digest and packaged artefacts, because
  a source-lockfile inventory cannot see every operating-system or copied binary component.
- Record any accepted exception with an owner, rationale, affected version, and review date.
`;
}

function countBy(items, selector) {
  const counts = new Map();
  for (const item of items) {
    const key = selector(item);
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  return [...counts.entries()].sort(([left], [right]) => left.localeCompare(right, "en"));
}

function renderCounts(counts) {
  return [
    "| Licence metadata | Package/version records |",
    "| --- | ---: |",
    ...counts.map(([license, count]) => `| ${escapeMarkdown(license)} | ${count} |`)
  ].join("\n");
}

function comparePackages(left, right) {
  return left.id.localeCompare(right.id, "en") || left.version.localeCompare(right.version, "en");
}

function link(label, url) {
  return url ? `[${escapeMarkdown(label)}](${url})` : "";
}

function formatAttribution(item) {
  return [item.authors, item.copyright].filter(Boolean).join("; ");
}

function renderFiles(files = []) {
  return files.length ? `<br>Files: ${files.map(escapeMarkdown).join(", ")}` : "";
}

function escapeMarkdown(value) {
  return String(value ?? "").replaceAll("|", "\\|").replaceAll("\n", " ");
}
