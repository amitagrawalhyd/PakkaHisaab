# ============================================================================
# PakkaHisaab API — self-host container (₹0 on your own machine / free VM)
# Runs on SQLite by default: no external database needed at all.
#
#   docker build -f deploy/Dockerfile -t pakkahisaab-api .
#   docker run -d -p 8080:8080 -v ph-data:/data \
#     -e Jwt__Key="$(openssl rand -base64 48)" pakkahisaab-api
#
# Then point Constants.ApiBaseUrl at http://<your-lan-ip>:8080
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/PakkaHisaab.Shared/ PakkaHisaab.Shared/
COPY src/PakkaHisaab.Api/ PakkaHisaab.Api/
RUN dotnet publish PakkaHisaab.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
VOLUME /data
ENV ASPNETCORE_URLS=http://+:8080 \
    Database__Provider=Sqlite \
    Database__AutoCreate=true \
    ConnectionStrings__Default="Data Source=/data/pakkahisaab.db" \
    Jwt__Issuer=http://localhost:8080 \
    Jwt__Audience=pakkahisaab-mobile
EXPOSE 8080
# Probe /health from your orchestrator (the base image ships no curl/wget).
ENTRYPOINT ["dotnet", "PakkaHisaab.Api.dll"]
