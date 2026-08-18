FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app/ ./app/

# Mount point for the file log handler (ADR 0020) — created here too so the container still
# starts cleanly if run without docker-compose's bind mount over this path.
RUN mkdir -p logs

EXPOSE 8000

# --log-config hands uvicorn a dictConfig that owns ALL logging setup (uvicorn's own loggers
# included) — safer than configuring logging at app-import time, which would race with
# uvicorn's own default logging setup and risk being silently overridden. See
# app/logging_config.py/.json for the actual config (console text + rotating JSON file).
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000", "--log-config", "app/logging_config.json"]
