import io

import pypdf
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


def make_encrypted_pdf_bytes(pdf_bytes: bytes, *, user_password: str, owner_password: str) -> bytes:
    """Re-encrypts already-built PDF bytes (see make_pdf_bytes) with pypdf.PdfWriter — used to
    build both flavors of "protected" PDF that ADR 0021 distinguishes: user_password="" (opens
    in any reader without prompting, only owner-level actions like printing are restricted) vs.
    a real user_password (genuinely requires a password to read)."""
    reader = pypdf.PdfReader(io.BytesIO(pdf_bytes))
    writer = pypdf.PdfWriter()
    for page in reader.pages:
        writer.add_page(page)
    writer.encrypt(user_password=user_password, owner_password=owner_password)

    buffer = io.BytesIO()
    writer.write(buffer)
    return buffer.getvalue()


@pytest.fixture
def encrypted_pdf_bytes_factory():
    return make_encrypted_pdf_bytes
