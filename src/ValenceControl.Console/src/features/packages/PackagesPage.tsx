import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, RefreshCw, Search, X } from "lucide-react";
import { useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Badge, Button, EmptyState, Input, SecondaryButton, Select, Table } from "@/components/ui";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { approvePackageVersion, listPackages, rejectPackageVersion } from "@/features/packages/packageApi";
import {
  hasSuspiciousChange,
  isPackageListed,
  latestVersion,
  packageApprovalStatus,
  packageValidationStatus,
  selectableLatestVersion,
  selectionKey,
  type CatalogPackage,
  type PackageFilter,
  type PackageSort,
  type SelectablePackageVersion
} from "@/features/packages/packageModels";
import { formatDateTime } from "@/lib/formatters";
import { queryKeys } from "@/lib/query/queryClient";
import { sourceStatusTone, statusToneClass } from "@/lib/status/statusBadges";

const filters: PackageFilter[] = ["All", "Pending", "Approved", "Rejected", "Invalid", "Suspicious", "Unlisted"];
const sorts: { value: PackageSort; label: string }[] = [
  { value: "packageId", label: "Package ID" },
  { value: "latestVersion", label: "Latest version" },
  { value: "approvalStatus", label: "Approval" },
  { value: "validationStatus", label: "Validation" },
  { value: "updatedAt", label: "Updated" }
];

export function PackagesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [selected, setSelected] = useState<Record<string, SelectablePackageVersion>>({});
  const [rejectionReason, setRejectionReason] = useState("");
  const queryClient = useQueryClient();
  const packages = useQuery({ queryKey: queryKeys.packages, queryFn: listPackages, refetchInterval: 30_000 });

  const filter = readFilter(searchParams.get("approval"));
  const sort = readSort(searchParams.get("sort"));
  const query = searchParams.get("q") ?? "";
  const selectedItems = Object.values(selected);

  const visiblePackages = useMemo(() => {
    return sortPackages(filterPackages(packages.data ?? [], query, filter), sort);
  }, [filter, packages.data, query, sort]);

  const approveSelected = useMutation({
    mutationFn: () => runBatch(selectedItems.map((item) => approvePackageVersion(item))),
    onSuccess: () => resetAfterMutation(queryClient, setSelected),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.packages });
    }
  });

  const rejectSelected = useMutation({
    mutationFn: () => runBatch(selectedItems.map((item) => rejectPackageVersion(item, rejectionReason))),
    onSuccess: () => {
      setRejectionReason("");
      resetAfterMutation(queryClient, setSelected);
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.packages });
    }
  });

  if (packages.isLoading) return <RequestStateView state="loading" title="Loading packages" />;
  if (packages.isError && !packages.data) return <RequestStateView state="unexpected" title="Packages could not load" />;

  const mutationError = approveSelected.isError || rejectSelected.isError;

  return (
    <section className="space-y-5">
      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
        <div>
          <h1 className="text-xl font-semibold">Packages</h1>
          <p className="mt-1 text-sm text-muted-foreground">Review indexed package versions, validation state, and approval readiness.</p>
        </div>
        <SecondaryButton onClick={() => packages.refetch()} title="Refresh packages">
          <RefreshCw className="h-4 w-4" />
          Refresh
        </SecondaryButton>
      </div>

      {packages.isRefetchError ? <RequestStateView state="stale" title="Showing last loaded packages" /> : null}
      {mutationError ? <RequestStateView state="unexpected" title="Package action failed" description="Refresh the package list and try the action again." /> : null}

      <div className="grid gap-3 lg:grid-cols-[minmax(16rem,26rem)_auto_auto] lg:items-center">
        <label className="relative block">
          <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={query} onChange={(event) => updateSearchParams(searchParams, setSearchParams, { q: event.target.value })} className="pl-9" placeholder="Search packages" />
        </label>
        <Select value={filter} onChange={(event) => updateSearchParams(searchParams, setSearchParams, { approval: event.target.value === "All" ? "" : event.target.value })} aria-label="Filter packages">
          {filters.map((item) => (
            <option key={item} value={item}>{item}</option>
          ))}
        </Select>
        <Select value={sort} onChange={(event) => updateSearchParams(searchParams, setSearchParams, { sort: event.target.value })} aria-label="Sort packages">
          {sorts.map((item) => (
            <option key={item.value} value={item.value}>{item.label}</option>
          ))}
        </Select>
      </div>

      {selectedItems.length > 0 ? (
        <div className="flex flex-col gap-3 rounded-ui border border-border bg-surface p-3 md:flex-row md:items-center md:justify-between">
          <p className="text-sm font-medium">{selectedItems.length} version{selectedItems.length === 1 ? "" : "s"} selected</p>
          <div className="flex flex-col gap-2 md:flex-row md:items-center">
            <Input value={rejectionReason} onChange={(event) => setRejectionReason(event.target.value)} placeholder="Rejection reason" aria-label="Rejection reason" />
            <Button onClick={() => approveSelected.mutate()} disabled={approveSelected.isPending || rejectSelected.isPending}>
              <Check className="h-4 w-4" />
              Approve Selected
            </Button>
            <SecondaryButton onClick={() => rejectSelected.mutate()} disabled={!rejectionReason.trim() || approveSelected.isPending || rejectSelected.isPending}>
              <X className="h-4 w-4" />
              Reject Selected
            </SecondaryButton>
          </div>
        </div>
      ) : null}

      {(packages.data ?? []).length === 0 ? (
        <EmptyState title="No indexed packages" description="Run a source sync to populate the catalog." />
      ) : visiblePackages.length === 0 ? (
        <EmptyState title="No matching packages" description="Clear the search or filters to see all indexed packages." />
      ) : (
        <Table>
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-muted/40 text-left text-xs uppercase text-muted-foreground">
              <tr>
                <th className="w-10 px-3 py-2"><span className="sr-only">Select</span></th>
                <th className="px-3 py-2">Package ID</th>
                <th className="px-3 py-2">Latest Version</th>
                <th className="px-3 py-2">Approval</th>
                <th className="px-3 py-2">Validation</th>
                <th className="px-3 py-2">Source</th>
                <th className="px-3 py-2">Visibility</th>
                <th className="px-3 py-2">Features</th>
                <th className="px-3 py-2">Updated</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {visiblePackages.map((packageItem) => (
                <PackageRow
                  key={packageItem.packageId}
                  packageItem={packageItem}
                  selected={selected}
                  onSelectionChange={setSelected}
                />
              ))}
            </tbody>
          </table>
        </Table>
      )}
    </section>
  );
}

