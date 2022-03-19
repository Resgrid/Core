#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
ARG BUILD_VERSION=3.5.0

FROM mcr.microsoft.com/dotnet/runtime:6.0.1-bullseye-slim-amd64 AS base
ARG BUILD_VERSION
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0.101-bullseye-slim-amd64 AS build
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
COPY ["Providers/Resgrid.Providers.Firebase/Resgrid.Providers.Firebase.csproj", "Providers/Resgrid.Providers.Firebase/"]
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

FROM base AS final
## Add the wait script to the image
ADD https://github.com/ufoscout/docker-compose-wait/releases/download/2.9.0/wait wait
RUN chmod +x wait

WORKDIR /app

## START - INSTALL WKHTMLTOPDF
ENV WKHTMLTOX wkhtmltox_0.12.6-1.buster_amd64.deb
ENV BUILD_PACKAGES build-essential
ENV MAIN_PACKAGES fontconfig libfreetype6 libjpeg62-turbo libxext6 libpng16-16 libx11-6 libxcb1 libxrender1 xfonts-75dpi xfonts-base libgdiplus libc6-dev wget

RUN set -xe \
    && apt-get update -qq \
    && apt-get install --no-install-recommends -yq $BUILD_PACKAGES $MAIN_PACKAGES \
    && wget https://github.com/wkhtmltopdf/packaging/releases/download/0.12.6-1/wkhtmltox_0.12.6-1.buster_amd64.deb \
    && dpkg -i ${WKHTMLTOX} \
    && apt-get remove -y $BUILD_PACKAGES \
    && apt-get autoremove -y \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/* \
    && rm -rf ${WKHTMLTOX} \
    && truncate -s 0 /var/log/*log
## END - INSTALL WKHTMLTPDF

COPY --from=publish /app/publish .

ENTRYPOINT ["sh", "-c", "./wait && dotnet Resgrid.Workers.Console.dll"]
