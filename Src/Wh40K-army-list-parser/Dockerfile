# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files for layer-cached restore
COPY Wh40kArmyEnricher.sln ./
COPY src/Wh40kArmyEnricher.Contracts/Wh40kArmyEnricher.Contracts.csproj src/Wh40kArmyEnricher.Contracts/
COPY src/Wh40kArmyEnricher.Core/Wh40kArmyEnricher.Core.csproj src/Wh40kArmyEnricher.Core/
COPY src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj src/Wh40kArmyEnricher.Web/
COPY src/Wh40kArmyEnricher.Cli/Wh40kArmyEnricher.Cli.csproj src/Wh40kArmyEnricher.Cli/
COPY tests/Wh40kArmyEnricher.Tests/Wh40kArmyEnricher.Tests.csproj tests/Wh40kArmyEnricher.Tests/

RUN dotnet restore Wh40kArmyEnricher.sln

# Copy everything and publish
COPY . .
RUN dotnet publish src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj \
    -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# BSData cache directory (mount a volume here for persistence)
RUN mkdir -p /root/.wh40k-enricher/cache

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Wh40kArmyEnricher.Web.dll"]
