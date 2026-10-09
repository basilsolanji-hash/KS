# Образ knitERP. Сборка из папки kniterp: docker build -f deploy/beget/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src
COPY . .
RUN dotnet publish src/KnitErp.Web -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Приложение слушает HTTP внутри сети Docker; HTTPS снаружи даёт Caddy на сервере.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "KnitErp.Web.dll"]
