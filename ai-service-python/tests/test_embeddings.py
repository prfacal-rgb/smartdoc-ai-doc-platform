import httpx
import pytest

from app.embeddings.dependencies import get_embedding_provider
from app.embeddings.provider import EmbeddingProvider
from app.main import app


def test_embed_single_text_returns_1024_dim_vector_and_model_name(client):
    """Hits the real Ollama instance (bge-m3 on the physical host, ADR 0026) — same policy
    as the .NET side's integration tests against real Postgres/MinIO."""
    response = client.post("/embed", json={"texts": ["Hello SmartDoc, this is a test."]})

    assert response.status_code == 200
    body = response.json()
    assert len(body["embeddings"]) == 1
    assert len(body["embeddings"][0]) == 1024
    assert body["dimensions"] == 1024
    assert body["model"] == "bge-m3"


def test_embed_multiple_texts_returns_one_embedding_per_text(client):
    texts = ["first chunk", "second chunk", "third chunk"]

    response = client.post("/embed", json={"texts": texts})

    assert response.status_code == 200
    body = response.json()
    assert len(body["embeddings"]) == len(texts)
    assert all(len(e) == 1024 for e in body["embeddings"])


def test_embed_with_empty_texts_list_returns_422(client):
    response = client.post("/embed", json={"texts": []})

    assert response.status_code == 422


class _FailingProvider(EmbeddingProvider):
    async def embed(self, texts: list[str]) -> tuple[list[list[float]], str]:
        raise httpx.ConnectError("connection refused (simulated)")


def test_embed_when_provider_fails_returns_502(client):
    """Doesn't touch the real network — verifies the router's error translation in
    isolation via FastAPI's dependency override mechanism."""
    app.dependency_overrides[get_embedding_provider] = lambda: _FailingProvider()
    try:
        response = client.post("/embed", json={"texts": ["anything"]})
    finally:
        app.dependency_overrides.clear()

    assert response.status_code == 502
