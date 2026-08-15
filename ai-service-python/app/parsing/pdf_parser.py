import io

from pypdf import PdfReader

from app.parsing.models import ParsedPage


class InvalidPdfError(Exception):
    """Raised when the given bytes are not a readable, unencrypted PDF."""


def extract_pages(pdf_bytes: bytes) -> list[ParsedPage]:
    """Extracts text per page. Text-based PDFs only — no OCR (a known MVP limitation,
    matching PROJECT.md's "PDF únicamente" scope; scanned/image-only PDFs will yield empty
    or near-empty page text)."""
    try:
        reader = PdfReader(io.BytesIO(pdf_bytes))
        if reader.is_encrypted:
            raise InvalidPdfError("PDF is encrypted; cannot extract text without a password.")
        pages = [
            ParsedPage(page_number=index, text=page.extract_text() or "")
            for index, page in enumerate(reader.pages, start=1)
        ]
    except InvalidPdfError:
        raise
    except Exception as exc:  # pypdf raises several distinct exception types for malformed input
        raise InvalidPdfError(f"Could not read PDF: {exc}") from exc

    if not pages:
        raise InvalidPdfError("PDF has no pages.")

    return pages
