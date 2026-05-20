FROM node:22-alpine AS admin-ui-build
WORKDIR /src/src/Elsa.Platform.AdminUi

COPY src/Elsa.Platform.AdminUi/package*.json ./
RUN npm ci

COPY src/Elsa.Platform.AdminUi/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN mkdir -p src/Elsa.Platform.PackageCatalog.Api/wwwroot/admin
COPY --from=admin-ui-build /src/src/Elsa.Platform.AdminUi/dist/ src/Elsa.Platform.PackageCatalog.Api/wwwroot/admin/
RUN dotnet restore Elsa.Platform.sln
RUN dotnet publish src/Elsa.Platform.PackageCatalog.Api/Elsa.Platform.PackageCatalog.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Elsa.Platform.PackageCatalog.Api.dll"]
