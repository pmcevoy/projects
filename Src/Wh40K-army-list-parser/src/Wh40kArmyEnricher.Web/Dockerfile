FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build
WORKDIR /src

COPY ["src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj", "src/Wh40kArmyEnricher.Web/"]
COPY ["src/Wh40kArmyEnricher.Core/Wh40kArmyEnricher.Core.csproj", "src/Wh40kArmyEnricher.Core/"]

RUN dotnet restore "src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj"

COPY --link . .
RUN dotnet publish "src/Wh40kArmyEnricher.Web/Wh40kArmyEnricher.Web.csproj" --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled AS final
WORKDIR /app
EXPOSE 8080

COPY --link --from=build /app/publish .
ENTRYPOINT ["dotnet", "Wh40kArmyEnricher.Web.dll"]
