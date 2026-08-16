from app.config import settings
from app.llm.groq_provider import GroqLlmProvider
from app.llm.provider import LlmProvider


def get_llm_provider() -> LlmProvider:
    if settings.llm_provider == "groq":
        if not settings.groq_api_key:
            raise RuntimeError("GROQ_API_KEY is not configured.")
        return GroqLlmProvider(settings.groq_api_key, settings.groq_model, settings.groq_base_url)

    # Only Groq is wired up so far (see ADR 0014 — chosen for /generate specifically because
    # it's synchronous/user-facing, unlike /embed; local Ollama measured 40s+ for a
    # 35-token completion, unusable for a chat endpoint). No auto-fallback between
    # providers by design (CLAUDE.md scope guard).
    raise NotImplementedError(f"LLM provider '{settings.llm_provider}' is not implemented.")
