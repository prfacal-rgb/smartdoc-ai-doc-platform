def test_parse_pdf_with_single_page_extracts_text(client, pdf_bytes_factory):
    pdf_bytes = pdf_bytes_factory("Hello SmartDoc")

    response = client.post(
        "/parse",
        files={"file": ("report.pdf", pdf_bytes, "application/pdf")},
    )

    assert response.status_code == 200
    body = response.json()
    assert len(body["pages"]) == 1
    assert body["pages"][0]["page_number"] == 1
    assert "Hello SmartDoc" in body["pages"][0]["text"]


def test_parse_pdf_with_multiple_pages_returns_one_entry_per_page(client, pdf_bytes_factory):
    pdf_bytes = pdf_bytes_factory("Page one text", "Page two text", "Page three text")

    response = client.post(
        "/parse",
        files={"file": ("report.pdf", pdf_bytes, "application/pdf")},
    )

    assert response.status_code == 200
    body = response.json()
    assert [p["page_number"] for p in body["pages"]] == [1, 2, 3]
    assert "Page two text" in body["pages"][1]["text"]


def test_parse_with_non_pdf_content_type_returns_400(client):
    response = client.post(
        "/parse",
        files={"file": ("report.txt", b"just some text", "text/plain")},
    )

    assert response.status_code == 400


def test_parse_with_empty_file_returns_400(client):
    response = client.post(
        "/parse",
        files={"file": ("report.pdf", b"", "application/pdf")},
    )

    assert response.status_code == 400


def test_parse_with_corrupt_pdf_bytes_returns_400(client):
    response = client.post(
        "/parse",
        files={"file": ("report.pdf", b"%PDF-1.4 not actually a real pdf structure", "application/pdf")},
    )

    assert response.status_code == 400


def test_parse_pdf_encrypted_with_empty_user_password_extracts_text(client, pdf_bytes_factory, encrypted_pdf_bytes_factory):
    # Regression test for ADR 0021: a PDF encrypted with an empty user password (owner
    # password set to restrict printing/copying, but no password needed to open/read it in
    # any normal viewer) used to be rejected outright. It should parse like any other PDF.
    plain_bytes = pdf_bytes_factory("Protected but readable content")
    encrypted_bytes = encrypted_pdf_bytes_factory(plain_bytes, user_password="", owner_password="owner-secret")

    response = client.post(
        "/parse",
        files={"file": ("protected.pdf", encrypted_bytes, "application/pdf")},
    )

    assert response.status_code == 200
    body = response.json()
    assert len(body["pages"]) == 1
    assert "Protected but readable content" in body["pages"][0]["text"]


def test_parse_pdf_encrypted_with_real_password_returns_400(client, pdf_bytes_factory, encrypted_pdf_bytes_factory):
    # The other half of the ADR 0021 fix: a PDF that genuinely requires a password to open
    # (non-empty user password) must still be rejected - decrypt("") is expected to fail.
    plain_bytes = pdf_bytes_factory("Truly locked content")
    encrypted_bytes = encrypted_pdf_bytes_factory(plain_bytes, user_password="real-password", owner_password="owner-secret")

    response = client.post(
        "/parse",
        files={"file": ("locked.pdf", encrypted_bytes, "application/pdf")},
    )

    assert response.status_code == 400
