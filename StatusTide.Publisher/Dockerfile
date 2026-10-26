# Build from the repository root: docker build -f StatusTide.Publisher/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY StatusTide.Contracts/StatusTide.Contracts.csproj StatusTide.Contracts/
COPY StatusTide.Common/StatusTide.Common.csproj StatusTide.Common/
COPY StatusTide.Data/StatusTide.Data.csproj StatusTide.Data/
COPY StatusTide.Publisher/StatusTide.Publisher.csproj StatusTide.Publisher/
RUN dotnet restore StatusTide.Publisher/StatusTide.Publisher.csproj

COPY StatusTide.Contracts/ StatusTide.Contracts/
COPY StatusTide.Common/ StatusTide.Common/
COPY StatusTide.Data/ StatusTide.Data/
COPY StatusTide.Publisher/ StatusTide.Publisher/
RUN dotnet publish StatusTide.Publisher/StatusTide.Publisher.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "StatusTide.Publisher.dll"]
