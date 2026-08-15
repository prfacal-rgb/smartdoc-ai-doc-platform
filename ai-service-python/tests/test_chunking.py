def test_chunk_single_page_short_text_yields_one_chunk(client):
    payload = {
        "pages": [{"page_number": 1, "text": "This is a short test sentence."}],
        "chunk_size_tokens": 500,
        "overlap_tokens": 50,
    }

    response = client.post("/chunk", json=payload)

    assert response.status_code == 200
    chunks = response.json()["chunks"]
    assert len(chunks) == 1
    assert chunks[0]["page_number"] == 1
    assert chunks[0]["chunk_index"] == 0


def test_chunk_long_page_text_yields_multiple_chunks(client):
    payload = {
        "pages": [{"page_number": 1, "text": "word " * 1000}],
        "chunk_size_tokens": 50,
        "overlap_tokens": 10,
    }

    response = client.post("/chunk", json=payload)

    assert response.status_code == 200
    chunks = response.json()["chunks"]
    assert len(chunks) > 1
    assert all(c["page_number"] == 1 for c in chunks)
    assert [c["chunk_index"] for c in chunks] == list(range(len(chunks)))


def test_chunk_never_crosses_page_boundaries(client):
    payload = {
        "pages": [
            {"page_number": 1, "text": "alpha " * 200},
            {"page_number": 2, "text": "beta " * 200},
        ],
        "chunk_size_tokens": 50,
        "overlap_tokens": 10,
    }

    response = client.post("/chunk", json=payload)

    chunks = response.json()["chunks"]
    assert len(chunks) > 2  # sanity: both pages actually produced multiple chunks
    for chunk in chunks:
        if chunk["page_number"] == 1:
            assert "beta" not in chunk["text"]
        else:
            assert "alpha" not in chunk["text"]


def test_chunk_with_empty_page_text_produces_no_chunks_for_that_page(client):
    payload = {
        "pages": [{"page_number": 1, "text": ""}],
        "chunk_size_tokens": 500,
        "overlap_tokens": 50,
    }

    response = client.post("/chunk", json=payload)

    assert response.status_code == 200
    assert response.json()["chunks"] == []


def test_chunk_with_overlap_greater_than_or_equal_to_chunk_size_returns_400(client):
    payload = {
        "pages": [{"page_number": 1, "text": "some text"}],
        "chunk_size_tokens": 50,
        "overlap_tokens": 50,
    }

    response = client.post("/chunk", json=payload)

    assert response.status_code == 400


def test_chunk_uses_default_sizes_when_not_specified(client):
    payload = {"pages": [{"page_number": 1, "text": "short text"}]}

    response = client.post("/chunk", json=payload)

    assert response.status_code == 200
