#!/bin/sh
set -eu

if [ "$#" -eq 0 ]; then
    set -- --help
fi

case "$1" in
    check-calendar|stats-periods|run-realtime|run-asof|run-range|--help|-h|help)
        exec dotnet /app/BlowingCandles.Cli.dll "$@"
        ;;
    *)
        exec "$@"
        ;;
esac
