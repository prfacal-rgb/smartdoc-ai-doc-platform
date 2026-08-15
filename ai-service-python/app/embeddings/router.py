import httpx
from fastapi import APIRouter, Depends, HTTPException

from app.embeddings.dependencies import get_embedding_provider
from app.embeddings.models import EmbedRequest, EmbedResponse
from app.embeddings.provider import EmbeddingProvider

router = APIRouter(tags=["embeddings"])


@router.post("/embed", response_model=EmbedResponse)
async def embed_texts(
    request: EmbedRequest,
    provider: EmbeddingProvider = Depends(get_embedding_provider),
) -> EmbedResponse:
    try:
        embeddings, model_used = await provider.embed(request.texts)
    except httpx.HTTPError as exc:
        # 502: the request to *us* was fine, the upstream embedding provider failed —
        # distinct from 400 (bad request) or a generic 500.
        raise HTTPException(status_code=502, detail=f"Embedding provider request failed: {exc}") from exc

    dimensions = len(embeddings[0]) if embeddings else 0
    return EmbedResponse(embeddings=embeddings, model=model_used, dimensions=dimensions)
