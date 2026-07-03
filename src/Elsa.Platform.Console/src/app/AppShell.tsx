import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity,
  Archive,
  Bot,
  Building2,
  Boxes,
  ChevronDown,
  Cloud,
  DatabaseZap,
  FileClock,
  Gauge,
  Home,
  KeyRound,
  Layers3,
  Moon,
  PackageSearch,
  Palette,
  Rocket,
  ShieldCheck,
  Sun,
  Terminal,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { NavLink, Outlet } from "react-router-dom";
import { getApplicationInfo } from "@/app/applicationApi";
import { WorkspaceContextProvider, useWorkspaceContext } from "@/app/WorkspaceContextProvider";
import { WeaverAssistantPanel } from "@/features/weaver/WeaverAssistantPanel";
import { useAuth } from "@/lib/auth/AuthProvider";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";
import { Select } from "@/components/ui";

type NavItem = {
  to: string;
  label: string;
  icon: LucideIcon;
  disabled?: boolean;
  end?: boolean;
};

const navSections: Array<{ label: string; items: NavItem[] }> = [
  {
    label: "Platform",
    items: [
      { to: "/admin/overview", label: "Overview", icon: Home }
    ]
  },
  {
    // "Deliver" gathers everything an operator touches to ship a version from artifact to running:
    // the deployments cockpit, application pipeline, versions/revisions, and artifacts.
    label: "Deliver",
    items: [
      { to: "/admin/deployments", label: "Overview", icon: Gauge, end: true },
      { to: "/admin/deployments/applications", label: "Applications", icon: Rocket },
      { to: "/admin/artifacts", label: "Artifacts", icon: Archive }
    ]
  },
  {
    // "Operate" holds the advanced control-plane surfaces: tier definitions, engine credential
    // stores, and the raw operational tools. These are configuration-and-diagnostics screens rather
    // than day-to-day delivery steps.
    label: "Operate",
    items: [
      { to: "/admin/deployments/tiers", label: "Tiers", icon: ShieldCheck },
      { to: "/admin/deployments/credentials", label: "Engine credentials", icon: KeyRound },
      { to: "/admin/console", label: "Console", icon: Terminal },
      { to: "/admin/targets", label: "Targets", icon: Cloud, disabled: true },
      { to: "/admin/runtimes", label: "Managed Runtimes", icon: Gauge, disabled: true },
      { to: "/admin/operations", label: "Runtime Operations", icon: Activity, disabled: true },
      { to: "/admin/audit", label: "Audit", icon: FileClock, disabled: true }
    ]
  },
  {
    label: "Package Catalog",
    items: [
      { to: "/admin/sources", label: "Sources", icon: DatabaseZap },
      { to: "/admin/packages", label: "Packages", icon: PackageSearch },
      { to: "/admin/sync-runs", label: "Sync Runs", icon: Boxes }
    ]
  },
  {
    label: "Runtime Builder",
    items: [
      { to: "/admin/runtime-builder", label: "Build configurations", icon: Layers3 }
    ]
  }
];

type Theme = "light" | "dark";
type ThemeAccent = "teal" | "blue" | "violet" | "amber" | "rose";

const themeAccentStorageKey = "elsa-console-theme-accent";

const themeAccents: Array<{ value: ThemeAccent; label: string }> = [
  { value: "teal", label: "Teal" },
  { value: "blue", label: "Blue" },
  { value: "violet", label: "Violet" },
  { value: "amber", label: "Amber" },
  { value: "rose", label: "Rose" }
];

export function AppShell() {
  const [theme, setTheme] = useTheme();
  const [themeAccent, setThemeAccent] = useThemeAccent();

  return (
    <WorkspaceContextProvider>
      <AppShellLayout theme={theme} themeAccent={themeAccent} onThemeChange={setTheme} onThemeAccentChange={setThemeAccent} />
    </WorkspaceContextProvider>
  );
}

