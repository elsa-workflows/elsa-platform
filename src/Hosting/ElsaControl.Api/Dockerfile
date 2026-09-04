FROM node:22-alpine AS console-build
WORKDIR /src/src/Hosting/ElsaControl.Console

COPY src/Hosting/ElsaControl.Console/package*.json ./
RUN npm ci

COPY src/Hosting/ElsaControl.Console/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN mkdir -p src/Hosting/ElsaControl.Api/wwwroot/admin
COPY --from=console-build /src/src/Hosting/ElsaControl.Console/dist/ src/Hosting/ElsaControl.Api/wwwroot/admin/
RUN dotnet restore src/Hosting/ElsaControl.Api/ElsaControl.Api.csproj
# The console is built by the node stage above and copied into wwwroot/admin, and this SDK image has
# no npm, so the project's own console build is skipped here.
RUN dotnet publish src/Hosting/ElsaControl.Api/ElsaControl.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    -p:SkipAdminConsoleBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS runtime

# The managed-instance Azure worker is intentionally in-process for the initial
# stamp. Keep every executable in the provider authority immutable: Azure CLI
# and the .NET runtime are digest-pinned multi-platform images, while sqlcmd is
# checksum-pinned per target architecture.
FROM mcr.microsoft.com/azure-cli:2.77.0@sha256:d41153958a61392a4c9db1591e33b533ca8ab8b59529d212d06bb6ec7e9c1f25 AS final
ARG TARGETARCH
ARG SQLCMD_VERSION=1.10.0
ARG SQLCMD_AMD64_SHA256=92516d98c63d99b0994de5b61350c91f6915f9b76f139a59039fbcb225c2e987
ARG SQLCMD_ARM64_SHA256=9faaa981f9c374f319ac796dedb4678499b8596c87d5b6c512e9b0e7a3b74f8e
# Copying the shared .NET runtime does not copy its OS dependencies. Keep full
# globalization/time-zone support; invariant mode is not compatible with SQL.
RUN tdnf install --assumeyes tar bzip2 icu tzdata libstdc++ openssl-libs \
    && tdnf clean all \
    && case "$TARGETARCH" in \
      amd64) sqlcmd_sha256="$SQLCMD_AMD64_SHA256" ;; \
      arm64) sqlcmd_sha256="$SQLCMD_ARM64_SHA256" ;; \
      *) echo "Unsupported target architecture" >&2; exit 1 ;; \
    esac \
    && curl --fail --silent --show-error --location \
      "https://github.com/microsoft/go-sqlcmd/releases/download/v${SQLCMD_VERSION}/sqlcmd-linux-${TARGETARCH}.tar.bz2" \
      --output /tmp/sqlcmd.tar.bz2 \
    && echo "${sqlcmd_sha256}  /tmp/sqlcmd.tar.bz2" | sha256sum --check --status \
    && tar --extract --bzip2 --file /tmp/sqlcmd.tar.bz2 --directory /usr/local/bin sqlcmd \
    && chmod 0755 /usr/local/bin/sqlcmd \
    && command -v az >/dev/null \
    && command -v curl >/dev/null \
    && /usr/local/bin/sqlcmd --version >/dev/null \
    && rm /tmp/sqlcmd.tar.bz2
COPY --from=runtime /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && dotnet --list-runtimes | grep --fixed-strings 'Microsoft.AspNetCore.App 10.' >/dev/null
ARG ELSA_CONTROL_IMAGE_ID=unknown
WORKDIR /app
LABEL org.opencontainers.image.title="Elsa Control" \
      org.opencontainers.image.source="https://github.com/valence-works/elsa-control" \
      org.opencontainers.image.licenses="LicenseRef-Elsa-Control-Commercial"
COPY --from=build /app/publish .
COPY LICENSE NOTICE.md ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ELSA_CONTROL_IMAGE_ID=$ELSA_CONTROL_IMAGE_ID \
    Deployment__AzureProvider__Runner__AzureCliPath=/usr/bin/az \
    Deployment__AzureProvider__Runner__SqlCmdPath=/usr/local/bin/sqlcmd \
    Deployment__AzureProvider__Runner__CurlPath=/usr/bin/curl \
    Deployment__AzureProvider__Runner__TemplateRoot=/app/azure-provider
COPY infra/azure-production/ /app/azure-provider/
ENTRYPOINT ["dotnet", "ElsaControl.Api.dll"]
