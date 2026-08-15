import pytest
from fastapi.testclient import TestClient
from fpdf import FPDF

from app.main import app


@pytest.fixture
def client() -> TestClient:
    return TestClient(app)


def make_pdf_bytes(*page_texts: str) -> bytes:
    """Builds a real, minimal PDF with one page per given text — used instead of a
    hand-crafted/binary fixture file so tests stay self-contained and easy to read."""
    pdf = FPDF()
    for text in page_texts:
        pdf.add_page()
        pdf.set_font("Helvetica", size=12)
        pdf.cell(text=text)
    return bytes(pdf.output())


@pytest.fixture
def pdf_bytes_factory():
    return make_pdf_bytes
