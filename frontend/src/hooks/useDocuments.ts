import { useCallback, useEffect, useRef, useState } from "react";
import { deleteDocument, getDocuments, uploadDocument, ApiError } from "../api/client";
import type { DocumentResponse } from "../api/types";

const POLL_INTERVAL_MS = 4000;
const IN_FLIGHT_STATUSES = new Set(["Uploaded", "Processing"]);

/**
 * Loads the shared document list and keeps it fresh while any document is still being
 * processed by the Worker (see ADR 0009/0018) — polling stops on its own once every
 * document has settled into Ready/Failed, and resumes automatically after a new upload.
 */
export function useDocuments(token: string | null, onUnauthorized: () => void) {
  const [documents, setDocuments] = useState<DocumentResponse[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const fetchDocuments = useCallback(async () => {
    if (!token) return;
    try {
      const result = await getDocuments(token);
      setDocuments(result);
      setError(null);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onUnauthorized();
        return;
      }
      setError(err instanceof Error ? err.message : "Failed to load documents.");
    } finally {
      setIsLoading(false);
    }
  }, [token, onUnauthorized]);

  useEffect(() => {
    void fetchDocuments();
  }, [fetchDocuments]);

  useEffect(() => {
    const hasInFlightDocument = documents.some((doc) => IN_FLIGHT_STATUSES.has(doc.status));

    if (intervalRef.current) {
      clearInterval(intervalRef.current);
      intervalRef.current = null;
    }

    if (hasInFlightDocument) {
      intervalRef.current = setInterval(() => void fetchDocuments(), POLL_INTERVAL_MS);
    }

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [documents, fetchDocuments]);

  const upload = useCallback(
    async (file: File) => {
      if (!token) return;
      setIsUploading(true);
      setError(null);
      try {
        const created = await uploadDocument(token, file);
        setDocuments((prev) => [created, ...prev]);
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) {
          onUnauthorized();
          return;
        }
        setError(err instanceof Error ? err.message : "Upload failed.");
        throw err;
      } finally {
        setIsUploading(false);
      }
    },
    [token, onUnauthorized],
  );

  const remove = useCallback(
    async (id: string) => {
      if (!token) return;
      const previous = documents;
      // Optimistic removal — deleting is a common enough action that waiting a round trip
      // to update the list would feel sluggish; roll back on failure.
      setDocuments((prev) => prev.filter((doc) => doc.id !== id));
      try {
        await deleteDocument(token, id);
      } catch (err) {
        setDocuments(previous);
        if (err instanceof ApiError && err.status === 401) {
          onUnauthorized();
          return;
        }
        setError(err instanceof Error ? err.message : "Failed to delete document.");
      }
    },
    [token, documents, onUnauthorized],
  );

  return { documents, isLoading, error, isUploading, upload, remove };
}
