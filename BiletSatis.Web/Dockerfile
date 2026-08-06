# syntax=docker/dockerfile:1

# ---------- Build & yayınlama aşaması ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Önce sadece .csproj'u kopyalayıp restore et — Docker layer cache'inden yararlanmak için
# (kaynak kod değişse bile paketler değişmediyse restore adımı tekrar çalışmaz).
COPY BiletSatis.Web/BiletSatis.Web.csproj BiletSatis.Web/
RUN dotnet restore BiletSatis.Web/BiletSatis.Web.csproj

COPY BiletSatis.Web/ BiletSatis.Web/
WORKDIR /src/BiletSatis.Web
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---------- Çalıştırma aşaması ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "BiletSatis.Web.dll"]
