# syntax=docker/dockerfile:1
# Multi-stage: the SDK image builds, the small ASP.NET runtime image runs (no SDK, source or secrets).

# Build natively on the build machine's CPU, even when targeting another architecture.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# amd64 or arm64; publishing for one architecture drops ~66 MB of other platforms' native libraries.
ARG TARGETARCH
WORKDIR /src

# Restore first so the package layer stays cached until a .csproj changes.
COPY global.json Directory.Build.props .editorconfig SiteLedger.slnx ./
COPY src/SiteLedger.Domain/SiteLedger.Domain.csproj                 src/SiteLedger.Domain/
COPY src/SiteLedger.Application/SiteLedger.Application.csproj       src/SiteLedger.Application/
COPY src/SiteLedger.Infrastructure/SiteLedger.Infrastructure.csproj src/SiteLedger.Infrastructure/
COPY src/SiteLedger.Api/SiteLedger.Api.csproj                       src/SiteLedger.Api/
RUN dotnet restore src/SiteLedger.Api/SiteLedger.Api.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/SiteLedger.Api/SiteLedger.Api.csproj -a $TARGETARCH --self-contained false \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Production defaults; set ConnectionStrings__Default and Jwt__SigningKey as environment variables.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY --from=build /app/publish .

# Never run as root: the base image's unprivileged "app" user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SiteLedger.Api.dll"]
