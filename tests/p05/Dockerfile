FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore tests/CP6.Platform.DaprKafkaFixture/CP6.Platform.DaprKafkaFixture.csproj
RUN dotnet publish tests/CP6.Platform.DaprKafkaFixture/CP6.Platform.DaprKafkaFixture.csproj --configuration Release --no-restore --output /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
COPY --from=build /src/contracts /contracts
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CP6.Platform.DaprKafkaFixture.dll"]
