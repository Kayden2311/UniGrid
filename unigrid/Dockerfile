FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY unigrid.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish unigrid.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./
# Render/Railway inject PORT; fall back to 8080 locally.
ENV ASPNETCORE_ENVIRONMENT=Production
CMD ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet unigrid.dll"]
