# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM node:22-alpine AS client-assets
WORKDIR /src
COPY ["package.json", "package-lock.json", "./"]
RUN npm ci
COPY ["tailwind.config.js", "./"]
COPY ["Assets", "./Assets"]
COPY ["Areas", "./Areas"]
COPY ["Views", "./Views"]
COPY ["wwwroot", "./wwwroot"]
RUN npm run assets:build

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER root
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Portfolio.csproj", "."]
RUN dotnet restore "./Portfolio.csproj"
COPY . .
WORKDIR "/src/."
COPY --from=client-assets /src/wwwroot/css/fonts.css ./wwwroot/css/fonts.css
COPY --from=client-assets /src/wwwroot/css/tailwind.min.css ./wwwroot/css/tailwind.min.css
COPY --from=client-assets /src/wwwroot/fonts ./wwwroot/fonts
COPY --from=client-assets /src/wwwroot/vendor ./wwwroot/vendor
RUN dotnet build "./Portfolio.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Portfolio.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

USER root
RUN mkdir -p /app/dataprotection-keys && \
    mkdir -p /app/wwwroot/uploads && \
    chown -R $APP_UID /app/dataprotection-keys && \
    chown -R $APP_UID /app/wwwroot/uploads
USER $APP_UID

ENTRYPOINT ["dotnet", "Portfolio.dll"]
