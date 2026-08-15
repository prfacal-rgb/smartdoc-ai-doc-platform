from fastapi import APIRouter, HTTPException

from app.chunking.chunker import chunk_pages
from app.chunking.models import ChunkRequest, ChunkResponse

router = APIRouter(tags=["chunking"])


@router.post("/chunk", response_model=ChunkResponse)
async def chunk_document(request: ChunkRequest) -> ChunkResponse:
    try:
        chunks = chunk_pages(request.pages, request.chunk_size_tokens, request.overlap_tokens)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    return ChunkResponse(chunks=chunks)
