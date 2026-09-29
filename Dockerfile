# Bruker ASP.NET Core 10 som grunnlag for å kjøre nettsiden
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Setter /app som arbeidsmappe inne i containeren
WORKDIR /app

# Forteller at applikasjonen bruker port 5216
EXPOSE 5216

# Forteller ASP.NET Core at nettsiden skal lytte på port 5216
ENV ASPNETCORE_URLS=http://+:5216

# Kjører applikasjonen som brukeren "app" i stedet for root
USER app


# Bruker .NET SDK 10 for å bygge prosjektet
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Setter byggemodus til Release
ARG configuration=Release

# Setter /src som arbeidsmappe
WORKDIR /src

# Kopierer prosjektfilen (.csproj) inn i containeren
COPY ["FirstWebAppInDocker/FirstWebAppInDocker.csproj", "FirstWebAppInDocker/"]

# Laster ned pakkene som prosjektet trenger
RUN dotnet restore "FirstWebAppInDocker/FirstWebAppInDocker.csproj"

# Kopierer resten av prosjektet inn i containeren
COPY . .

# Går inn i mappen til prosjektet
WORKDIR "/src/FirstWebAppInDocker"

# Bygger prosjektet
RUN dotnet build "FirstWebAppInDocker.csproj" -c $configuration -o /app/build


# Lager en ferdig versjon av applikasjonen som kan kjøres
FROM build AS publish

# Bruker Release som byggemodus
ARG configuration=Release

# Publiserer den ferdige applikasjonen til /app/publish
RUN dotnet publish "FirstWebAppInDocker.csproj" -c $configuration -o /app/publish /p:UseAppHost=false


# Lager den endelige Docker-containeren
FROM base AS final

# Setter /app som arbeidsmappe
WORKDIR /app

# Kopierer den ferdige applikasjonen fra publish-steget
COPY --from=publish /app/publish .

# Kommandoen som kjøres når containeren starter
ENTRYPOINT ["dotnet", "FirstWebAppInDocker.dll"]