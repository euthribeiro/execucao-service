FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore em camada separada para aproveitar o cache do Docker.
COPY ["src/wrench.execucao.domain/wrench.execucao.domain.csproj", "src/wrench.execucao.domain/"]
COPY ["src/wrench.execucao.application/wrench.execucao.application.csproj", "src/wrench.execucao.application/"]
COPY ["src/wrench.execucao.infra/wrench.execucao.infra.csproj", "src/wrench.execucao.infra/"]
COPY ["src/wrench.execucao.api/wrench.execucao.api.csproj", "src/wrench.execucao.api/"]
RUN dotnet restore "src/wrench.execucao.api/wrench.execucao.api.csproj"

COPY src/ src/
RUN dotnet publish "src/wrench.execucao.api/wrench.execucao.api.csproj" \
    -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "wrench.execucao.api.dll"]
