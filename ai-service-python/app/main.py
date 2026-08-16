from fastapi import FastAPI

from app.chunking.router import router as chunking_router
from app.embeddings.router import router as embeddings_router
from app.llm.router import router as llm_router
from app.parsing.router import router as parsing_router

app = FastAPI(
    title="SmartDoc AI Service",
    description="Stateless PDF parsing, chunking and embeddings service (see PROJECT.md §4.D).",
)

app.include_router(parsing_router)
app.include_router(chunking_router)
app.include_router(embeddings_router)
app.include_router(llm_router)


@app.get("/health")
async def health() -> dict[str, str]:
    return {"status": "ok"}
