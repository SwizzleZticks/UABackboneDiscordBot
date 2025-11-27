# .NET 8 + Playwright browsers preinstalled
FROM mcr.microsoft.com/playwright/dotnet:v1.44.0-jammy

# Build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app --no-self-contained

# Runtime
WORKDIR /app
ENTRYPOINT ["dotnet", "UABackoneBot.dll"]
