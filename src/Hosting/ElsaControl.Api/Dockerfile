FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.300 AS verification-tools
ARG TARGETARCH
# Both supported artifacts and the trust root are content-pinned. Updating the verifier
# requires an explicit dependency review; the runtime never downloads executable code.
ADD --checksum=sha256:4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71 \
    https://github.com/sigstore/cosign/releases/download/v3.1.3/cosign-linux-amd64 /tmp/cosign-amd64
ADD --checksum=sha256:c5d324e091826b0d7a78eb16fef316450b4eb9aaec045611c08ba06f5e73220a \
    https://github.com/sigstore/cosign/releases/download/v3.1.3/cosign-linux-arm64 /tmp/cosign-arm64
ADD --checksum=sha256:6494e21ea73fa7ee769f85f57d5a3e6a08725eae1e38c755fc3517c9e6bc0b66 \
    https://raw.githubusercontent.com/sigstore/root-signing/c9bda74ad2221f938f7d2e0295ca3aad2da710a8/targets/trusted_root.json /opt/elsa-control/verification/trusted-root.json
RUN case "$TARGETARCH" in amd64|arm64) ;; *) exit 1 ;; esac \
    && install -m 0755 "/tmp/cosign-$TARGETARCH" /opt/elsa-control/verification/cosign \
    && chmod 0644 /opt/elsa-control/verification/trusted-root.json

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

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG ELSA_CONTROL_IMAGE_ID=unknown
WORKDIR /app
LABEL org.opencontainers.image.title="Elsa Control" \
      org.opencontainers.image.source="https://github.com/valence-works/elsa-control" \
      org.opencontainers.image.licenses="LicenseRef-Elsa-Control-Commercial"
COPY --from=build /app/publish .
COPY --from=verification-tools /opt/elsa-control/verification /opt/elsa-control/verification
COPY LICENSE NOTICE.md ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ELSA_CONTROL_IMAGE_ID=$ELSA_CONTROL_IMAGE_ID
ENTRYPOINT ["dotnet", "ElsaControl.Api.dll"]
