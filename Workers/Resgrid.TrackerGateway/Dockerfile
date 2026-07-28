# syntax=docker/dockerfile:1.7

ARG BUILD_VERSION=3.5.0

FROM dhi.io/aspnetcore:9.0.16-debian13@sha256:961647e80202ce33fc06472dda4e7ae2d2bc56d819aee6742602f70047b13dc7 AS base
ARG BUILD_VERSION
WORKDIR /app
EXPOSE 8080/tcp
EXPOSE 5004/tcp 5004/udp
EXPOSE 5023/tcp 5023/udp
EXPOSE 5027/tcp 5027/udp

FROM dhi.io/dotnet:9.0.314-sdk-debian13@sha256:a3acd51de0af79878e26292b3053aab513c6ca4476ddb2f9f11adb9c04aa7c89 AS build
ARG BUILD_VERSION
WORKDIR /src

COPY ["Workers/Resgrid.TrackerGateway/Resgrid.TrackerGateway.csproj", "Workers/Resgrid.TrackerGateway/"]
COPY ["Core/Resgrid.Config/Resgrid.Config.csproj", "Core/Resgrid.Config/"]
COPY ["Core/Resgrid.Framework/Resgrid.Framework.csproj", "Core/Resgrid.Framework/"]
COPY ["Core/Resgrid.Model/Resgrid.Model.csproj", "Core/Resgrid.Model/"]
COPY ["Core/Resgrid.Services/Resgrid.Services.csproj", "Core/Resgrid.Services/"]
COPY ["Providers/Resgrid.Providers.AddressVerification/Resgrid.Providers.AddressVerification.csproj", "Providers/Resgrid.Providers.AddressVerification/"]
COPY ["Providers/Resgrid.Providers.Bus/Resgrid.Providers.Bus.csproj", "Providers/Resgrid.Providers.Bus/"]
COPY ["Providers/Resgrid.Providers.Bus.Rabbit/Resgrid.Providers.Bus.Rabbit.csproj", "Providers/Resgrid.Providers.Bus.Rabbit/"]
COPY ["Providers/Resgrid.Providers.Cache/Resgrid.Providers.Cache.csproj", "Providers/Resgrid.Providers.Cache/"]
COPY ["Providers/Resgrid.Providers.Email/Resgrid.Providers.Email.csproj", "Providers/Resgrid.Providers.Email/"]
COPY ["Providers/Resgrid.Providers.Geo/Resgrid.Providers.Geo.csproj", "Providers/Resgrid.Providers.Geo/"]
COPY ["Providers/Resgrid.Providers.Marketing/Resgrid.Providers.Marketing.csproj", "Providers/Resgrid.Providers.Marketing/"]
COPY ["Providers/Resgrid.Providers.Migrations/Resgrid.Providers.Migrations.csproj", "Providers/Resgrid.Providers.Migrations/"]
COPY ["Providers/Resgrid.Providers.MigrationsPg/Resgrid.Providers.MigrationsPg.csproj", "Providers/Resgrid.Providers.MigrationsPg/"]
COPY ["Providers/Resgrid.Providers.Number/Resgrid.Providers.Number.csproj", "Providers/Resgrid.Providers.Number/"]
COPY ["Providers/Resgrid.Providers.Pdf/Resgrid.Providers.Pdf.csproj", "Providers/Resgrid.Providers.Pdf/"]
COPY ["Providers/Resgrid.Providers.Tracking/Resgrid.Providers.Tracking.csproj", "Providers/Resgrid.Providers.Tracking/"]
COPY ["Providers/Resgrid.Providers.Voip/Resgrid.Providers.Voip.csproj", "Providers/Resgrid.Providers.Voip/"]
COPY ["Repositories/Resgrid.Repositories.DataRepository/Resgrid.Repositories.DataRepository.csproj", "Repositories/Resgrid.Repositories.DataRepository/"]
RUN dotnet restore "Workers/Resgrid.TrackerGateway/Resgrid.TrackerGateway.csproj"

COPY . .
WORKDIR "/src/Workers/Resgrid.TrackerGateway"

FROM build AS publish
ARG BUILD_VERSION
RUN DEBIAN_FRONTEND=noninteractive apt-get update \
    && apt-get install -y --reinstall --no-install-recommends tzdata \
    && test -f /usr/share/zoneinfo/America/New_York \
    && test -f /usr/share/zoneinfo/Asia/Kolkata \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet publish "Resgrid.TrackerGateway.csproj" -c Release -o /app/publish -p:Version=${BUILD_VERSION}
ADD --checksum=sha256:2241be671073520e028b2f12df1e9ef0419014cffb5670b7a80b2080804be17d https://github.com/ufoscout/docker-compose-wait/releases/download/2.12.1/wait /app/publish/wait
# LocalXpose CLI (https://localxpose.io) used to expose the TCP/UDP ingest ports; checksum tracks the current linux/amd64 artifact.
ADD --checksum=sha256:b13e6914288e6c7d66744578e68b4eeacab5d629cd8fe3fb13863665d0a1ac95 https://api.localxpose.io/api/downloads/loclx-linux-amd64.zip /tmp/loclx.zip
RUN DEBIAN_FRONTEND=noninteractive apt-get update \
    && apt-get install -y --no-install-recommends unzip \
    && unzip /tmp/loclx.zip -d /app/publish \
    && rm -f /tmp/loclx.zip \
    && rm -rf /var/lib/apt/lists/* \
    && cp /src/Workers/Resgrid.TrackerGateway/localxpose-entrypoint.sh /app/publish/localxpose-entrypoint.sh \
    && chmod +x /app/publish/wait /app/publish/loclx /app/publish/localxpose-entrypoint.sh

FROM base AS final
WORKDIR /app
COPY --from=publish /usr/share/zoneinfo /usr/share/zoneinfo
# Hardened runtime has no shell; copy dash for the entrypoint and the CA bundle for loclx TLS.
COPY --from=publish /usr/bin/dash /usr/bin/dash
COPY --from=publish /etc/ssl/certs /etc/ssl/certs
ENV TZ=Etc/UTC
# Writable home for loclx state when tunnels are enabled (hardened runtime user has none).
ENV HOME=/tmp
COPY --from=publish /app/publish .
ENV WAIT_COMMAND="dotnet Resgrid.TrackerGateway.dll"
ENV LOCALXPOSE_ENABLED=false \
    LOCALXPOSE_REGION=us \
    LOCALXPOSE_TARGET_HOST=localhost \
    LOCALXPOSE_TCP_PORTS="5004 5023 5027" \
    LOCALXPOSE_UDP_PORTS="" \
    LOCALXPOSE_RESERVED_ENDPOINTS="" \
    LOCALXPOSE_RESTART_DELAY=5
ENTRYPOINT ["./localxpose-entrypoint.sh"]
