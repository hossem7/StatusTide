# Build from the repository root: docker build -f src/StatusTide.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/StatusTide.Core/StatusTide.Core.csproj src/StatusTide.Core/
COPY src/StatusTide.Infrastructure/StatusTide.Infrastructure.csproj src/StatusTide.Infrastructure/
COPY src/StatusTide.Api/StatusTide.Api.csproj src/StatusTide.Api/
RUN dotnet restore src/StatusTide.Api/StatusTide.Api.csproj

COPY src/ src/
RUN dotnet publish src/StatusTide.Api/StatusTide.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatusTide.Api.dll"]
