from abc import ABC, abstractmethod


class LlmProvider(ABC):
    """Port for text generation — the Python-side equivalent of CLAUDE.md §3's
    ILlmProvider. .NET never talks to Groq/Anthropic/etc. directly; it only calls this
    service's /generate endpoint with the question and the context it already retrieved."""

    @abstractmethod
    async def generate(self, messages: list[dict[str, str]]) -> tuple[str, str]:
        """Returns (generated text, model name actually used)."""
        raise NotImplementedError
