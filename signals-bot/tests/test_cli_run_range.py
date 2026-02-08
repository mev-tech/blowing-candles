import sys
import subprocess
from datetime import date

import pytest

from signals_bot.cli.run_range import main


def test_run_range_calls_subprocess(monkeypatch, tmp_path):
    calls = []

    def fake_run(cmd, check=True):
        calls.append(cmd)
        class R: pass
        return R()

    monkeypatch.setattr(subprocess, "run", fake_run)

    sys.argv = ["run_range", "--start", "2025-01-01", "--end", "2025-01-03", "--prefix", "pr"]
    main()

    # should have 3 calls (2025-01-01, 02, 03)
    assert len(calls) == 3
    assert all(isinstance(c, list) for c in calls)
    # verify command contains the -m run_asof module
    assert any("signals_bot.cli.run_asof" in cmd for cmd in calls[0])
