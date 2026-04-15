FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY MauiBlazorHybrid.Core/MauiBlazorHybrid.Core.csproj MauiBlazorHybrid.Core/
COPY MauiBlazorHybrid.BackupServer/MauiBlazorHybrid.BackupServer.csproj MauiBlazorHybrid.BackupServer/
RUN dotnet restore MauiBlazorHybrid.BackupServer/MauiBlazorHybrid.BackupServer.csproj

COPY MauiBlazorHybrid.Core/ MauiBlazorHybrid.Core/
COPY MauiBlazorHybrid.BackupServer/ MauiBlazorHybrid.BackupServer/
RUN dotnet publish MauiBlazorHybrid.BackupServer/MauiBlazorHybrid.BackupServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

ENV Backup__StoragePath=/data/backups
ENV Backup__ListenUrls=http://0.0.0.0:5199

EXPOSE 5199
VOLUME /data/backups

ENTRYPOINT ["dotnet", "MauiBlazorHybrid.BackupServer.dll"]
