FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["JxTerminal.Server.csproj", "./"]
RUN dotnet restore "JxTerminal.Server.csproj"

COPY . .
RUN dotnet publish "JxTerminal.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    Backup__Directory=/app/data

VOLUME ["/app/data"]

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JxTerminal.Server.dll"]
