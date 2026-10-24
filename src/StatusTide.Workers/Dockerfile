# Build from the repository root: docker build -f src/StatusTide.Workers/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/StatusTide.Core/StatusTide.Core.csproj src/StatusTide.Core/
COPY src/StatusTide.Infrastructure/StatusTide.Infrastructure.csproj src/StatusTide.Infrastructure/
COPY src/StatusTide.Workers/StatusTide.Workers.csproj src/StatusTide.Workers/
RUN dotnet restore src/StatusTide.Workers/StatusTide.Workers.csproj

COPY src/ src/
RUN dotnet publish src/StatusTide.Workers/StatusTide.Workers.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "StatusTide.Workers.dll"]
