SYSTEM_PROMPT = (
    "You are a document assistant. Answer the user's question using ONLY the context "
    "provided below. If the context does not contain enough information to answer the "
    "question, say so clearly — do not guess, and do not use outside knowledge."
)


def build_messages(question: str, context_chunks: list[str]) -> list[dict[str, str]]:
    """Builds the chat messages sent to the LLM provider. Citations (file + page) are not
    included here — .NET already knows exactly which chunks it retrieved and appends the
    "Sources:" list deterministically from that metadata (PROJECT.md §7), rather than
    trusting the LLM to reproduce citations accurately in its own text."""
    context_text = (
        "\n\n".join(f"[{i + 1}] {chunk}" for i, chunk in enumerate(context_chunks))
        if context_chunks
        else "(no relevant context was found)"
    )

    return [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": f"Context:\n{context_text}\n\nQuestion: {question}"},
    ]
