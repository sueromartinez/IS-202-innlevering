
# ========================================
# ASP.NET CORE 10 - RUNTIME
# ========================================

# Bruker ASP.NET Core 10 som grunnlag
# for å kjøre nettsiden.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

# Setter /app som arbeidsmappe.
WORKDIR /app

# Forteller at applikasjonen bruker port 5216.
EXPOSE 5216

# Nettsiden skal lytte på port 5216.
ENV ASPNETCORE_URLS=http://+:5216


# ========================================
# DATA PROTECTION - RETTIGHETER
# ========================================

# Oppretter nøkkelkatalogen i imaget
# før Docker monterer det permanente volumet.
#
# Katalogen eies av den innebygde
# ASP.NET Core-brukeren "app".
#
# Dette gjør at et nytt, tomt Docker-volum
# kan initialiseres med riktige rettigheter.
#
# .keep sørger for at katalogen har innhold
# når Docker initialiserer et nytt volum.

RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /home/app/.aspnet \
    && chmod 700 /home/app/.aspnet/DataProtection-Keys \
    && touch /home/app/.aspnet/DataProtection-Keys/.keep \
    && chown app:app /home/app/.aspnet/DataProtection-Keys/.keep


# ========================================
# IKKE-PRIVILEGERT BRUKER
# ========================================

# Applikasjonen skal ikke kjøre som root.
USER app


# ========================================
# .NET SDK 10 - BUILD
# ========================================

# Bruker .NET SDK 10 for å bygge prosjektet.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Setter byggemodus til Release.
ARG configuration=Release

# Setter /src som arbeidsmappe.
WORKDIR /src

# Kopierer prosjektfilen.
COPY ["FirstWebAppInDocker.csproj", "./"]

# Laster ned nødvendige NuGet-pakker.
RUN dotnet restore "FirstWebAppInDocker.csproj"

# Kopierer resten av prosjektet.
COPY . .

# Bygger prosjektet.
RUN dotnet build "FirstWebAppInDocker.csproj" \
    -c $configuration \
    -o /app/build


# ========================================
# PUBLISH
# ========================================

FROM build AS publish

# Bruker Release som byggemodus.
ARG configuration=Release

# Publiserer applikasjonen.
RUN dotnet publish "FirstWebAppInDocker.csproj" \
    -c $configuration \
    -o /app/publish \
    /p:UseAppHost=false


# ========================================
# FINAL - FERDIG APPLIKASJON
# ========================================

FROM base AS final

# Setter /app som arbeidsmappe.
WORKDIR /app

# Kopierer den publiserte applikasjonen.
COPY --from=publish /app/publish .

# Starter ASP.NET Core-applikasjonen.
ENTRYPOINT ["dotnet", "FirstWebAppInDocker.dll"]
