FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore dependencies
COPY ValorantBot/ValorantBot.csproj ValorantBot/
RUN dotnet restore ValorantBot/ValorantBot.csproj

# Build and publish
COPY ValorantBot/ ValorantBot/
RUN dotnet publish ValorantBot/ValorantBot.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ValorantBot.dll"]
