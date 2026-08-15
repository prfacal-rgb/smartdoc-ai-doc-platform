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
