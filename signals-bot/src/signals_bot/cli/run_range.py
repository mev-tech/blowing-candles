from datetime import date, timedelta, datetime, timezone
import subprocess
import argparse

def daterange(start: date, end: date):
    d = start
    while d <= end:
        yield d
        d += timedelta(days=1)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--start", required=True)
    ap.add_argument("--end", required=True)
    ap.add_argument("--prefix", default="range")
    args = ap.parse_args()

    start = date.fromisoformat(args.start)
    end = date.fromisoformat(args.end)

    for d in daterange(start, end):
        ds = d.isoformat()
        print(f"\n=== {ds} ===")
        subprocess.run([
            "python", "-m", "signals_bot.cli.run_asof",
            "--asof", ds,
            "--out-prefix", args.prefix
        ], check=True)

if __name__ == "__main__":
    main()
