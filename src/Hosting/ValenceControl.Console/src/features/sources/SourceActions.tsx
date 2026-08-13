import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Play, Power, RefreshCw, Trash2 } from "lucide-react";
import { KeyboardEvent, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Button, DialogPanel, SecondaryButton, buttonClassName } from "@/components/ui";
import { deleteSource, setSourceEnabled, syncSource } from "@/features/sources/sourceApi";
import type { PackageSource } from "@/features/sources/sourceModels";
import { queryKeys } from "@/lib/query/queryClient";

type SourceActionsProps = {
  source: PackageSource;
  onDeleted?: () => void;
  onSyncStateChange?: (sourceId: string, isSyncing: boolean) => void;
  showEdit?: boolean;
};

export function SourceActions({ source, onDeleted, onSyncStateChange, showEdit = true }: SourceActionsProps) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const queryClient = useQueryClient();
  const invalidate = () => queryClient.invalidateQueries({ queryKey: queryKeys.sources });
  const sync = useMutation({
    mutationFn: () => syncSource(source.id),
    onMutate: () => onSyncStateChange?.(source.id, true),
    onSettled: () => onSyncStateChange?.(source.id, false),
    onSuccess: invalidate
  });
  const toggle = useMutation({ mutationFn: () => setSourceEnabled(source, !source.enabled), onSuccess: invalidate });
  const remove = useMutation({
    mutationFn: () => deleteSource(source.id),
    onSuccess: () => {
      invalidate();
      onDeleted?.();
    }
  });
  const syncInProgress = source.isSyncing || sync.isPending;

  useEffect(() => {
    if (confirmingDelete) cancelButtonRef.current?.focus();
  }, [confirmingDelete]);

  function closeDialog() {
    if (!remove.isPending) setConfirmingDelete(false);
  }

  function handleDialogKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") closeDialog();
    if (event.key !== "Tab") return;

    const focusable = Array.from(event.currentTarget.querySelectorAll<HTMLElement>("button:not(:disabled), [href], input, select, textarea, [tabindex]:not([tabindex='-1'])"));
    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      {showEdit ? (
        <Link className={buttonClassName("secondary")} to={`/admin/sources/${source.id}/edit`} title="Edit source">
          <Pencil className="h-4 w-4" />
          Edit
        </Link>
      ) : null}
      <SecondaryButton onClick={() => sync.mutate()} disabled={syncInProgress} title={syncInProgress ? "Sync in progress" : "Sync now"}>
        {syncInProgress ? <RefreshCw className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
        {syncInProgress ? "Syncing" : "Sync"}
      </SecondaryButton>
      <SecondaryButton onClick={() => toggle.mutate()} disabled={toggle.isPending} title={source.enabled ? "Disable source" : "Enable source"}>
        <Power className="h-4 w-4" />
        {source.enabled ? "Disable" : "Enable"}
      </SecondaryButton>
      <SecondaryButton onClick={() => setConfirmingDelete(true)} className="text-destructive" title="Soft-delete source">
        <Trash2 className="h-4 w-4" />
        Delete
      </SecondaryButton>
      {confirmingDelete ? (
        <div className="fixed inset-0 z-20 flex items-center justify-center bg-background/70 p-4" onMouseDown={closeDialog}>
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-source-title"
            onKeyDown={handleDialogKeyDown}
            onMouseDown={(event) => event.stopPropagation()}
          >
            <DialogPanel>
            <div className="max-w-sm space-y-4">
              <div>
                <h2 id="delete-source-title" className="font-medium">Delete {source.name}?</h2>
                <p className="mt-1 text-sm text-muted-foreground">The source is hidden from admin reads and syncs, but package history is preserved.</p>
              </div>
              <div className="flex justify-end gap-2">
                <SecondaryButton ref={cancelButtonRef} onClick={closeDialog}>Cancel</SecondaryButton>
                <Button onClick={() => remove.mutate()} disabled={remove.isPending} className="bg-destructive text-white">
                  Delete
                </Button>
              </div>
            </div>
            </DialogPanel>
          </div>
        </div>
      ) : null}
    </div>
  );
}
