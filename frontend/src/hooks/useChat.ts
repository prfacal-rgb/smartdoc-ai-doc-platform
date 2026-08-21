import { useCallback, useState } from "react";
import { postChat, ApiError } from "../api/client";
import type { Citation } from "../api/types";

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
 */
export function useChat(token: string | null, onUnauthorized: () => void) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [conversationId, setConversationId] = useState<string | undefined>(undefined);
  const [isAsking, setIsAsking] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
