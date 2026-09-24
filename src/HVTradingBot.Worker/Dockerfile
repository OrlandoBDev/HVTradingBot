FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY nuget.config Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/HVTradingBot.Worker/HVTradingBot.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app ./
# Key directory for the shared Data Protection key ring (mounted as a volume; ownership is copied on first use).
RUN mkdir -p /keys && chown $APP_UID /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "HVTradingBot.Worker.dll"]
