#!/bin/sh
set -eu

if [ "$#" -eq 0 ]; then
    set -- --help
fi

case "$1" in
    check-calendar|stats-periods|run-realtime|run-asof|run-range|--help|-h|help)
        exec dotnet /app/BlowingCandles.Cli.dll "$@"
        ;;
    api)
        shift
        if [ "${ASPNETCORE_URLS:-}" = "" ]; then
            set -- --urls http://0.0.0.0:5000 "$@"
        fi
        exec dotnet /app/api/BlowingCandles.Api.dll "$@"
        ;;
    *)
        exec "$@"
        ;;
esac
