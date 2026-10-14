FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY WebhookEngine.sln ./
COPY src/WebhookEngine.Api/WebhookEngine.Api.csproj src/WebhookEngine.Api/
COPY src/WebhookEngine.Core/WebhookEngine.Core.csproj src/WebhookEngine.Core/
COPY src/WebhookEngine.Infrastructure/WebhookEngine.Infrastructure.csproj src/WebhookEngine.Infrastructure/
RUN dotnet restore src/WebhookEngine.Api/WebhookEngine.Api.csproj

COPY src/ src/
RUN dotnet publish src/WebhookEngine.Api/WebhookEngine.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebhookEngine.Api.dll"]
