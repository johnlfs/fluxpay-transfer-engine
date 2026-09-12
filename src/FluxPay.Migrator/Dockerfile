# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY global.json ./

COPY src/FluxPay.Domain/FluxPay.Domain.csproj \
     src/FluxPay.Domain/

COPY src/FluxPay.Application/FluxPay.Application.csproj \
     src/FluxPay.Application/

COPY src/FluxPay.Infrastructure/FluxPay.Infrastructure.csproj \
     src/FluxPay.Infrastructure/

COPY src/FluxPay.Migrator/FluxPay.Migrator.csproj \
     src/FluxPay.Migrator/

RUN dotnet restore \
    src/FluxPay.Migrator/FluxPay.Migrator.csproj

COPY src/ ./src/

RUN dotnet publish \
    src/FluxPay.Migrator/FluxPay.Migrator.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

COPY --from=build \
     --chown=app:app \
     /app/publish/ \
     ./

USER app

ENTRYPOINT ["dotnet", "FluxPay.Migrator.dll"]
