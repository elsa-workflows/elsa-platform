#!/usr/bin/env python3
"""Classify pull-request paths for the repository's .NET CI gate.

The classifier deliberately keeps a small allowlist of paths known not to
affect .NET behavior. Everything else runs the full gate, so adding a new
repository area cannot accidentally make a pull request skip verification.
"""

from __future__ import annotations

import argparse
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Sequence


PULL_REQUEST_EVENT = "pull_request"

# These areas have their own lightweight/console/browser validation and do not
# contain .NET sources or test fixtures. Keep this list explicit and small.
KNOWN_UNRELATED_PREFIXES = (
    "docs/",
    "infra/",
    "scripts/managed-elsa-browser-proof/",
    "src/Hosting/ElsaControl.Console/",
    "tests/Hosting/ElsaControl.Console.E2E/",
)


@dataclass(frozen=True)
class DotnetGateDecision:
    """The decision and evidence consumed by the GitHub Actions workflow."""

    run_dotnet: bool
    reason: str
    impacting_paths: tuple[str, ...] = ()


def _normalize_path(path: str) -> str:
    normalized = path.replace("\\", "/")
    while normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized


def _is_known_unrelated(path: str) -> bool:
    if ".." in path.split("/"):
        return False
    return path.startswith(KNOWN_UNRELATED_PREFIXES) or (
        "/" not in path and path.endswith((".md", ".mdx"))
    )


def classify_paths(
    event_name: str,
    changed_paths: Iterable[str],
    *,
    paths_available: bool = True,
) -> DotnetGateDecision:
    """Return a fail-closed .NET gate decision for an event and path set.

    Only pull requests may skip the .NET gate. Pushes, manual runs, unknown
    events, and unavailable path data always run the complete gate.
    """

    event = event_name.strip().lower()
    if event in {"push", "workflow_dispatch"}:
        return DotnetGateDecision(
            True,
            f"Running full .NET gate for {event_name} (event override).",
        )

    if event != PULL_REQUEST_EVENT:
        return DotnetGateDecision(
            True,
            f"Running full .NET gate for unsupported event '{event_name}' (fail closed).",
        )

    if not paths_available:
        return DotnetGateDecision(
            True,
            "Running full .NET gate because no changed paths were available (fail closed).",
        )

    normalized_paths = tuple(
        path for path in (_normalize_path(item) for item in changed_paths) if path
    )
    if not normalized_paths:
        return DotnetGateDecision(
            True,
            "Running full .NET gate because no changed paths were available (fail closed).",
        )

    impacting_paths = tuple(
        path for path in normalized_paths if not _is_known_unrelated(path)
    )
    if impacting_paths:
        path = impacting_paths[0].replace("\n", " ").replace("\r", " ")
        return DotnetGateDecision(
            True,
            "Running full .NET gate because changed path is not on the "
            f"conservative unrelated-path allowlist and may affect .NET: {path}.",
            impacting_paths,
        )

    return DotnetGateDecision(
        False,
        "Skipping .NET setup, restore, build, and test: all changed paths are "
        "on the conservative unrelated-path allowlist.",
    )


def _changed_paths_from_git(repo_root: Path, base_sha: str, head_sha: str) -> list[str]:
    if not base_sha or not head_sha:
        raise ValueError("pull-request base and head SHAs are required")

    result = subprocess.run(
        [
            "git",
            "diff",
            "--name-only",
            "--diff-filter=ACDMRTUXB",
            "-z",
            f"{base_sha}...{head_sha}",
        ],
        cwd=repo_root,
        check=True,
        capture_output=True,
    )
    return [
        path.decode("utf-8", errors="surrogateescape")
        for path in result.stdout.split(b"\0")
        if path
    ]


def _write_github_output(output_path: Path, decision: DotnetGateDecision) -> None:
    reason = decision.reason.replace("\r", " ").replace("\n", " ")
    with output_path.open("a", encoding="utf-8") as output:
        output.write(f"run_dotnet={'true' if decision.run_dotnet else 'false'}\n")
        output.write(f"reason={reason}\n")
        output.write(f"impacting_path_count={len(decision.impacting_paths)}\n")


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--event-name", default=os.environ.get("GITHUB_EVENT_NAME", ""))
    parser.add_argument("--base-sha", default=os.environ.get("CI_BASE_SHA", ""))
    parser.add_argument("--head-sha", default=os.environ.get("CI_HEAD_SHA", ""))
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path.cwd(),
        help="Repository root used to calculate pull-request changes.",
    )
    parser.add_argument(
        "--github-output",
        type=Path,
        default=None,
        help="Append decision outputs to this GitHub Actions output file.",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    event_name = args.event_name

    if event_name.strip().lower() == PULL_REQUEST_EVENT:
        try:
            changed_paths = _changed_paths_from_git(
                args.repo_root,
                args.base_sha,
                args.head_sha,
            )
        except (OSError, subprocess.CalledProcessError, ValueError) as error:
            decision = classify_paths(
                event_name,
                (),
                paths_available=False,
            )
            decision = DotnetGateDecision(
                decision.run_dotnet,
                f"{decision.reason} Git diff could not be read: {error}",
                decision.impacting_paths,
            )
        else:
            decision = classify_paths(event_name, changed_paths)
    else:
        decision = classify_paths(event_name, ())

    if args.github_output is not None:
        _write_github_output(args.github_output, decision)

    print(f"run_dotnet={'true' if decision.run_dotnet else 'false'}")
    print(f"reason={decision.reason}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
