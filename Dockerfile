FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5216

ENV ASPNETCORE_URLS=http://+:5216

USER app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG configuration=Release
WORKDIR /src
COPY ["FirstWebAppInDocker/FirstWebAppInDocker.csproj", "FirstWebAppInDocker/"]
RUN dotnet restore "FirstWebAppInDocker/FirstWebAppInDocker.csproj"
COPY . .
WORKDIR "/src/FirstWebAppInDocker"
RUN dotnet build "FirstWebAppInDocker.csproj" -c $configuration -o /app/build

FROM build AS publish
ARG configuration=Release
RUN dotnet publish "FirstWebAppInDocker.csproj" -c $configuration -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FirstWebAppInDocker.dll"]