FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GallerySiteBackend/GallerySiteBackend.csproj", "GallerySiteBackend/"]
COPY ["Contracts/Contracts.csproj", "Contracts/"]
COPY ["Entities/Entities.csproj", "Entities/"]
COPY ["GallerySiteBackend.Presentation/GallerySiteBackend.Presentation.csproj", "GallerySiteBackend.Presentation/"]
COPY ["Service.Contracts/Service.Contracts.csproj", "Service.Contracts/"]
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["Service/Service.csproj", "Service/"]
COPY ["LoggerService/LoggerService.csproj", "LoggerService/"]
COPY ["Repository/Repository.csproj", "Repository/"]
RUN dotnet restore "GallerySiteBackend/GallerySiteBackend.csproj"
COPY . .
WORKDIR "/src/GallerySiteBackend"
RUN dotnet build "GallerySiteBackend.csproj" -c $BUILD_CONFIGURATION -o /app/build
RUN dotnet tool restore
RUN dotnet ef migrations bundle

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "GallerySiteBackend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GallerySiteBackend.dll"]
