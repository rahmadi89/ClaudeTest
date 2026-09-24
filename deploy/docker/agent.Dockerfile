# syntax=docker/dockerfile:1.7
# Containerised agent running the device SIMULATOR – for demos, load tests and CI only.
# Real terminals run the agent as a Windows Service (see deploy/agent/windows).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Shared/AtmMonitor.Contracts/AtmMonitor.Contracts.csproj src/Shared/AtmMonitor.Contracts/
COPY src/Agent/AtmMonitor.Agent/AtmMonitor.Agent.csproj src/Agent/AtmMonitor.Agent/
RUN dotnet restore src/Agent/AtmMonitor.Agent/AtmMonitor.Agent.csproj
COPY src/ src/
RUN dotnet publish src/Agent/AtmMonitor.Agent/AtmMonitor.Agent.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV DOTNET_ENVIRONMENT=Production \
    Agent__DataDirectory=/data
USER root
RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "AtmMonitor.Agent.dll"]
