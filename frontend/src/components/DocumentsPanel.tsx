import type { DocumentResponse } from "../api/types";
import { formatRelativeTime } from "../lib/relativeTime";
import { Spinner } from "./Spinner";
import { StatusBadge } from "./StatusBadge";

interface DocumentsPanelProps {
  documents: DocumentResponse[];
  isLoading: boolean;
  error: string | null;
  onDelete: (id: string) => void;
}

export function DocumentsPanel({ documents, isLoading, error, onDelete }: DocumentsPanelProps) {
  return (
    <section className="flex h-64 flex-col rounded-xl border border-slate-200 bg-white shadow-sm">
      <header className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
        <div>
          <h2 className="text-sm font-semibold text-slate-900">Documents</h2>
          <p className="text-xs text-slate-500">Shared across all users</p>
        </div>
        {documents.length > 0 && (
          <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
            {documents.length}
          </span>
        )}
      </header>

      <div className="flex-1 overflow-y-auto px-2 py-2">
        {isLoading && (
          <div className="flex items-center gap-2 px-2 py-4 text-sm text-slate-400">
            <Spinner /> Loading…
          </div>
        )}

        {!isLoading && error && <p className="px-2 py-4 text-sm text-red-600">{error}</p>}

        {!isLoading && !error && documents.length === 0 && (
          <p className="px-2 py-4 text-sm text-slate-400">No documents uploaded yet.</p>
        )}

        <ul className="divide-y divide-slate-100">
          {documents.map((doc) => (
            <li key={doc.id} className="group flex items-center justify-between gap-2 px-2 py-2">
              <div className="min-w-0">
                <p className="truncate text-sm text-slate-800" title={doc.fileName}>
                  {doc.fileName}
                </p>
                <div className="mt-0.5 flex items-center gap-2">
                  <StatusBadge status={doc.status} />
                  <span className="text-xs text-slate-400">{formatRelativeTime(doc.createdAt)}</span>
                </div>
              </div>
              <button
                type="button"
                onClick={() => onDelete(doc.id)}
                aria-label={`Delete ${doc.fileName}`}
                className="shrink-0 rounded px-2 py-1 text-xs text-slate-400 opacity-0 transition hover:bg-red-50 hover:text-red-600 group-hover:opacity-100"
              >
                Delete
              </button>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
