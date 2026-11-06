# syntax=docker/dockerfile:1.7
# Builds the Core hosts the QA stack runs, from this checkout, so the QA departments are always seeded into the
# schema of the code under test. Build context: the Core repository root.
#   docker build -f Docker/qa-departments/stack/core.Dockerfile --target console -t resgrid-qa/console:local .
#   docker build -f Docker/qa-departments/stack/core.Dockerfile --target api -t resgrid-qa/api:local .
#   docker build -f Docker/qa-departments/stack/core.Dockerfile --target eventing -t resgrid-qa/eventing:local .
# stack/qa-stack.py builds all three. The production Dockerfiles stay the release path; this one skips their
# hardened bases and wkhtmltopdf (PDF endpoints are not part of the app regression suite).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Tools/Resgrid.Console/Resgrid.Console.csproj \
    && dotnet restore Web/Resgrid.Web.Services/Resgrid.Web.Services.csproj \
    && dotnet restore Web/Resgrid.Web.Eventing/Resgrid.Web.Eventing.csproj

FROM build AS publish-console
RUN dotnet publish Tools/Resgrid.Console/Resgrid.Console.csproj -c Release --no-restore -o /out/console

FROM build AS publish-api
RUN dotnet publish Web/Resgrid.Web.Services/Resgrid.Web.Services.csproj -c Release --no-restore -o /out/api

FROM build AS publish-eventing
RUN dotnet publish Web/Resgrid.Web.Eventing/Resgrid.Web.Eventing.csproj -c Release --no-restore -o /out/eventing

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
# TimeZoneInfo needs the IANA zones: the QA departments run in several time zones on purpose.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*
ENV TZ=Etc/UTC DOTNET_EnableDiagnostics=0
WORKDIR /app

FROM runtime AS console
COPY --from=publish-console /out/console .
ENTRYPOINT ["dotnet", "Resgrid.Console.dll"]

FROM runtime AS api
COPY --from=publish-api /out/api .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Resgrid.Web.Services.dll"]

FROM runtime AS eventing
COPY --from=publish-eventing /out/eventing .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Resgrid.Web.Eventing.dll"]
