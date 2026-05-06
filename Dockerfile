# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props NuGet.Config ./
COPY src/AgentBlazor.Core/ src/AgentBlazor.Core/
COPY src/AgentBlazor.ProviderAdapters/ src/AgentBlazor.ProviderAdapters/
COPY src/AgentBlazor.Licensing/ src/AgentBlazor.Licensing/
COPY src/AgentBlazor.Hosting/ src/AgentBlazor.Hosting/
COPY src/AgentBlazor.Components/ src/AgentBlazor.Components/
COPY demo/AgentBlazor.Demo/ demo/AgentBlazor.Demo/

RUN dotnet restore demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj
RUN dotnet publish demo/AgentBlazor.Demo/AgentBlazor.Demo.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "AgentBlazor.Demo.dll"]
