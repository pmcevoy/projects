FROM mcr.microsoft.com/dotnet/sdk:8.0-noble AS build
WORKDIR /src

COPY ["src/ProbHammer.Web/ProbHammer.Web.csproj", "src/ProbHammer.Web/"]
COPY ["src/ProbHammer.Core/ProbHammer.Core.csproj", "src/ProbHammer.Core/"]

RUN dotnet restore "src/ProbHammer.Web/ProbHammer.Web.csproj"

COPY --link . .
RUN dotnet publish "src/ProbHammer.Web/ProbHammer.Web.csproj" --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled AS final
WORKDIR /app
EXPOSE 8080

COPY --link --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProbHammer.Web.dll"]
