from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Loaded from environment variables (see CLAUDE.md §"Variables de entorno")."""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    ai_service_port: int = 8000

    # Chunking defaults — see PROJECT.md §7 (RAG parameters): chunk size ~500 tokens,
    # overlap ~50-100 tokens. Token counts are estimated with tiktoken (cl100k_base) as a
    # provider-agnostic approximation — no embedding model here uses that exact tokenizer,
    # but it is a well-established stand-in for "roughly how many tokens is this text".
    default_chunk_size_tokens: int = 500
    default_chunk_overlap_tokens: int = 75

    embedding_provider: str = "ollama"
    ollama_base_url: str = "http://localhost:11434/v1"
    ollama_embedding_model: str = "nomic-embed-text"

    # Chosen over local Ollama for /generate: measured 40s+ for a 35-token completion with
    # qwen2.5-coder:14b on the physical host, vs. ~0.5s end-to-end on Groq (see ADR 0014) —
    # /generate is synchronous/user-facing, unlike /embed which runs in the background Worker.
    llm_provider: str = "groq"
    groq_api_key: str = ""
    # llama-3.3-70b-versatile (used when this was first wired up, ADR 0014) was retired from
    # Groq's catalog since — model availability on hosted providers isn't permanent, worth
    # remembering when picking one. openai/gpt-oss-120b confirmed working: ~0.74s, good
    # quality, full completion (gpt-oss-20b truncated oddly in the same test).
    groq_model: str = "openai/gpt-oss-120b"
    groq_base_url: str = "https://api.groq.com/openai/v1"


settings = Settings()
