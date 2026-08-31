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
COPY LICENSE NOTICE.md ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ELSA_CONTROL_IMAGE_ID=$ELSA_CONTROL_IMAGE_ID
ENTRYPOINT ["dotnet", "ElsaControl.Api.dll"]
