#!/bin/sh
set -eu

case "${1:-api}" in
    api)
        if [ "$#" -gt 0 ]; then
            shift
        fi
        if [ "${ASPNETCORE_URLS:-}" = "" ]; then
            set -- --urls http://0.0.0.0:5000 "$@"
        fi
        exec dotnet /app/api/BlowingCandles.Api.dll "$@"
        ;;
    cli)
        shift
        exec dotnet /app/BlowingCandles.Cli.dll "$@"
        ;;
    check-calendar|stats-periods|run-realtime|run-asof|run-range|--help|-h|help)
        exec dotnet /app/BlowingCandles.Cli.dll "$@"
        ;;
    *)
        exec "$@"
        ;;
esac
