from app.config import settings
from app.embeddings.ollama_provider import OllamaEmbeddingProvider
from app.embeddings.provider import EmbeddingProvider


def get_embedding_provider() -> EmbeddingProvider:
    if settings.embedding_provider == "ollama":
        return OllamaEmbeddingProvider(settings.ollama_base_url, settings.ollama_embedding_model)

    # Only Ollama is wired up so far (see ADR 0012 — deliberately chosen over OpenAI/Gemini
    # given hardware/cost constraints, no auto-fallback between providers by design). Adding
    # another provider means a new EmbeddingProvider implementation here, not touching the
    # router or the .NET side.
    raise NotImplementedError(f"Embedding provider '{settings.embedding_provider}' is not implemented.")
