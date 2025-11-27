# --- BASE IMAGE ---
# Contains .NET, Playwright, and ALL browsers preinstalled
FROM mcr.microsoft.com/playwright/dotnet:v1.44.0-focal

# --- BUILD APP ---
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app --no-self-contained

# --- RUNTIME ---
WORKDIR /app
ENTRYPOINT ["dotnet", "UABackoneBot.dll"]
