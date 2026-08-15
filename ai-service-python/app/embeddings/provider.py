from abc import ABC, abstractmethod


class EmbeddingProvider(ABC):
    """Port for embedding generation — the Python-side equivalent of CLAUDE.md §3's
    IEmbeddingProvider. .NET never talks to Ollama/OpenAI directly; it only calls this
    service's /embed endpoint, which delegates to whichever provider is configured here."""

    @abstractmethod
    async def embed(self, texts: list[str]) -> tuple[list[list[float]], str]:
        """Returns (embeddings, model name actually used) — the model name is threaded back
        into the response so callers can record which model generated each vector (see
        DocumentChunk.EmbeddingModel on the .NET side)."""
        raise NotImplementedError
