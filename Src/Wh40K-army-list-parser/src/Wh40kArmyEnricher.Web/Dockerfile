FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj", "src/Wh40kArmyEnricher.Web/"]
COPY ["src/Wh40kArmyEnricher.Core/Wh40kArmyEnricher.Core.csproj", "src/Wh40kArmyEnricher.Core/"]
RUN dotnet restore "src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj"
COPY . .
RUN dotnet publish "src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Wh40kArmyEnricher.Web.dll"]
