# Multi-arch (works natively on Apple Silicon and Intel Macs).
FROM node:22-alpine AS web
WORKDIR /repo/web/Ospreya.Web
COPY web/Ospreya.Web/package.json web/Ospreya.Web/package-lock.json ./
RUN npm ci
COPY web/Ospreya.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY nuget.config Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Ospreya.Api/Ospreya.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
COPY --from=web /repo/src/Ospreya.Api/wwwroot ./wwwroot
# Key directory for the shared Data Protection key ring. docker-compose.yml bind-mounts the host folder here; Docker
# Desktop on macOS maps file ownership, so the non-root user can read and write it.
RUN mkdir -p /keys && chown $APP_UID /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "Ospreya.Api.dll"]
