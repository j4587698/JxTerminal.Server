# ---- Build stage: Alpine (musl) ----
# The runtime stage is a plain Alpine image, so the binary must be linked against musl.
# Building in the Debian-based SDK would produce a glibc binary that cannot run on Alpine.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Native AOT toolchain: clang as the linker driver, build-base for musl-dev/binutils,
# zlib-dev because the runtime links zlib.
RUN apk add --no-cache clang build-base zlib-dev

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

# ---- Runtime stage: plain Alpine, no .NET image at all ----
# An AOT binary is a native executable: it needs a C runtime and libstdc++, nothing else.
# (The previous runtime-deps image was not the .NET runtime either - it only carried the
#  native prerequisites, but it also dragged in the Debian userland and ICU.)
FROM alpine:3.22 AS final
WORKDIR /app

# musl libc comes from the base image; libstdc++ (+ libgcc) is what AOT output links against.
RUN apk add --no-cache libstdc++ ca-certificates

EXPOSE 5000

ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    Backup__Directory=/app/data

VOLUME ["/app/data"]

COPY --from=build /app/publish .

# Build-time smoke tests: prove the musl binary actually runs inside this image, so a
# missing shared library or a glibc/musl mismatch fails the image build instead of
# crash-looping in production.
# 1) CLI path (TinyDb + AOT startup)
RUN ./JxTerminal.Server --data-dir /tmp/cli-smoke --server-help > /dev/null && \
    rm -rf /tmp/cli-smoke

# 2) Web path: start Kestrel and require a real 200 from /api/health (exercises the
#    minimal-API pipeline and the source-generated JSON in the final image)
RUN set -eux; \
    ./JxTerminal.Server --data-dir /tmp/web-smoke > /tmp/web-smoke.log 2>&1 & \
    pid=$!; \
    ok=0; i=0; \
    while [ $i -lt 40 ]; do \
        if wget -q -O - http://127.0.0.1:5000/api/health 2>/dev/null | grep -q '"status":"ok"'; then ok=1; break; fi; \
        i=$((i+1)); sleep 0.5; \
    done; \
    kill $pid || true; \
    cat /tmp/web-smoke.log; \
    rm -rf /tmp/web-smoke /tmp/web-smoke.log; \
    [ $ok -eq 1 ]

# 3) What the binary links against (musl libc proves the RID matched the base image)
RUN ldd ./JxTerminal.Server || true

ENTRYPOINT ["./JxTerminal.Server"]
