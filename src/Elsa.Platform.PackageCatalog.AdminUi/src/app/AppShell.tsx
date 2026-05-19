import { useQuery } from "@tanstack/react-query";
import { Boxes, DatabaseZap, Home, PackageSearch } from "lucide-react";
import { NavLink, Outlet } from "react-router-dom";
import { getApplicationInfo } from "@/app/applicationApi";
import { queryKeys } from "@/lib/query/queryClient";
import { cn } from "@/lib/utils";

const navItems = [
  { to: "/admin/overview", label: "Overview", icon: Home },
  { to: "/admin/sources", label: "Sources", icon: DatabaseZap },
  { to: "/admin/packages", label: "Packages", icon: PackageSearch },
  { to: "/admin/sync-runs", label: "Sync Runs", icon: Boxes }
];

export function AppShell() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <aside className="fixed inset-y-0 left-0 hidden w-64 flex-col border-r border-border bg-surface px-3 py-4 md:flex">
        <div>
          <div className="px-2 pb-6">
            <p className="text-sm font-semibold">Elsa Package Catalog</p>
            <p className="text-xs text-muted-foreground">Admin</p>
          </div>
          <PrimaryNavigation />
        </div>
        <ApplicationBuildNumber className="mt-auto px-2 pt-4" />
      </aside>
      <div className="md:pl-64">
        <header className="sticky top-0 z-10 border-b border-border bg-background/95 px-4 py-3 backdrop-blur md:hidden">
          <div className="mb-2 flex items-center justify-between gap-3">
            <p className="text-sm font-semibold">Elsa Package Catalog</p>
            <ApplicationBuildNumber className="shrink-0 text-right" />
          </div>
          <PrimaryNavigation compact />
        </header>
        <main className="mx-auto max-w-7xl px-4 py-6 md:px-8">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

function PrimaryNavigation({ compact = false }: { compact?: boolean }) {
  return (
    <nav aria-label="Primary" className={compact ? "flex gap-1 overflow-x-auto" : "space-y-1"}>
      {navItems.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          className={({ isActive }) =>
            cn(
              compact
                ? "whitespace-nowrap rounded-ui px-3 py-2 text-sm"
                : "flex items-center gap-2 rounded-ui px-3 py-2 text-sm transition-colors",
              isActive ? "bg-muted text-foreground" : "text-muted-foreground hover:bg-muted hover:text-foreground"
            )
          }
        >
          {!compact && <item.icon aria-hidden className="h-4 w-4" />}
          {item.label}
        </NavLink>
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