function PackageRow({
  packageItem,
  selected,
  onSelectionChange
}: {
  packageItem: CatalogPackage;
  selected: Record<string, SelectablePackageVersion>;
  onSelectionChange: (value: Record<string, SelectablePackageVersion>) => void;
}) {
  const selectable = selectableLatestVersion(packageItem);
  const key = selectable ? selectionKey(selectable) : "";
  const approval = packageApprovalStatus(packageItem);
  const validation = packageValidationStatus(packageItem);
  const suspicious = hasSuspiciousChange(packageItem);

  return (
    <tr>
      <td className="px-3 py-3">
        <input
          type="checkbox"
          aria-label={`Select ${packageItem.packageId}`}
          className="h-4 w-4 rounded border-border"
          checked={key in selected}
          disabled={!selectable}
          onChange={(event) => {
            if (!selectable) return;
            const next = { ...selected };
            if (event.target.checked) next[key] = selectable;
            else delete next[key];
            onSelectionChange(next);
          }}
        />
      </td>
      <td className="px-3 py-3 font-medium">
        <Link to={`/admin/packages/${encodeURIComponent(packageItem.packageId)}`}>{packageItem.packageId}</Link>
      </td>
      <td className="px-3 py-3">{latestVersion(packageItem) ?? "-"}</td>
      <td className="px-3 py-3">
        <Badge className={statusToneClass(sourceStatusTone(approval))}>{approval}</Badge>
      </td>
      <td className="px-3 py-3">
        <div className="flex flex-wrap gap-1">
          <Badge className={statusToneClass(sourceStatusTone(validation))}>{validation}</Badge>
          {suspicious ? <Badge className={statusToneClass(sourceStatusTone("Suspicious"))}>Suspicious</Badge> : null}
        </div>
      </td>
      <td className="px-3 py-3 text-muted-foreground">{packageItem.sourceId ?? "-"}</td>
      <td className="px-3 py-3">{isPackageListed(packageItem) ? "Listed" : "Unlisted"}</td>
      <td className="px-3 py-3">{packageItem.featuresCount ?? "-"}</td>
      <td className="px-3 py-3">{formatDateTime(packageItem.updatedAt)}</td>
    </tr>
  );
}

function filterPackages(packages: CatalogPackage[], query: string, filter: PackageFilter) {
  const term = query.trim().toLowerCase();
  return packages.filter((packageItem) => {
    const matchesQuery = !term || `${packageItem.packageId} ${latestVersion(packageItem) ?? ""}`.toLowerCase().includes(term);
    if (!matchesQuery) return false;
    switch (filter) {
      case "Pending":
      case "Approved":
      case "Rejected":
        return packageApprovalStatus(packageItem) === filter;
      case "Invalid":
        return packageValidationStatus(packageItem) === "Invalid";
      case "Suspicious":
        return hasSuspiciousChange(packageItem);
      case "Unlisted":
        return !isPackageListed(packageItem);
      default:
        return true;
    }
  });
}

function sortPackages(packages: CatalogPackage[], sort: PackageSort) {
  return [...packages].sort((left, right) => {
    switch (sort) {
      case "latestVersion":
        return compareText(latestVersion(left), latestVersion(right));
      case "approvalStatus":
        return compareText(packageApprovalStatus(left), packageApprovalStatus(right));
      case "validationStatus":
        return compareText(packageValidationStatus(left), packageValidationStatus(right));
      case "updatedAt":
        return compareText(right.updatedAt, left.updatedAt);
      default:
        return compareText(left.packageId, right.packageId);
    }
  });
}

function compareText(left?: string | null, right?: string | null) {
  return (left ?? "").localeCompare(right ?? "", undefined, { numeric: true, sensitivity: "base" });
}

function readFilter(value: string | null): PackageFilter {
  return filters.includes(value as PackageFilter) ? (value as PackageFilter) : "All";
}

function readSort(value: string | null): PackageSort {
  return sorts.some((item) => item.value === value) ? (value as PackageSort) : "packageId";
}

function updateSearchParams(
  searchParams: URLSearchParams,
  setSearchParams: ReturnType<typeof useSearchParams>[1],
  updates: Record<string, string>
) {
  const next = new URLSearchParams(searchParams);
  Object.entries(updates).forEach(([key, value]) => {
    if (value) next.set(key, value);
    else next.delete(key);
  });
  setSearchParams(next, { replace: true });
}

function resetAfterMutation(queryClient: ReturnType<typeof useQueryClient>, setSelected: (value: Record<string, SelectablePackageVersion>) => void) {
  setSelected({});
  void queryClient.invalidateQueries({ queryKey: queryKeys.packages });
}

async function runBatch(promises: Promise<unknown>[]) {
  const results = await Promise.allSettled(promises);
  const failures = results.filter((result) => result.status === "rejected");
  if (failures.length > 0) {
    throw new Error(`${failures.length} package action${failures.length === 1 ? "" : "s"} failed.`);
  }
}
