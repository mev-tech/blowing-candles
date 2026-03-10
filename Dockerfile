FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src

COPY ["BlowingCandles.sln", "./"]
COPY ["src/BlowingCandles.Domain/BlowingCandles.Domain.csproj", "src/BlowingCandles.Domain/"]
COPY ["src/BlowingCandles.Application/BlowingCandles.Application.csproj", "src/BlowingCandles.Application/"]
COPY ["src/BlowingCandles.Infrastructure/BlowingCandles.Infrastructure.csproj", "src/BlowingCandles.Infrastructure/"]
COPY ["src/BlowingCandles.Api/BlowingCandles.Api.csproj", "src/BlowingCandles.Api/"]
COPY ["src/BlowingCandles.Cli/BlowingCandles.Cli.csproj", "src/BlowingCandles.Cli/"]
COPY ["tests/BlowingCandles.Api.Tests/BlowingCandles.Api.Tests.csproj", "tests/BlowingCandles.Api.Tests/"]
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
RUN dotnet test tests/BlowingCandles.CrossValidation.Tests/BlowingCandles.CrossValidation.Tests.csproj -c Release --no-restore

FROM test AS publish
WORKDIR /src

RUN dotnet publish src/BlowingCandles.Cli/BlowingCandles.Cli.csproj -c Release -o /app/publish/cli --no-restore
RUN dotnet publish src/BlowingCandles.Api/BlowingCandles.Api.csproj -c Release -o /app/publish/api --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
LABEL maintainer="mev" \
      description="BlowingCandles signal generation API service"

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
RUN getent group app >/dev/null || groupadd --system app
RUN id -u app >/dev/null 2>&1 || useradd --system --gid app --home-dir /app --create-home app

COPY ["docker-entrypoint.sh", "/app/docker-entrypoint.sh"]
RUN chmod +x /app/docker-entrypoint.sh

COPY --from=publish --chown=app:app /app/publish/cli/ ./
COPY --from=publish --chown=app:app /app/publish/api/ /app/api/
RUN chown app:app /app/docker-entrypoint.sh

USER app

EXPOSE 5000

HEALTHCHECK --start-period=10s CMD ["curl", "-fsS", "http://127.0.0.1:5000/health/live"]

ENTRYPOINT ["/app/docker-entrypoint.sh"]
