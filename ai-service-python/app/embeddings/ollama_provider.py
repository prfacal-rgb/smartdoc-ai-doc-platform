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
        # A single call batches every chunk of a document at once (ADR 0012's own numbers -
        # ~0.14-0.66s per call - were measured on small requests, not a ~100+ chunk batch
        # from a large PDF; ADR 0021 measured ~600-700ms/chunk in practice). Matches the
        # ceiling on the .NET -> ai-service leg (see AiServiceClient's HttpClient.Timeout) so
        # this inner leg isn't the one cutting a legitimately-slow-but-working call short.
        async with httpx.AsyncClient(timeout=600.0) as client:
            response = await client.post(
                f"{self._base_url}/embeddings",
                json={"model": self._model, "input": texts},
            )
            response.raise_for_status()
            data = response.json()

        embeddings = [item["embedding"] for item in data["data"]]
        model_used = data.get("model") or self._model
        return embeddings, model_used
