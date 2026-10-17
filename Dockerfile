# Build stage with .NET 10 SDK and Native AOT toolchain
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install clang and zlib needed for Native AOT compilation and linking
RUN apt-get update && apt-get install -y --no-install-recommends \
    clang \
    zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

COPY ["JxTerminal.Server.csproj", "./"]
RUN dotnet restore "JxTerminal.Server.csproj"

COPY . .

# Publish Native AOT binary (architecture is determined automatically by the build container)
RUN dotnet publish "JxTerminal.Server.csproj" \
    -c Release \
    -p:PublishAot=true \
    -p:StripSymbols=true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o /app/publish && \
    rm -f /app/publish/*.pdb

# Runtime stage using ultra-lean .NET runtime dependencies image
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
WORKDIR /app
EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    Backup__Directory=/app/data

VOLUME ["/app/data"]

COPY --from=build /app/publish .

RUN chmod +x ./JxTerminal.Server

ENTRYPOINT ["./JxTerminal.Server"]
