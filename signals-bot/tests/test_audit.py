from pathlib import Path
from signals_bot.shared.audit import append_jsonl


def test_append_jsonl(tmp_path):
    p = tmp_path / "out.jsonl"
    rows = [{"a": 1}, {"b": 2}]
    append_jsonl(str(p), rows)
    assert p.exists()
    lines = p.read_text().splitlines()
    assert len(lines) == 2
