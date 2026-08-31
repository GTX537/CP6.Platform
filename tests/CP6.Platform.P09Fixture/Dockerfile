FROM mcr.microsoft.com/dotnet/sdk:8.0.424 AS build
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["global.json", "."]
COPY ["tests/CP6.Platform.P09Fixture/CP6.Platform.P09Fixture.csproj", "tests/CP6.Platform.P09Fixture/"]
COPY ["src/CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj", "src/CP6.Platform.Abstractions/"]
COPY ["src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj", "src/CP6.Platform.Contracts/"]
COPY ["src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj", "src/CP6.Platform.Deployment/"]
COPY ["src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj", "src/CP6.Platform.Messaging/"]
RUN dotnet restore "tests/CP6.Platform.P09Fixture/CP6.Platform.P09Fixture.csproj"

COPY . .
RUN dotnet publish "tests/CP6.Platform.P09Fixture/CP6.Platform.P09Fixture.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0.30 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "CP6.Platform.P09Fixture.dll"]
