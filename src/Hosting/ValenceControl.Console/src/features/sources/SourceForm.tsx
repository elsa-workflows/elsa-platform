import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button, Input, SecondaryButton, Select } from "@/components/ui";
import { previewPatterns } from "@/features/sources/patternTester";
import type { PackageSource, SourceFormValues } from "@/features/sources/sourceModels";
import { splitPatterns, toSourceFormValues } from "@/features/sources/sourceModels";
import { queryKeys } from "@/lib/query/queryClient";

const samplePackageIds = ["Elsa.Persistence.PostgreSql", "Elsa.Messaging.RabbitMQ", "Elsa.Tests", "Elsa.Abstractions"];

export function SourceForm({
  source,
  onSubmit
}: {
  source?: PackageSource;
  onSubmit: (values: SourceFormValues) => Promise<unknown>;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [values, setValues] = useState<SourceFormValues>(() => toSourceFormValues(source));
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const preview = previewPatterns(splitPatterns(values.includePatterns), splitPatterns(values.excludePatterns), samplePackageIds);

  async function submit() {
    setError(null);
    if (!values.name.trim() || !values.url.trim()) {
      setError("Name and feed URL are required.");
      return;
    }
    setSaving(true);
    try {
      await onSubmit(values);
      await queryClient.invalidateQueries({ queryKey: queryKeys.sources });
      navigate("/admin/sources");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Source could not be saved.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="max-w-3xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold">{source ? "Edit Source" : "New Source"}</h1>
        <p className="mt-1 text-sm text-muted-foreground">Keep feeds narrow and explicit so indexing stays predictable.</p>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        <label className="space-y-1 text-sm">
          <span>Name</span>
          <Input value={values.name} onChange={(event) => setValues({ ...values, name: event.target.value })} />
        </label>
        <label className="space-y-1 text-sm">
          <span>Feed URL</span>
          <Input value={values.url} onChange={(event) => setValues({ ...values, url: event.target.value })} />
        </label>
        <label className="space-y-1 text-sm">
          <span>Approval Policy</span>
          <Select value={values.approvalPolicy} onChange={(event) => setValues({ ...values, approvalPolicy: event.target.value as SourceFormValues["approvalPolicy"] })}>
            <option value="Manual">Manual</option>
            <option value="AutoApprove">Auto approve</option>
          </Select>
        </label>
        <label className="space-y-1 text-sm">
          <span>Version Discovery</span>
          <Select value={values.versionDiscoveryPolicy} onChange={(event) => setValues({ ...values, versionDiscoveryPolicy: event.target.value as SourceFormValues["versionDiscoveryPolicy"] })}>
            <option value="AllVersions">All versions</option>
            <option value="LatestStable">Latest stable</option>
            <option value="LatestIncludingPrerelease">Latest incl. previews</option>
            <option value="LatestPreview">Latest preview only</option>
          </Select>
        </label>
        <label className="space-y-1 text-sm">
          <span>Polling Interval</span>
          <Input value={values.pollingInterval} onChange={(event) => setValues({ ...values, pollingInterval: event.target.value })} placeholder="PT30M" />
        </label>
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={values.enabled} onChange={(event) => setValues({ ...values, enabled: event.target.checked })} />
        Enabled
      </label>
      <div className="grid gap-4 md:grid-cols-2">
        <label className="space-y-1 text-sm">
          <span>Include Patterns</span>
          <textarea className="min-h-32 w-full rounded-ui border border-border bg-background p-3 font-mono text-sm" value={values.includePatterns} onChange={(event) => setValues({ ...values, includePatterns: event.target.value })} />
        </label>
        <label className="space-y-1 text-sm">
          <span>Exclude Patterns</span>
          <textarea className="min-h-32 w-full rounded-ui border border-border bg-background p-3 font-mono text-sm" value={values.excludePatterns} onChange={(event) => setValues({ ...values, excludePatterns: event.target.value })} />
        </label>
      </div>
      <div className="rounded-ui border border-border p-4">
        <h2 className="text-sm font-medium">Pattern tester</h2>
        <div className="mt-3 grid gap-2 text-sm">
          {preview.map((item) => (
            <div key={item.packageId} className="flex items-center gap-2 font-mono">
              <span className={item.included ? "text-success" : "text-destructive"}>{item.included ? "OK" : "NO"}</span>
              <span>{item.packageId}</span>
            </div>
          ))}
        </div>
      </div>
      {error ? <p role="alert" className="text-sm text-destructive">{error}</p> : null}
      <div className="flex gap-2">
        <Button onClick={submit} disabled={saving}>{saving ? "Saving..." : "Save Source"}</Button>
        <SecondaryButton type="button" onClick={() => navigate("/admin/sources")}>Cancel</SecondaryButton>
      </div>
    </section>
  );
}
