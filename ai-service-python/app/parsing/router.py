from fastapi import APIRouter, HTTPException, UploadFile

from app.parsing.models import ParseResponse
from app.parsing.pdf_parser import InvalidPdfError, extract_pages

router = APIRouter(tags=["parsing"])


@router.post("/parse", response_model=ParseResponse)
async def parse_pdf(file: UploadFile) -> ParseResponse:
    if file.content_type != "application/pdf":
        raise HTTPException(status_code=400, detail="Only application/pdf files are supported.")

    content = await file.read()
    if not content:
        raise HTTPException(status_code=400, detail="File is empty.")

    try:
        pages = extract_pages(content)
    except InvalidPdfError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    return ParseResponse(pages=pages)
