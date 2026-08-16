import httpx
import pytest

from app.llm.dependencies import get_llm_provider
from app.llm.provider import LlmProvider
from app.main import app


def test_generate_with_context_answers_grounded_in_it(client):
    """Hits the real Groq API — same policy as the .NET side's integration tests against
    real Postgres/MinIO, and as test_embeddings.py's real Ollama calls."""
    response = client.post(
        "/generate",
        json={
            "question": "What color is the sky in this document?",
            "context_chunks": ["The sky described in this document is bright orange at sunset."],
        },
    )

    assert response.status_code == 200
    body = response.json()
    assert "orange" in body["answer"].lower()
    assert body["model"]


def test_generate_with_no_context_still_returns_an_answer(client):
    response = client.post("/generate", json={"question": "hello", "context_chunks": []})

    assert response.status_code == 200
    assert response.json()["answer"]


def test_generate_with_empty_question_returns_422(client):
    response = client.post("/generate", json={"question": "", "context_chunks": []})

    assert response.status_code == 422


class _FailingProvider(LlmProvider):
    async def generate(self, messages: list[dict[str, str]]) -> tuple[str, str]:
        raise httpx.ConnectError("connection refused (simulated)")


def test_generate_when_provider_fails_returns_502(client):
    """Doesn't touch the real network — verifies the router's error translation via
    FastAPI's dependency override mechanism, same as test_embeddings.py."""
    app.dependency_overrides[get_llm_provider] = lambda: _FailingProvider()
    try:
        response = client.post("/generate", json={"question": "anything", "context_chunks": []})
    finally:
        app.dependency_overrides.clear()

    assert response.status_code == 502
