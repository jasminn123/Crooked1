FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/CrookedAPI/CrookedAPI.csproj CrookedAPI/
RUN dotnet restore CrookedAPI/CrookedAPI.csproj

COPY src/CrookedAPI/ CrookedAPI/
RUN dotnet publish CrookedAPI/CrookedAPI.csproj -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "CrookedAPI.dll"]
