# syntax=docker/dockerfile:1
# Multi-stage build for the ParkingSaaS API.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the project files first for better layer caching.
COPY ParkingSaaS.slnx ./
COPY src/ParkingSaaS.Domain/*.csproj           src/ParkingSaaS.Domain/
COPY src/ParkingSaaS.Application/*.csproj       src/ParkingSaaS.Application/
COPY src/ParkingSaaS.Infrastructure/*.csproj    src/ParkingSaaS.Infrastructure/
COPY src/ParkingSaaS.Contracts/*.csproj         src/ParkingSaaS.Contracts/
COPY src/ParkingSaaS.Api/*.csproj               src/ParkingSaaS.Api/
RUN dotnet restore src/ParkingSaaS.Api/ParkingSaaS.Api.csproj

COPY . .
RUN dotnet publish src/ParkingSaaS.Api/ParkingSaaS.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as a non-root user.
RUN adduser --disabled-password --gecos "" appuser
COPY --chown=appuser:appuser --from=build /app .
RUN mkdir -p /app/dp-keys && chown -R appuser:appuser /app

USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ParkingSaaS.Api.dll"]
