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

COPY src/FluxPay.Worker/FluxPay.Worker.csproj \
     src/FluxPay.Worker/

RUN dotnet restore \
    src/FluxPay.Worker/FluxPay.Worker.csproj

COPY src/ ./src/

RUN dotnet publish \
    src/FluxPay.Worker/FluxPay.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true

EXPOSE 8080

COPY --from=build \
     --chown=app:app \
     /app/publish/ \
     ./

USER app

ENTRYPOINT ["dotnet", "FluxPay.Worker.dll"]
