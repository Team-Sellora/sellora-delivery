FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Sellora.DeliveryService.Api/Sellora.DeliveryService.Api.csproj src/Sellora.DeliveryService.Api/
COPY src/Sellora.DeliveryService.Application/Sellora.DeliveryService.Application.csproj src/Sellora.DeliveryService.Application/
COPY src/Sellora.DeliveryService.Domain/Sellora.DeliveryService.Domain.csproj src/Sellora.DeliveryService.Domain/
COPY src/Sellora.DeliveryService.Infrastructure/Sellora.DeliveryService.Infrastructure.csproj src/Sellora.DeliveryService.Infrastructure/
RUN dotnet restore src/Sellora.DeliveryService.Api/Sellora.DeliveryService.Api.csproj

COPY src/ src/
RUN dotnet publish src/Sellora.DeliveryService.Api/Sellora.DeliveryService.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ARG BUILD_COMMIT_SHA=unknown
ENV BUILD_COMMIT_SHA=$BUILD_COMMIT_SHA
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Sellora.DeliveryService.Api.dll"]
