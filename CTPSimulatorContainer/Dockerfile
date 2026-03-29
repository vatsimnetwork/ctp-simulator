FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY CTPSimulator/CTPSimulator.csproj ./CTPSimulator/
COPY CTPSimulatorContainer/CTPSimulatorContainer.csproj ./CTPSimulatorContainer/
RUN dotnet restore ./CTPSimulatorContainer/CTPSimulatorContainer.csproj

COPY CTPSimulator/ ./CTPSimulator/
COPY CTPSimulatorContainer/ ./CTPSimulatorContainer/
RUN dotnet publish ./CTPSimulatorContainer/CTPSimulatorContainer.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .
ENTRYPOINT ["dotnet", "CTPSimulatorContainer.dll"]
