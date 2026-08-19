import pytest


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


def test_chunk_with_whitespace_only_page_text_produces_no_chunks(client):
    # Regression test for ADR 0021: a page pypdf extracted only whitespace/control characters
    # from (e.g. a cover or image-only page) used to still produce a chunk once encoded/decoded
    # through tiktoken, which then crashed DocumentChunk's non-empty-text invariant on the .NET
    # side. Distinct from the pre-existing empty-string case above - "   \n\t " is non-empty but
    # still has nothing worth keeping. Verifies the observable outcome (no chunk makes it out of
    # a blank page), not which of the two guards in chunk_pages catches it - for this exact
    # all-whitespace input the window-level filter alone happens to be enough; the page-level
    # filter's own purpose (skip tokenizing a page that's trivially blank, and defend the case
    # where a future change makes the window-level filter insufficient on its own) isn't pinned
    # down by a black-box test through /chunk.
    payload = {
        "pages": [
            {"page_number": 1, "text": "   \n\t  "},
            {"page_number": 2, "text": "real content on this page"},
        ],
        "chunk_size_tokens": 500,
        "overlap_tokens": 50,
    }

    response = client.post("/chunk", json=payload)

    assert response.status_code == 200
    chunks = response.json()["chunks"]
    assert all(c["page_number"] != 1 for c in chunks)
    assert any(c["page_number"] == 2 for c in chunks)


def test_chunk_terminates_and_skips_windows_that_decode_to_only_whitespace(monkeypatch):
    # Regression test for the window-level filter in chunk_pages, and specifically for an
    # infinite-loop bug introduced (and caught before shipping) while adding it: the first
    # attempt at "skip a blank window" used `continue` before `start += step`, which would have
    # spun forever on exactly the input this test constructs. Bypasses the HTTP layer and calls
    # chunk_pages directly with tiktoken's decode() monkeypatched to always return whitespace -
    # forcing every window blank isn't reliably reproducible through real tokenization (BPE
    # tends to merge whitespace runs into whatever real-content token borders them). Runs on a
    # worker thread with a hard timeout so a regression fails loudly instead of hanging the
    # whole test run.
    import threading

    import app.chunking.chunker as chunker_module
    from app.chunking.chunker import chunk_pages
    from app.parsing.models import ParsedPage

    monkeypatch.setattr(chunker_module._encoding, "decode", lambda tokens: "   ")

    pages = [ParsedPage(page_number=1, text="some real content here that tokenizes to more than one window")]
    result: dict = {}

    # A plain daemon Thread, not ThreadPoolExecutor: concurrent.futures registers every worker
    # thread with a process-wide atexit hook that joins them unconditionally, so even
    # shutdown(wait=False) on a stuck executor still hangs the whole interpreter at exit - tried
    # that first, confirmed it hangs. A daemon thread carries no such hook; the process can exit
    # (and pytest can move on) while it's still spinning, abandoned.
    def run() -> None:
        result["chunks"] = chunk_pages(pages, 2, 0)

    thread = threading.Thread(target=run, daemon=True)
    thread.start()
    thread.join(timeout=5)

    if thread.is_alive():
        pytest.fail("chunk_pages did not terminate - regression of the infinite-loop bug fixed in ADR 0021")

    assert result["chunks"] == []
