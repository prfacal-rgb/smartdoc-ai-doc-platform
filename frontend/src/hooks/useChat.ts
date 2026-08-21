import { useCallback, useEffect, useRef, useState } from "react";
import { getConversation, postChat, ApiError } from "../api/client";
import type { Citation } from "../api/types";

const STORAGE_KEY = "smartdoc.conversationId";

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources?: Citation[];
}

/**
 * One continuous conversation per browser session — reuses the same conversationId across
 * questions (see POST /api/chat's ConversationId) instead of exposing a conversation
 * switcher, which the requested layout (one question box, one answer area) doesn't call for.
 *
 * The id is persisted to localStorage so a page refresh doesn't lose it - on mount, if one
 * is saved, the full message history is re-fetched from GET /api/chat/{id} (the source of
 * truth) rather than caching messages client-side, so there's nothing to keep in sync.
 * Historical messages come back as a single formatted string (prose + "Sources:", see
 * ADR 0015) rather than structured citations - rendered as-is via the same Markdown
 * rendering ChatPanel already does for live answers, instead of parsing that suffix back out.
 */
export function useChat(token: string | null, onUnauthorized: () => void) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [conversationId, setConversationId] = useState<string | undefined>(undefined);
  const [isAsking, setIsAsking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const hasHydrated = useRef(false);

  useEffect(() => {
    if (!token || hasHydrated.current) return;
    hasHydrated.current = true;

    const savedConversationId = localStorage.getItem(STORAGE_KEY);
    if (!savedConversationId) return;

    getConversation(token, savedConversationId)
      .then((history) => {
        setConversationId(history.conversationId);
        setMessages(
          history.messages.map((m) => ({
            id: m.id,
            role: m.role === "User" ? "user" : "assistant",
            content: m.content,
          })),
        );
      })
      .catch((err) => {
        // Gone, or belongs to a different login (404 either way, ChatEndpoints doesn't
        // distinguish) - drop the stale id and start a fresh conversation instead of
        // surfacing an error for something the user never asked about.
        if (err instanceof ApiError && err.status === 401) {
          onUnauthorized();
          return;
        }
        localStorage.removeItem(STORAGE_KEY);
      });
  }, [token, onUnauthorized]);

  const ask = useCallback(
    async (question: string) => {
      if (!token || !question.trim()) return;

      const userMessage: ChatMessage = { id: crypto.randomUUID(), role: "user", content: question };
      setMessages((prev) => [...prev, userMessage]);
      setIsAsking(true);
      setError(null);

      try {
        const response = await postChat(token, question, conversationId);
        setConversationId(response.conversationId);
        localStorage.setItem(STORAGE_KEY, response.conversationId);
        setMessages((prev) => [
          ...prev,
          { id: crypto.randomUUID(), role: "assistant", content: response.answer, sources: response.sources },
        ]);
      } catch (err) {
        if (err instanceof ApiError && err.status === 401) {
          onUnauthorized();
          return;
        }
        setError(err instanceof Error ? err.message : "Failed to get an answer.");
      } finally {
        setIsAsking(false);
      }
    },
    [token, conversationId, onUnauthorized],
  );

  return { messages, isAsking, error, ask };
}