function AppShellLayout({
  theme,
  themeAccent,
  onThemeChange,
  onThemeAccentChange
}: {
  theme: Theme;
  themeAccent: ThemeAccent;
  onThemeChange: (theme: Theme) => void;
  onThemeAccentChange: (themeAccent: ThemeAccent) => void;
}) {
  const [weaverOpen, setWeaverOpen] = useState(false);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <aside className="fixed inset-y-0 left-0 hidden w-72 flex-col border-r border-border bg-surface px-3 py-4 md:flex">
        <div>
          <div className="flex items-start justify-between gap-3 px-2 pb-6">
            <div>
              <p className="font-display text-base font-semibold tracking-normal">Elsa Platform</p>
              <p className="text-xs text-muted-foreground">Platform Console</p>
            </div>
            <div className="flex shrink-0 items-center gap-1">
              <ThemeAccentPicker themeAccent={themeAccent} onThemeAccentChange={onThemeAccentChange} />
              <ThemeToggle theme={theme} onThemeChange={onThemeChange} />
            </div>
          </div>
          <OrganizationWorkspaceSwitcher className="mb-5" />
          <PrimaryNavigation />
        </div>
        <ApplicationBuildNumber className="mt-auto px-2 pt-4" />
      </aside>
      <div className="md:pl-72">
        <header className="sticky top-0 z-10 border-b border-border bg-background/95 px-4 py-3 backdrop-blur md:hidden">
          <div className="mb-2 flex items-center justify-between gap-3">
            <p className="font-display text-sm font-semibold">Elsa Platform</p>
            <div className="flex shrink-0 items-center gap-2">
              <ApplicationBuildNumber className="text-right" />
              <WeaverTrigger onClick={() => setWeaverOpen(true)} compact />
              <ThemeAccentPicker themeAccent={themeAccent} onThemeAccentChange={onThemeAccentChange} compact />
              <ThemeToggle theme={theme} onThemeChange={onThemeChange} />
            </div>
          </div>
          <OrganizationWorkspaceSwitcher compact className="mb-2" />
          <PrimaryNavigation compact />
        </header>
        <header className="sticky top-0 z-10 hidden border-b border-border bg-background/95 px-8 py-3 backdrop-blur md:block">
          <div className="flex w-full items-center justify-between gap-4">
            <div className="flex min-w-0 items-center gap-3 text-sm text-muted-foreground">
              <ShieldCheck aria-hidden className="h-4 w-4 text-primary" />
              <span className="truncate">Organization control plane for deployments, packages, runtimes, and operations</span>
            </div>
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <WeaverTrigger onClick={() => setWeaverOpen(true)} />
              <ThemeToggle theme={theme} onThemeChange={onThemeChange} />
            </div>
          </div>
        </header>
        <main className="w-full px-4 py-6 md:px-8">
          <Outlet />
        </main>
      </div>
      <WeaverAssistantPanel open={weaverOpen} onClose={() => setWeaverOpen(false)} />
    </div>
  );
}

function useTheme(): [Theme, (theme: Theme) => void] {
  const [theme, setThemeState] = useState<Theme>(() => {
    return getStoredTheme();
  });

  useEffect(() => {
    document.documentElement.classList.toggle("dark", theme === "dark");
    storeTheme(theme);
  }, [theme]);

  return [theme, setThemeState];
}

function useThemeAccent(): [ThemeAccent, (themeAccent: ThemeAccent) => void] {
  const [themeAccent, setThemeAccentState] = useState<ThemeAccent>(() => {
    return getStoredThemeAccent();
  });

  useEffect(() => {
    document.documentElement.dataset.themeAccent = themeAccent;
    storeThemeAccent(themeAccent);
  }, [themeAccent]);

  return [themeAccent, setThemeAccentState];
}

function getStoredTheme(): Theme {
  if (typeof window === "undefined" || typeof window.localStorage?.getItem !== "function") {
    return "light";
  }

  try {
    return window.localStorage.getItem("elsa-console-theme") === "dark" ? "dark" : "light";
  } catch {
    return "light";
  }
}

