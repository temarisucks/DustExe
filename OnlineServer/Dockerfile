FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY OnlineServer/Dust.OnlineServer.csproj OnlineServer/
RUN dotnet restore OnlineServer/Dust.OnlineServer.csproj
COPY OnlineServer/ OnlineServer/
RUN dotnet publish OnlineServer/Dust.OnlineServer.csproj \
    -c Release \
    --no-restore \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o /output

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /output/ ./
ENV OnlineServer__AccountFile=/data/accounts.json
dustexe-production.up.railway.appEXPOSE 5077
ENTRYPOINT ["dotnet", "Dust.OnlineServer.dll"]
