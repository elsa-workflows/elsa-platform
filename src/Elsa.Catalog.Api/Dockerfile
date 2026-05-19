FROM node:22-alpine AS admin-ui-build
WORKDIR /src/src/Elsa.Catalog.AdminUi

COPY src/Elsa.Catalog.AdminUi/package*.json ./
RUN npm ci

COPY src/Elsa.Catalog.AdminUi/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN mkdir -p src/Elsa.Catalog.Api/wwwroot/admin
COPY --from=admin-ui-build /src/src/Elsa.Catalog.AdminUi/dist/ src/Elsa.Catalog.Api/wwwroot/admin/
RUN dotnet restore Elsa.PackageCatalog.sln
RUN dotnet publish src/Elsa.Catalog.Api/Elsa.Catalog.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Elsa.Catalog.Api.dll"]