function storeTheme(theme: Theme) {
  if (typeof window === "undefined" || typeof window.localStorage?.setItem !== "function") {
    return;
  }

  try {
    window.localStorage.setItem("elsa-console-theme", theme);
  } catch {
    // Theme persistence is optional; the UI should continue to work without browser storage.
  }
}

function getStoredThemeAccent(): ThemeAccent {
  if (typeof window === "undefined" || typeof window.localStorage?.getItem !== "function") {
    return "teal";
  }

  try {
    return parseThemeAccent(window.localStorage.getItem(themeAccentStorageKey));
  } catch {
    return "teal";
  }
}

function storeThemeAccent(themeAccent: ThemeAccent) {
  if (typeof window === "undefined" || typeof window.localStorage?.setItem !== "function") {
    return;
  }

  try {
    window.localStorage.setItem(themeAccentStorageKey, themeAccent);
  } catch {
    // Theme persistence is optional; the UI should continue to work without browser storage.
  }
}

function parseThemeAccent(value: string | null): ThemeAccent {
  return themeAccents.some((themeAccent) => themeAccent.value === value) ? (value as ThemeAccent) : "teal";
}

function ThemeToggle({ theme, onThemeChange }: { theme: Theme; onThemeChange: (theme: Theme) => void }) {
  const nextTheme = theme === "dark" ? "light" : "dark";
  const Icon = theme === "dark" ? Sun : Moon;

  return (
    <button
      type="button"
      aria-label={`Switch to ${nextTheme} mode`}
      aria-pressed={theme === "dark"}
      className="inline-flex h-8 w-8 items-center justify-center rounded-ui border border-border bg-background text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
      onClick={() => onThemeChange(nextTheme)}
    >
      <Icon aria-hidden className="h-4 w-4" />
    </button>
  );
}

function ThemeAccentPicker({
  themeAccent,
  compact = false,
  onThemeAccentChange
}: {
  themeAccent: ThemeAccent;
  compact?: boolean;
  onThemeAccentChange: (themeAccent: ThemeAccent) => void;
}) {
  return (
    <label
      className={cn(
        "relative inline-flex h-8 items-center gap-1 rounded-ui border border-border bg-background px-2 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-within:outline focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-primary",
        compact ? "max-w-[7rem]" : "max-w-[8rem]"
      )}
      title="Theme accent"
    >
      <Palette aria-hidden className="h-4 w-4 shrink-0 text-primary" />
      <span className="min-w-0 flex-1 truncate text-xs font-medium text-foreground">
        {themeAccents.find((item) => item.value === themeAccent)?.label ?? "Teal"}
      </span>
      <select
        aria-label="Theme accent"
        className="theme-accent-picker-select absolute inset-0 h-full w-full cursor-pointer appearance-none rounded-ui bg-transparent opacity-0"
        value={themeAccent}
        onChange={(event) => onThemeAccentChange(parseThemeAccent(event.target.value))}
      >
        {themeAccents.map((item) => (
          <option key={item.value} value={item.value}>
            {item.label}
          </option>
        ))}
      </select>
      <ChevronDown aria-hidden className="h-3 w-3 shrink-0 text-muted-foreground" />
    </label>
  );
}

function WeaverTrigger({ compact = false, onClick }: { compact?: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      aria-label="Open Weaver assistant"
      className={cn(
        "inline-flex h-8 items-center justify-center gap-2 rounded-ui border border-border bg-background px-2 text-foreground transition-colors hover:bg-muted",
        compact ? "w-8 px-0" : "text-xs font-medium"
      )}
      onClick={onClick}
    >
      <Bot aria-hidden className="h-4 w-4 text-primary" />
      {compact ? null : <span>Weaver</span>}
    </button>
  );
}

