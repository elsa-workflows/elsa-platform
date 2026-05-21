import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  Activity,
  Archive,
  Boxes,
  Cloud,
  DatabaseZap,
  FileClock,
  Gauge,
  Home,
  Layers3,
  Moon,
  PackageSearch,
  Rocket,
  ShieldCheck,
  Sun,
  TerminalSquare
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { NavLink, Outlet } from "react-router-dom";
import { getApplicationInfo } from "@/app/applicationApi";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";

type NavItem = {
  to: string;
  label: string;
  icon: LucideIcon;
  disabled?: boolean;
};

const navSections: Array<{ label: string; items: NavItem[] }> = [
  {
    label: "Platform",
    items: [
      { to: "/admin/overview", label: "Overview", icon: Home },
      { to: "/admin/deployments", label: "Deployments", icon: Rocket, disabled: true },
      { to: "/admin/artifacts", label: "Artifacts", icon: Archive, disabled: true },
      { to: "/admin/runtime-builder", label: "Runtime Builder", icon: Layers3 }
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
    label: "Operations",
    items: [
      { to: "/admin/targets", label: "Targets", icon: Cloud, disabled: true },
      { to: "/admin/runtimes", label: "Managed Runtimes", icon: Gauge, disabled: true },
      { to: "/admin/operations", label: "Runtime Operations", icon: Activity, disabled: true },
      { to: "/admin/audit", label: "Audit", icon: FileClock, disabled: true }
    ]
  }
];

type Theme = "light" | "dark";

export function AppShell() {
  const [theme, setTheme] = useTheme();

  return (
    <div className="min-h-screen bg-background text-foreground">
      <aside className="fixed inset-y-0 left-0 hidden w-72 flex-col border-r border-border bg-surface px-3 py-4 md:flex">
        <div>
          <div className="flex items-start justify-between gap-3 px-2 pb-6">
            <div>
              <p className="font-display text-base font-semibold tracking-normal">Elsa Platform</p>
              <p className="text-xs text-muted-foreground">Admin Console</p>
            </div>
            <ThemeToggle theme={theme} onThemeChange={setTheme} />
          </div>
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
              <ThemeToggle theme={theme} onThemeChange={setTheme} />
            </div>
          </div>
          <PrimaryNavigation compact />
        </header>
        <header className="sticky top-0 z-10 hidden border-b border-border bg-background/95 px-8 py-3 backdrop-blur md:block">
          <div className="mx-auto flex max-w-7xl items-center justify-between gap-4">
            <div className="flex min-w-0 items-center gap-3 text-sm text-muted-foreground">
              <ShieldCheck aria-hidden className="h-4 w-4 text-primary" />
              <span className="truncate">Unified control plane for deployments, packages, runtimes, and operations</span>
            </div>
            <div className="flex items-center gap-2 text-xs text-muted-foreground">
              <TerminalSquare aria-hidden className="h-4 w-4" />
              <span>REST + SignalR ready</span>
              <ThemeToggle theme={theme} onThemeChange={setTheme} />
            </div>
          </div>
        </header>
        <main className="mx-auto max-w-7xl px-4 py-6 md:px-8">
          <Outlet />
        </main>
      </div>
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

function getStoredTheme(): Theme {
  if (typeof window === "undefined" || typeof window.localStorage?.getItem !== "function") {
    return "light";
  }

  try {
    return window.localStorage.getItem("elsa-admin-theme") === "dark" ? "dark" : "light";
  } catch {
    return "light";
  }
}

function storeTheme(theme: Theme) {
  if (typeof window === "undefined" || typeof window.localStorage?.setItem !== "function") {
    return;
  }

  try {
    window.localStorage.setItem("elsa-admin-theme", theme);
  } catch {
    // Theme persistence is optional; the UI should continue to work without browser storage.
  }
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

function PrimaryNavigation({ compact = false }: { compact?: boolean }) {
  if (compact) {
    const compactItems = navSections.flatMap((section) => section.items.filter((item) => !item.disabled));

    return (
      <nav aria-label="Primary" className="flex gap-1 overflow-x-auto">
        {compactItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
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
