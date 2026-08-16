import httpx
from fastapi import APIRouter, Depends, HTTPException

from app.llm.dependencies import get_llm_provider
from app.llm.models import GenerateRequest, GenerateResponse
from app.llm.prompt import build_messages
from app.llm.provider import LlmProvider

router = APIRouter(tags=["generation"])


@router.post("/generate", response_model=GenerateResponse)
async def generate_answer(
    request: GenerateRequest,
    provider: LlmProvider = Depends(get_llm_provider),
) -> GenerateResponse:
    messages = build_messages(request.question, request.context_chunks)

    try:
        answer, model_used = await provider.generate(messages)
    except httpx.HTTPError as exc:
        raise HTTPException(status_code=502, detail=f"LLM provider request failed: {exc}") from exc

    return GenerateResponse(answer=answer, model=model_used)
