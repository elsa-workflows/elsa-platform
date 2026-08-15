FROM node:22-alpine AS console-build
WORKDIR /src/src/Hosting/ValenceControl.Console

COPY src/Hosting/ValenceControl.Console/package*.json ./
RUN npm ci

COPY src/Hosting/ValenceControl.Console/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN mkdir -p src/Hosting/ValenceControl.Api/wwwroot/admin
COPY --from=console-build /src/src/Hosting/ValenceControl.Console/dist/ src/Hosting/ValenceControl.Api/wwwroot/admin/
RUN dotnet restore src/Hosting/ValenceControl.Api/ValenceControl.Api.csproj
# The console is built by the node stage above and copied into wwwroot/admin, and this SDK image has
# no npm, so the project's own console build is skipped here.
RUN dotnet publish src/Hosting/ValenceControl.Api/ValenceControl.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    -p:SkipAdminConsoleBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
LABEL org.opencontainers.image.title="Valence Control" \
      org.opencontainers.image.source="https://github.com/valence-works/valence-control" \
      org.opencontainers.image.licenses="LicenseRef-Valence-Control-Commercial"
COPY --from=build /app/publish .
COPY LICENSE NOTICE.md ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ValenceControl.Api.dll"]
