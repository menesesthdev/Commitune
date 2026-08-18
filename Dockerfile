FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on the project files alone, so editing code doesn't invalidate the layer.
COPY Directory.Build.props Commitune.slnx ./
COPY src/Commitune.Domain/Commitune.Domain.csproj src/Commitune.Domain/
COPY src/Commitune.Infrastructure/Commitune.Infrastructure.csproj src/Commitune.Infrastructure/
COPY src/Commitune.Api/Commitune.Api.csproj src/Commitune.Api/
COPY src/Commitune.Tests/Commitune.Tests.csproj src/Commitune.Tests/
RUN dotnet restore Commitune.slnx

COPY . .
RUN dotnet publish src/Commitune.Api/Commitune.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Data Protection keys live on a mounted volume so they survive redeploys — losing them
# means every stored GitHub token becomes undecryptable.
RUN mkdir -p /keys && chown $APP_UID:$APP_UID /keys
ENV DATA_PROTECTION_KEY_PATH=/keys \
    ASPNETCORE_HTTP_PORTS=8080

COPY --from=build /app .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Commitune.Api.dll"]
