import tiktoken

from app.chunking.models import Chunk
from app.parsing.models import ParsedPage

# cl100k_base as a provider-agnostic token-count approximation — see app/config.py.
_encoding = tiktoken.get_encoding("cl100k_base")


def chunk_pages(pages: list[ParsedPage], chunk_size_tokens: int, overlap_tokens: int) -> list[Chunk]:
    """Chunks each page's text independently (not across page boundaries), so every chunk
    maps to exactly one page_number — required for the "file — page N" citation format
    PROJECT.md specifies. Trade-off: a chunk near a page boundary won't include context from
    the adjacent page."""
    if overlap_tokens >= chunk_size_tokens:
        raise ValueError("overlap_tokens must be smaller than chunk_size_tokens.")

    chunks: list[Chunk] = []
    chunk_index = 0
    step = chunk_size_tokens - overlap_tokens

    for page in pages:
        # A page with only whitespace/control characters (e.g. an image-only or cover page
        # pypdf still extracts a stray space or two from) encodes to non-empty tokens, so
        # `not tokens` alone doesn't catch it - it must be filtered explicitly, otherwise it
        # reaches DocumentChunk's non-empty-text invariant on the .NET side and fails the
        # whole document's processing over what amounts to a blank page.
        if not page.text.strip():
            continue

        tokens = _encoding.encode(page.text)
        if not tokens:
            continue

        start = 0
        while start < len(tokens):
            window = tokens[start : start + chunk_size_tokens]
            text = _encoding.decode(window)
            # start += step must run regardless, or a blank window (rare - only possible at
            # a page's trailing edge) would spin the loop forever without advancing.
            if text.strip():
                chunks.append(Chunk(chunk_index=chunk_index, page_number=page.page_number, text=text))
                chunk_index += 1
            start += step

    return chunks
