FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
 && rm -rf /var/lib/apt/lists/*

COPY pyproject.toml /app/pyproject.toml
COPY src /app/src
COPY config.yaml /app/config.yaml
COPY earnings_calendar.json /app/earnings_calendar.json
COPY requirements.txt /app/requirements.txt
COPY tests /app/tests

RUN pip install --upgrade pip \
 && pip install -e . \
 && pip install -r requirements.txt

CMD ["python", "-m", "signals_bot.cli.run_realtime"]
