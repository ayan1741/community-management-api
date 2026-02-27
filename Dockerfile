FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY CommunityManagement.sln .
COPY src/CommunityManagement.Core/CommunityManagement.Core.csproj src/CommunityManagement.Core/
COPY src/CommunityManagement.Application/CommunityManagement.Application.csproj src/CommunityManagement.Application/
COPY src/CommunityManagement.Infrastructure/CommunityManagement.Infrastructure.csproj src/CommunityManagement.Infrastructure/
COPY src/CommunityManagement.Api/CommunityManagement.Api.csproj src/CommunityManagement.Api/
RUN dotnet restore src/CommunityManagement.Api/CommunityManagement.Api.csproj

COPY . .
RUN dotnet publish src/CommunityManagement.Api/CommunityManagement.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet CommunityManagement.Api.dll"]
