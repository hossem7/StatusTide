# Build from the repository root: docker build -f StatusTide.Workers/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY StatusTide.Core/StatusTide.Core.csproj StatusTide.Core/
COPY StatusTide.Infrastructure/StatusTide.Infrastructure.csproj StatusTide.Infrastructure/
COPY StatusTide.Workers/StatusTide.Workers.csproj StatusTide.Workers/
RUN dotnet restore StatusTide.Workers/StatusTide.Workers.csproj

COPY StatusTide.Core/ StatusTide.Core/
COPY StatusTide.Infrastructure/ StatusTide.Infrastructure/
COPY StatusTide.Workers/ StatusTide.Workers/
RUN dotnet publish StatusTide.Workers/StatusTide.Workers.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "StatusTide.Workers.dll"]
