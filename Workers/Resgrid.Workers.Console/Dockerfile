# syntax=docker/dockerfile:1.7
#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
ARG BUILD_VERSION=3.5.0

#FROM mcr.microsoft.com/dotnet/runtime:9.0.3-noble-amd64 AS base
FROM dhi.io/dotnet:9.0.16-debian13@sha256:4797903e46b475875f4a402aa6f42a1dcc78cfe67d217f9f5f49e358b60d47a3 AS base
ARG BUILD_VERSION
WORKDIR /app

#FROM mcr.microsoft.com/dotnet/sdk:9.0.202-noble-amd64 AS build
FROM dhi.io/dotnet:9.0.314-sdk-debian13@sha256:a3acd51de0af79878e26292b3053aab513c6ca4476ddb2f9f11adb9c04aa7c89 AS build
ARG BUILD_VERSION
WORKDIR /src

COPY ["Workers/Resgrid.Workers.Console/Resgrid.Workers.Console.csproj", "Workers/Resgrid.Workers.Console/"]
COPY ["Providers/Resgrid.Providers.Bus.Rabbit/Resgrid.Providers.Bus.Rabbit.csproj", "Providers/Resgrid.Providers.Bus.Rabbit/"]
COPY ["Core/Resgrid.Framework/Resgrid.Framework.csproj", "Core/Resgrid.Framework/"]
COPY ["Core/Resgrid.Config/Resgrid.Config.csproj", "Core/Resgrid.Config/"]
COPY ["Core/Resgrid.Model/Resgrid.Model.csproj", "Core/Resgrid.Model/"]
COPY ["Providers/Resgrid.Providers.AddressVerification/Resgrid.Providers.AddressVerification.csproj", "Providers/Resgrid.Providers.AddressVerification/"]
COPY ["Core/Resgrid.Services/Resgrid.Services.csproj", "Core/Resgrid.Services/"]
COPY ["Providers/Resgrid.Providers.Bus/Resgrid.Providers.Bus.csproj", "Providers/Resgrid.Providers.Bus/"]
COPY ["Providers/Resgrid.Providers.Cache/Resgrid.Providers.Cache.csproj", "Providers/Resgrid.Providers.Cache/"]
COPY ["Providers/Resgrid.Providers.Geo/Resgrid.Providers.Geo.csproj", "Providers/Resgrid.Providers.Geo/"]
COPY ["Repositories/Resgrid.Repositories.DataRepository/Resgrid.Repositories.DataRepository.csproj", "Repositories/Resgrid.Repositories.DataRepository/"]
COPY ["Providers/Resgrid.Providers.Number/Resgrid.Providers.Number.csproj", "Providers/Resgrid.Providers.Number/"]
COPY ["Providers/Resgrid.Providers.Email/Resgrid.Providers.Email.csproj", "Providers/Resgrid.Providers.Email/"]
COPY ["Providers/Resgrid.Providers.Marketing/Resgrid.Providers.Marketing.csproj", "Providers/Resgrid.Providers.Marketing/"]
COPY ["Providers/Resgrid.Providers.Pdf/Resgrid.Providers.Pdf.csproj", "Providers/Resgrid.Providers.Pdf/"]
COPY ["Providers/Resgrid.Providers.Claims/Resgrid.Providers.Claims.csproj", "Providers/Resgrid.Providers.Claims/"]
COPY ["Workers/Resgrid.Workers.Framework/Resgrid.Workers.Framework.csproj", "Workers/Resgrid.Workers.Framework/"]
COPY ["Providers/Resgrid.Providers.Migrations/Resgrid.Providers.Migrations.csproj", "Providers/Resgrid.Providers.Migrations/"]
COPY ["Providers/Resgrid.Providers.Voip/Resgrid.Providers.Voip.csproj", "Providers/Resgrid.Providers.Voip/"]
RUN dotnet restore "Workers/Resgrid.Workers.Console/Resgrid.Workers.Console.csproj"
COPY . .
WORKDIR "/src/Workers/Resgrid.Workers.Console"

FROM build AS publish
ARG BUILD_VERSION
RUN dotnet publish "Resgrid.Workers.Console.csproj" -c Release -o /app/publish -p:Version=${BUILD_VERSION}
ADD --checksum=sha256:2241be671073520e028b2f12df1e9ef0419014cffb5670b7a80b2080804be17d https://github.com/ufoscout/docker-compose-wait/releases/download/2.12.1/wait /app/publish/wait
RUN chmod +x /app/publish/wait

## Install wkhtmltopdf and capture its runtime libraries
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        wget \
        fontconfig \
        fonts-dejavu-core \
        init-system-helpers \
        libfontconfig1 \
        libfreetype6 \
        libjpeg62-turbo \
        libpng16-16 \
        libssl3 \
        libx11-6 \
        libxext6 \
        libxrender1 \
        zlib1g \
    && wget -q https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6.1-3/wkhtmltox_0.12.6.1-3.bookworm_amd64.deb \
    && dpkg -i wkhtmltox_0.12.6.1-3.bookworm_amd64.deb || apt-get install -fy \
    && rm -f wkhtmltox_0.12.6.1-3.bookworm_amd64.deb \
    && mkdir -p /tmp/wkdeps \
    && ldd /usr/local/bin/wkhtmltopdf | awk '/=>/ {print $3}' | while read lib; do cp "$lib" /tmp/wkdeps/; done \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

FROM base AS final
WORKDIR /app

## Copy wkhtmltopdf binary + runtime libraries from publish stage
COPY --from=publish /usr/local/bin/wkhtmltopdf /usr/local/bin/wkhtmltopdf
COPY --from=publish /tmp/wkdeps/ /usr/local/lib/wkhtmltopdf/
COPY --from=publish /etc/fonts/ /etc/fonts/
COPY --from=publish /usr/share/fonts/ /usr/share/fonts/

ENV LD_LIBRARY_PATH="/usr/local/lib/wkhtmltopdf"

COPY --from=publish /app/publish .

# docker-compose-wait waits for WAIT_HOSTS, then execs WAIT_COMMAND.
# These hardened (distroless) images have no shell, so we can't chain with "sh -c".
ENV WAIT_COMMAND="dotnet Resgrid.Workers.Console.dll"
ENTRYPOINT ["./wait"]
