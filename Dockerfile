FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ImageStorage__RootPath=/app/Data/images \
    Embedding__ModelPath=/app/Data/model/model.onnx \
    Observability__LogPath=/app/logs

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GallerySiteBackend/GallerySiteBackend.csproj", "GallerySiteBackend/"]
COPY ["GallerySiteBackend.Presentation/GallerySiteBackend.Presentation.csproj", "GallerySiteBackend.Presentation/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Contracts/Contracts.csproj", "Contracts/"]
COPY ["Entities/Entities.csproj", "Entities/"]
COPY ["Repository/Repository.csproj", "Repository/"]
COPY ["Service.Contracts/Service.Contracts.csproj", "Service.Contracts/"]
COPY ["Service/Service.csproj", "Service/"]
COPY ["Shared/Shared.csproj", "Shared/"]
RUN dotnet restore "GallerySiteBackend/GallerySiteBackend.csproj"
COPY . .
WORKDIR "/src/GallerySiteBackend"
RUN dotnet publish "GallerySiteBackend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
RUN mkdir -p /app/Data/images /app/Data/model /app/logs && chown -R $APP_UID:$APP_UID /app/Data /app/logs
VOLUME ["/app/Data", "/app/logs"]
USER $APP_UID
ENTRYPOINT ["dotnet", "GallerySiteBackend.dll"]
