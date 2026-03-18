# ─────────────────────────────────────────────────────────────────────────────
# Nodus.API Dockerfile (.NET 10)
# ─────────────────────────────────────────────────────────────────────────────

# --- Build Stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["Nodus.API.csproj", "./"]
RUN dotnet restore "Nodus.API.csproj"

# Copy the rest of the source code
COPY . .

# Build and publish the application
RUN dotnet publish "Nodus.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- Runtime Stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Expose the port Render expects (usually 10000, but ASP.NET typically uses 8080/80)
# Render injections PORT env var, ASP.NET Core 8+ automatically listens on its default port.
EXPOSE 8080

# Copy the build artifacts from the build stage
COPY --from=build /app/publish .

# Environment variables for production
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Entry point for the application
ENTRYPOINT ["dotnet", "Nodus.API.dll"]
