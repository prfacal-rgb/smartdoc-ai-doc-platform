from pydantic import BaseModel, Field


class GenerateRequest(BaseModel):
    question: str = Field(min_length=1)
    context_chunks: list[str] = Field(default_factory=list)


class GenerateResponse(BaseModel):
    answer: str
    model: str
