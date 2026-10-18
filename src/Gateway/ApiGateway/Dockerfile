FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS build
WORKDIR /src
COPY Directory.Build.props EvolutionaryLogistics.slnx ./
COPY src/ ./src/
RUN dotnet restore src/Gateway/ApiGateway/ApiGateway.csproj
RUN dotnet publish src/Gateway/ApiGateway/ApiGateway.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
