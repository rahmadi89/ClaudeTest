# syntax=docker/dockerfile:1.7
# Single deployable: ASP.NET Core API + the React console served as static files.

FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Shared/AtmMonitor.Contracts/AtmMonitor.Contracts.csproj src/Shared/AtmMonitor.Contracts/
COPY src/Server/AtmMonitor.Domain/AtmMonitor.Domain.csproj src/Server/AtmMonitor.Domain/
COPY src/Server/AtmMonitor.Infrastructure/AtmMonitor.Infrastructure.csproj src/Server/AtmMonitor.Infrastructure/
COPY src/Server/AtmMonitor.Api/AtmMonitor.Api.csproj src/Server/AtmMonitor.Api/
RUN dotnet restore src/Server/AtmMonitor.Api/AtmMonitor.Api.csproj
COPY src/ src/
RUN dotnet publish src/Server/AtmMonitor.Api/AtmMonitor.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1
COPY --from=build /app ./
COPY --from=web /web/dist ./wwwroot
# Non-root user provided by the Microsoft base image.
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "AtmMonitor.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "AtmMonitor.Api.dll"]
