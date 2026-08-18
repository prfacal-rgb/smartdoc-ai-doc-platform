"""Logging setup for ai-service (ADR 0020).

Same discipline as the .NET side (Serilog): human-readable text to console for live dev
tailing, structured JSON lines to a rotating file for later analysis — no new dependency,
just stdlib logging with a small custom formatter. Not a strict CLEF implementation like
Serilog's CompactJsonFormatter, but the same visual convention (`@t`/`@l` prefixed metadata,
everything else is data) so a file from either side reads the same way.

Applied via uvicorn's --log-config (see Dockerfile) rather than a module-level
logging.basicConfig()/dictConfig() call at import time — uvicorn configures its own loggers
(uvicorn/uvicorn.access/uvicorn.error) on startup and that would race with (and can silently
override) any handler set up when this module is merely imported. --log-config lets one
dictConfig own everything, uvicorn's loggers included, with no ordering risk.
"""

import json
import logging
from datetime import datetime, timezone

_RESERVED_ATTRS = frozenset(
    {
        "name", "msg", "args", "levelname", "levelno", "pathname", "filename", "module",
        "exc_info", "exc_text", "stack_info", "lineno", "funcName", "created", "msecs",
        "relativeCreated", "thread", "threadName", "processName", "process", "message",
        "taskName",
        # Not a real LogRecord attribute — Formatter.format() sets it as a side effect when a
        # format string uses %(asctime)s, and since the same LogRecord instance is shared
        # across every handler for one log call (console handler runs first here), it leaks
        # into this formatter's view of the record. Redundant with @t anyway.
        "asctime",
    }
)


class JsonFormatter(logging.Formatter):
    """One JSON object per line: `@t` (UTC timestamp), `@l` (level), `logger`, `message`,
    `exception` (if any), plus whatever was passed via `extra={...}` on the log call."""

    def format(self, record: logging.LogRecord) -> str:
        payload: dict[str, object] = {
            "@t": datetime.fromtimestamp(record.created, tz=timezone.utc).isoformat(),
            "@l": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }

        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)

        extras = {k: v for k, v in record.__dict__.items() if k not in _RESERVED_ATTRS}
        payload.update(extras)

        return json.dumps(payload, default=str)
