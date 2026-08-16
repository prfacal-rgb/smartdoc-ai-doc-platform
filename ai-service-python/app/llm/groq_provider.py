import httpx

from app.llm.provider import LlmProvider


class GroqLlmProvider(LlmProvider):
    """Talks to Groq's OpenAI-compatible /chat/completions endpoint.

    Groq's API sits behind Cloudflare, which rejects requests that look bot-like — a bare
    request with no User-Agent was blocked outright (Cloudflare error 1010) during manual
    testing, before ever reaching Groq's own auth/model logic. A normal User-Agent is set
    explicitly to avoid that.
    """

    def __init__(self, api_key: str, model: str, base_url: str):
        self._api_key = api_key
        self._model = model
        self._base_url = base_url.rstrip("/")

    async def generate(self, messages: list[dict[str, str]]) -> tuple[str, str]:
        async with httpx.AsyncClient(timeout=60.0) as client:
            response = await client.post(
                f"{self._base_url}/chat/completions",
                json={"model": self._model, "messages": messages},
                headers={
                    "Authorization": f"Bearer {self._api_key}",
                    "User-Agent": "SmartDoc-AI-Service/0.1",
                },
            )
            response.raise_for_status()
            data = response.json()

        answer = data["choices"][0]["message"]["content"]
        model_used = data.get("model") or self._model
        return answer, model_used
