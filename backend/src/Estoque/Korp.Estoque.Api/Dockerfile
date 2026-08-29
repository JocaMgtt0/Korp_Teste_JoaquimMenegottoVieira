# Contexto de build: pasta ./backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia a solution inteira. Simples e previsivel: o ganho de cache de
# copiar csproj por csproj nao compensa a fragilidade em um projeto deste porte.
COPY . .
RUN dotnet restore src/Estoque/Korp.Estoque.Api/Korp.Estoque.Api.csproj
RUN dotnet publish src/Estoque/Korp.Estoque.Api/Korp.Estoque.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# wget para o healthcheck do compose
RUN apt-get update && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Korp.Estoque.Api.dll"]
