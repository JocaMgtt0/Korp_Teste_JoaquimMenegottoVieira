# Contexto de build: pasta ./backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/Faturamento/Korp.Faturamento.Api/Korp.Faturamento.Api.csproj
RUN dotnet publish src/Faturamento/Korp.Faturamento.Api/Korp.Faturamento.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# wget para o healthcheck do compose.
# libfontconfig e libfreetype sao exigidos pelo QuestPDF para renderizar fontes.
RUN apt-get update && apt-get install -y --no-install-recommends \
        wget libfontconfig1 libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Korp.Faturamento.Api.dll"]
