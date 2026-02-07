from pathlib import Path
import json
from datetime import datetime, timezone
import yaml

def main():
    cfg = yaml.safe_load(Path("config.yaml").read_text())
    watchlist = cfg["watchlist"]

    now = datetime.now(timezone.utc).isoformat()

    lines = [f"{t}  WAIT  (BOOTSTRAP)" for t in watchlist]
    Path("signals.txt").write_text("\n".join(lines) + "\n")
    Path("signals.json").write_text(json.dumps({"timestamp": now, "signals": lines}, indent=2) + "\n")

    print("signals.txt and signals.json written")

if __name__ == "__main__":
    main()
