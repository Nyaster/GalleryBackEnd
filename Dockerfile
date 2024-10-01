FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# Устанавливаем пользователя, от имени которого будет запущено приложение
USER $APP_UID

# Рабочая директория
WORKDIR /app

# Даём права на рабочую директорию для текущего пользователя
RUN chown -R $APP_UID /app

EXPOSE 8080
EXPOSE 8081

# Этап сборки
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Копируем csproj файлы
COPY ["GallerySiteBackend/GallerySiteBackend.csproj", "GallerySiteBackend/"]
COPY ["Contracts/Contracts.csproj", "Contracts/"]
COPY ["Entities/Entities.csproj", "Entities/"]
COPY ["GallerySiteBackend.Presentation/GallerySiteBackend.Presentation.csproj", "GallerySiteBackend.Presentation/"]
COPY ["Service.Contracts/Service.Contracts.csproj", "Service.Contracts/"]
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["Service/Service.csproj", "Service/"]
COPY ["LoggerService/LoggerService.csproj", "LoggerService/"]
COPY ["Repository/Repository.csproj", "Repository/"]

# Восстанавливаем зависимости
RUN dotnet restore "GallerySiteBackend/GallerySiteBackend.csproj"

# Копируем все файлы в контейнер
COPY . .

# Устанавливаем рабочую директорию для сборки
WORKDIR "/src/GallerySiteBackend"

# Собираем проект
RUN dotnet build "GallerySiteBackend.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Этап публикации
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "GallerySiteBackend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Финальный образ
FROM base AS final
WORKDIR /app

# Копируем файлы из этапа publish
COPY --from=publish /app/publish .
USER root
# Убедимся, что пользователь имеет права на запись в рабочую директорию
RUN chown -R $APP_UID /app
USER  $APP_UID
# Запускаем приложение
ENTRYPOINT ["dotnet", "GallerySiteBackend.dll"]
