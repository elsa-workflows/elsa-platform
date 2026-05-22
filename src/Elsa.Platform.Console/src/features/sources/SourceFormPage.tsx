import { useQuery } from "@tanstack/react-query";
import { useParams } from "react-router-dom";
import { RequestStateView } from "@/components/states/RequestStateViews";
import { SourceForm } from "@/features/sources/SourceForm";
import { createSource, getSource, updateSource } from "@/features/sources/sourceApi";
import { queryKeys } from "@/lib/query/queryClient";

export function NewSourcePage() {
  return <SourceForm onSubmit={createSource} />;
}

export function EditSourcePage() {
  const { sourceId } = useParams();
  const source = useQuery({ queryKey: [...queryKeys.sources, sourceId], queryFn: () => getSource(sourceId!), enabled: Boolean(sourceId) });

  if (source.isLoading) return <RequestStateView state="loading" title="Loading source" />;
  if (source.isError || !source.data) return <RequestStateView state="not-found" title="Source not found" />;

  return <SourceForm source={source.data} onSubmit={(values) => updateSource(source.data.id, values)} />;
}
