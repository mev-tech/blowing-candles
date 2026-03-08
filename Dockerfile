FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src

COPY ["BlowingCandles.sln", "./"]
COPY ["src/BlowingCandles.Domain/BlowingCandles.Domain.csproj", "src/BlowingCandles.Domain/"]
COPY ["src/BlowingCandles.Application/BlowingCandles.Application.csproj", "src/BlowingCandles.Application/"]
COPY ["src/BlowingCandles.Infrastructure/BlowingCandles.Infrastructure.csproj", "src/BlowingCandles.Infrastructure/"]
COPY ["src/BlowingCandles.Cli/BlowingCandles.Cli.csproj", "src/BlowingCandles.Cli/"]
COPY ["tests/BlowingCandles.Domain.Tests/BlowingCandles.Domain.Tests.csproj", "tests/BlowingCandles.Domain.Tests/"]
COPY ["tests/BlowingCandles.Application.Tests/BlowingCandles.Application.Tests.csproj", "tests/BlowingCandles.Application.Tests/"]
COPY ["tests/BlowingCandles.Infrastructure.Tests/BlowingCandles.Infrastructure.Tests.csproj", "tests/BlowingCandles.Infrastructure.Tests/"]
COPY ["tests/BlowingCandles.CrossValidation.Tests/BlowingCandles.CrossValidation.Tests.csproj", "tests/BlowingCandles.CrossValidation.Tests/"]

RUN dotnet restore BlowingCandles.sln

FROM restore AS test
WORKDIR /src

COPY . .

RUN dotnet test tests/BlowingCandles.Domain.Tests/BlowingCandles.Domain.Tests.csproj -c Release --no-restore
RUN dotnet test tests/BlowingCandles.Application.Tests/BlowingCandles.Application.Tests.csproj -c Release --no-restore
RUN dotnet test tests/BlowingCandles.Infrastructure.Tests/BlowingCandles.Infrastructure.Tests.csproj -c Release --no-restore

FROM test AS publish
WORKDIR /src

RUN dotnet publish src/BlowingCandles.Cli/BlowingCandles.Cli.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
LABEL maintainer="mev" \
      description="BlowingCandles manual trading signal generator CLI"

WORKDIR /app

RUN getent group app >/dev/null || groupadd --system app
RUN id -u app >/dev/null 2>&1 || useradd --system --gid app --home-dir /app --create-home app

COPY ["docker-entrypoint.sh", "/app/docker-entrypoint.sh"]
RUN chmod +x /app/docker-entrypoint.sh

COPY --from=publish --chown=app:app /app/publish/ ./
RUN chown app:app /app/docker-entrypoint.sh

USER app

HEALTHCHECK CMD ["/app/docker-entrypoint.sh", "--help"]

ENTRYPOINT ["/app/docker-entrypoint.sh"]
