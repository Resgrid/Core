# syntax=docker/dockerfile:1.7
# Protected Data Broker — the ONLY image with a KMS route. Deploy on an isolated subnet;
# the broker's OpenBao client certificate is a mounted secret, never baked into this image.
ARG BUILD_VERSION=3.5.0

FROM dhi.io/aspnetcore:9.0.16-debian13@sha256:961647e80202ce33fc06472dda4e7ae2d2bc56d819aee6742602f70047b13dc7 AS base
ARG BUILD_VERSION
WORKDIR /app
EXPOSE 80

FROM dhi.io/dotnet:9.0.314-sdk-debian13@sha256:a3acd51de0af79878e26292b3053aab513c6ca4476ddb2f9f11adb9c04aa7c89 AS build
ARG BUILD_VERSION
WORKDIR /src
## Root MSBuild files must be present for the restore layer, otherwise the
## security pins in Directory.Build.targets are missing from project.assets.json.
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Build.targets", "./"]
COPY ["Web/Resgrid.Web.Broker/Resgrid.Web.Broker.csproj", "Web/Resgrid.Web.Broker/"]
COPY ["Core/Resgrid.Framework/Resgrid.Framework.csproj", "Core/Resgrid.Framework/"]
COPY ["Core/Resgrid.Config/Resgrid.Config.csproj", "Core/Resgrid.Config/"]
COPY ["Core/Resgrid.Model/Resgrid.Model.csproj", "Core/Resgrid.Model/"]
COPY ["Core/Resgrid.Services/Resgrid.Services.csproj", "Core/Resgrid.Services/"]
COPY ["Providers/Resgrid.Providers.ProtectedData/Resgrid.Providers.ProtectedData.csproj", "Providers/Resgrid.Providers.ProtectedData/"]
RUN dotnet restore "Web/Resgrid.Web.Broker/Resgrid.Web.Broker.csproj"
COPY . .
WORKDIR "/src/Web/Resgrid.Web.Broker"

FROM build AS publish
ARG BUILD_VERSION
## The hardened DHI image marks tzdata as installed (dpkg) but ships none of the
## zone files (see the Eventing Dockerfile note); the migration window scheduler
## needs real zone files, so fail the build loudly if they are missing.
RUN DEBIAN_FRONTEND=noninteractive apt-get update \
    && apt-get install -y --reinstall --no-install-recommends tzdata \
    && test -f /usr/share/zoneinfo/America/New_York \
    && test -f /usr/share/zoneinfo/Asia/Kolkata \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet publish "Resgrid.Web.Broker.csproj" -c Release -o /app/publish -p:Version=${BUILD_VERSION}
ADD --checksum=sha256:2241be671073520e028b2f12df1e9ef0419014cffb5670b7a80b2080804be17d https://github.com/ufoscout/docker-compose-wait/releases/download/2.12.1/wait /app/publish/wait
RUN chmod +x /app/publish/wait

FROM base AS final
WORKDIR /app
COPY --from=publish /usr/share/zoneinfo /usr/share/zoneinfo
ENV TZ=Etc/UTC
COPY --from=publish /app/publish .
ENV WAIT_COMMAND="dotnet Resgrid.Web.Broker.dll"
ENTRYPOINT ["./wait"]
