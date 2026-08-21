import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import type { ChatMessage } from "../hooks/useChat";

interface ChatPanelProps {
  messages: ChatMessage[];
  isAsking: boolean;
  error: string | null;
  onAsk: (question: string) => void;
}

export function ChatPanel({ messages, isAsking, error, onAsk }: ChatPanelProps) {
  const [question, setQuestion] = useState("");
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [messages]);

  function submit() {
    const trimmed = question.trim();
    if (!trimmed || isAsking) return;
    onAsk(trimmed);
    setQuestion("");
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      submit();
    }
  }

  return (
    <section className="flex h-full flex-col rounded-xl border border-slate-200 bg-white shadow-sm">
      <header className="border-b border-slate-200 px-4 py-3">
        <h2 className="text-sm font-semibold text-slate-900">Ask a question</h2>
        <p className="text-xs text-slate-500">Answers are grounded in your uploaded documents, with citations.</p>
      </header>

      <div ref={scrollRef} className="flex-1 space-y-4 overflow-y-auto px-4 py-4">
        {messages.length === 0 && (
          <p className="text-sm text-slate-400">
            Upload a document and ask a question about it — the answer will cite the file and page it came from.
          </p>
        )}

        {messages.map((message) => (
          <div key={message.id} className={`flex ${message.role === "user" ? "justify-end" : "justify-start"}`}>
            <div
              className={`max-w-[80%] rounded-lg px-3 py-2 text-sm ${
                message.role === "user" ? "bg-slate-900 text-white" : "bg-slate-100 text-slate-800"
              }`}
            >
              <p className="whitespace-pre-wrap">{message.content}</p>
              {message.sources && message.sources.length > 0 && (
                <ul className="mt-2 space-y-0.5 border-t border-slate-300/50 pt-2 text-xs text-slate-500">
                  {message.sources.map((source, index) => (
                    <li key={index}>
                      {source.fileName} — page {source.pageNumber}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        ))}

        {isAsking && <p className="text-sm text-slate-400">Thinking…</p>}
      </div>

      {error && <p className="border-t border-slate-200 px-4 py-2 text-sm text-red-600">{error}</p>}

      <div className="flex items-end gap-2 border-t border-slate-200 p-3">
        <textarea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={handleKeyDown}
          rows={1}
          placeholder="Ask a question about your documents…"
          className="max-h-32 flex-1 resize-none rounded-md border border-slate-300 px-3 py-2 text-sm outline-none focus:border-slate-500 focus:ring-1 focus:ring-slate-500"
        />
        <button
          type="button"
          onClick={submit}
          disabled={isAsking || !question.trim()}
          className="rounded-md bg-slate-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          Ask
        </button>
      </div>
    </section>
  );
}
