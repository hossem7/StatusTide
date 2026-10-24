# Build from the repository root: docker build -f StatusTide.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY StatusTide.Core/StatusTide.Core.csproj StatusTide.Core/
COPY StatusTide.Infrastructure/StatusTide.Infrastructure.csproj StatusTide.Infrastructure/
COPY StatusTide.Api/StatusTide.Api.csproj StatusTide.Api/
RUN dotnet restore StatusTide.Api/StatusTide.Api.csproj

COPY StatusTide.Core/ StatusTide.Core/
COPY StatusTide.Infrastructure/ StatusTide.Infrastructure/
COPY StatusTide.Api/ StatusTide.Api/
RUN dotnet publish StatusTide.Api/StatusTide.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatusTide.Api.dll"]
