from pydantic import BaseModel, Field

from app.config import settings
from app.parsing.models import ParsedPage


class ChunkRequest(BaseModel):
    pages: list[ParsedPage]
    chunk_size_tokens: int = Field(default=settings.default_chunk_size_tokens, gt=0)
    overlap_tokens: int = Field(default=settings.default_chunk_overlap_tokens, ge=0)


class Chunk(BaseModel):
    chunk_index: int
    page_number: int
    text: str


class ChunkResponse(BaseModel):
    chunks: list[Chunk]
