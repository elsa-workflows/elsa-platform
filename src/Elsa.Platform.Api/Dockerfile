FROM node:22-alpine AS console-build
WORKDIR /src/src/Elsa.Platform.Console

COPY src/Elsa.Platform.Console/package*.json ./
RUN npm ci

COPY src/Elsa.Platform.Console/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS build
WORKDIR /src

COPY . .
RUN mkdir -p src/Elsa.Platform.Api/wwwroot/admin
COPY --from=console-build /src/src/Elsa.Platform.Console/dist/ src/Elsa.Platform.Api/wwwroot/admin/
RUN dotnet restore Elsa.Platform.sln
RUN dotnet publish src/Elsa.Platform.Api/Elsa.Platform.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Elsa.Platform.Api.dll"]
