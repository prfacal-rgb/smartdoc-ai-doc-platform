import type { DocumentStatus } from "../api/types";

const STYLES: Record<DocumentStatus, string> = {
  Uploaded: "bg-slate-100 text-slate-600",
  Processing: "bg-amber-100 text-amber-700",
  Ready: "bg-emerald-100 text-emerald-700",
  Failed: "bg-red-100 text-red-700",
};

export function StatusBadge({ status }: { status: DocumentStatus }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${STYLES[status]}`}>
      {status}
    </span>
  );
}
