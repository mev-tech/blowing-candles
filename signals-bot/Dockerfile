FROM python:3.13-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates wget \
 && rm -rf /var/lib/apt/lists/*

COPY pyproject.toml /app/pyproject.toml
COPY src /app/src
COPY config.yaml /app/config.yaml

RUN pip install --upgrade pip \
 && pip install -e . \
 && pip install pyyaml

CMD ["python", "-m", "signals_bot.cli.run_realtime"]
