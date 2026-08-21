import httpx

from app.embeddings.provider import EmbeddingProvider


class OllamaEmbeddingProvider(EmbeddingProvider):
    """Talks to Ollama's OpenAI-compatible /v1/embeddings endpoint. Ollama runs on the
    physical host, not this container or the VM — see ADR 0011/0012 for the connectivity
    story."""

    def __init__(self, base_url: str, model: str):
        self._base_url = base_url.rstrip("/")
        self._model = model

    async def embed(self, texts: list[str]) -> tuple[list[list[float]], str]:
        # A single call batches every chunk of a document at once. nomic-embed-text measured
        # ~600-700ms/chunk (ADR 0021); bge-m3 (ADR 0026) measured ~1835ms/chunk on a real
        # 409-chunk document - 750s (12.5 min) for that one document alone. Matches the
        # ceiling on the .NET -> ai-service leg (see AiServiceClient's HttpClient.Timeout) so
        # this inner leg isn't the one cutting a legitimately-slow-but-working call short.
        async with httpx.AsyncClient(timeout=1800.0) as client:
            response = await client.post(
                f"{self._base_url}/embeddings",
                json={"model": self._model, "input": texts},
            )
            response.raise_for_status()
            data = response.json()

        embeddings = [item["embedding"] for item in data["data"]]
        model_used = data.get("model") or self._model
        return embeddings, model_used