function PrimaryNavigation({ compact = false }: { compact?: boolean }) {
  if (compact) {
    const compactItems = navSections.flatMap((section) => section.items.filter((item) => !item.disabled));

    return (
      <nav aria-label="Primary" className="flex gap-1 overflow-x-auto">
        {compactItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              cn(
                "whitespace-nowrap rounded-ui px-3 py-2 text-sm",
                isActive ? "border border-primary/20 bg-primary/10 text-foreground" : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )
            }
          >
            {item.label}
          </NavLink>
        ))}
      </nav>
    );
  }

  return (
    <nav aria-label="Primary" className="space-y-5">
      {navSections.map((section) => (
        <div key={section.label} className="space-y-1">
          <p className="px-3 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">{section.label}</p>
          {section.items.map((item) =>
            item.disabled ? (
              <span
                key={item.to}
                aria-disabled="true"
                className="flex cursor-not-allowed items-center gap-2 rounded-ui px-3 py-2 text-sm text-muted-foreground/60"
                title="Planned module"
              >
                <item.icon aria-hidden className="h-4 w-4" />
                <span className="min-w-0 flex-1 truncate">{item.label}</span>
                <span className="rounded-sm border border-border px-1.5 py-0.5 text-[10px] uppercase tracking-wide">Soon</span>
              </span>
            ) : (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  cn(
                    "flex items-center gap-2 rounded-ui px-3 py-2 text-sm transition-colors",
                    isActive
                      ? "border border-primary/20 bg-primary/10 text-foreground"
                      : "text-muted-foreground hover:bg-muted hover:text-foreground"
                  )
                }
              >
                <item.icon aria-hidden className="h-4 w-4" />
                {item.label}
              </NavLink>
            )
          )}
        </div>
      ))}
    </nav>
  );
}

function OrganizationWorkspaceSwitcher({ compact = false, className }: { compact?: boolean; className?: string }) {
  const auth = useAuth();
  const workspaceContext = useWorkspaceContext();

  if (!auth.session?.authenticated) return null;

  if (workspaceContext.isLoading) {
    return (
      <div className={cn("rounded-ui border border-border bg-background px-3 py-2 text-xs text-muted-foreground", className)}>
        Loading context
      </div>
    );
  }

  if (workspaceContext.isError || workspaceContext.organizations.length === 0) {
    return (
      <div className={cn("rounded-ui border border-border bg-background px-3 py-2 text-xs text-muted-foreground", className)}>
        No organization
      </div>
    );
  }

  return (
    <div className={cn("rounded-ui border border-border bg-background p-2", className)}>
      <div className={cn("flex gap-2", compact ? "items-center" : "flex-col")}>
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <Building2 aria-hidden className="h-4 w-4 shrink-0 text-primary" />
          <Select
            aria-label="Organization"
            className="min-w-0 flex-1"
            value={workspaceContext.selectedOrganizationId}
            onChange={(event) => workspaceContext.setSelectedOrganizationId(event.target.value)}
          >
            {workspaceContext.organizations.map((organization) => (
              <option key={organization.id} value={organization.id}>
                {organization.name}
              </option>
            ))}
          </Select>
        </div>
        <Select
          aria-label="Workspace"
          className="min-w-0 flex-1"
          value={workspaceContext.selectedWorkspaceId}
          onChange={(event) => workspaceContext.setSelectedWorkspaceId(event.target.value)}
        >
          {workspaceContext.organizationWorkspaces.map((workspace) => (
            <option key={workspace.id} value={workspace.id}>
              {workspace.name}
            </option>
          ))}
        </Select>
      </div>
    </div>
  );
}

function ApplicationBuildNumber({ className }: { className?: string }) {
  const { data } = useQuery({
    queryKey: queryKeys.application,
    queryFn: getApplicationInfo,
    staleTime: 300_000
  });

  if (!data?.buildNumber) {
    return null;
  }

  return (
    <p aria-label="Application build number" className={cn("truncate text-xs text-muted-foreground", className)}>
      Build {data.buildNumber}
    </p>
  );
}
