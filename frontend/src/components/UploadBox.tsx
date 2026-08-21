import { useRef, useState, type DragEvent } from "react";

interface UploadBoxProps {
  isUploading: boolean;
  onUpload: (file: File) => Promise<void>;
}

const ACCEPTED_CONTENT_TYPE = "application/pdf";

export function UploadBox({ isUploading, onUpload }: UploadBoxProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  async function handleFile(file: File | undefined) {
    if (!file) return;
    // PDF-only in the MVP (see PROJECT.md §8) — checked here too so the user gets instant
    // feedback instead of waiting on a round trip to the Api's own validator.
    if (file.type !== ACCEPTED_CONTENT_TYPE) {
      setLocalError("Only PDF files are supported.");
      return;
    }
    setLocalError(null);
    try {
      await onUpload(file);
    } catch {
      // The parent hook already records the server-side error for display; nothing else to
      // do here besides not letting it bubble up as an unhandled rejection.
    }
  }

  function handleDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setIsDragging(false);
    void handleFile(event.dataTransfer.files[0]);
  }

  return (
    <section className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <h2 className="text-sm font-semibold text-slate-900">Upload a document</h2>

      <div
        onDragOver={(e) => {
          e.preventDefault();
          setIsDragging(true);
        }}
        onDragLeave={() => setIsDragging(false)}
        onDrop={handleDrop}
        onClick={() => inputRef.current?.click()}
        className={`mt-3 flex cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed px-4 py-6 text-center transition ${
          isDragging ? "border-slate-500 bg-slate-50" : "border-slate-300"
        }`}
      >
        <input
          ref={inputRef}
          type="file"
          accept="application/pdf"
          className="hidden"
          onChange={(e) => void handleFile(e.target.files?.[0])}
        />
        <p className="text-sm text-slate-600">
          {isUploading ? "Uploading…" : "Drag a PDF here, or click to browse"}
        </p>
        <p className="mt-1 text-xs text-slate-400">PDF only</p>
      </div>

      {localError && <p className="mt-2 text-sm text-red-600">{localError}</p>}
    </section>
  );
}
