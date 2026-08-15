from pydantic import BaseModel, Field


class ParsedPage(BaseModel):
    page_number: int = Field(ge=1)
    text: str


class ParseResponse(BaseModel):
    pages: list[ParsedPage]
