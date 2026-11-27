# syntax=docker/dockerfile:1.7
# resgrid-sealing: the EU DSS demo webapp (esig/dss-demonstrations, LGPL-2.1) as Resgrid's PAdES sealing
# sidecar (protocols-training-knowledge-plan §22.2; Core client Providers/Resgrid.Providers.Pdf/Sealing/
# DssRestClient.cs). The webapp is built unmodified from the pinned upstream tag; only the bases differ from
# upstream's Dockerfile (maven + tomcat:11.0-jdk25-temurin): Docker Hardened Images for both stages, and the
# runtime is non-root (uid 65532).
#
#   docker build -t resgridllc/resgridsealing:6.5.1 Docker/sealing
#
# CI (.github/workflows/dotnet.yml) publishes it with the other images as resgridllc/resgridsealing, tagged with the
# release version, latest, the commit and the DSS version (extra_tag, keep it equal to DSS_VERSION below).
#
# Runtime configuration is not baked in. The demo ships a mock TSP; the production TSA comes from a Spring beans
# file mounted at run time and selected with -Dtsp-source (see tsp-config.example.xml and int-Fleet
# resgrid/deployments/resgrid-sealing.yaml). Port 8080, REST under /services/rest.
ARG DSS_VERSION=6.5.1
# refs/tags/6.5.1^{} in https://github.com/esig/dss-demonstrations
ARG DSS_COMMIT=3c79488771671220542267129a9419586832ad55

FROM dhi.io/eclipse-temurin:25-jdk-dev@sha256:db1365c1f17aca71c5f055f40e176ecd3e7bcf707ee2c1d17cd8b523a24c9210 AS build
ARG DSS_VERSION
ARG DSS_COMMIT
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates git maven \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
## Fail the build if the tag no longer points at the reviewed commit.
RUN git clone --depth 1 --branch "${DSS_VERSION}" https://github.com/esig/dss-demonstrations.git . \
    && test "$(git rev-parse HEAD)" = "${DSS_COMMIT}"
## Same modules and profiles as upstream's Dockerfile: the webapp embeds the standalone-app zip for its download
## page, so the standalone app has to be packaged first.
RUN --mount=type=cache,target=/root/.m2 \
    mvn -B -ntp package -pl dss-standalone-app,dss-standalone-app-package,dss-demo-webapp -P quick,linux \
    && cp dss-demo-webapp/target/dss-demo-webapp-*.war /src/ROOT.war

FROM dhi.io/tomcat:11-jdk25@sha256:8b18ea5b7cbbc5ef8268776705e6705b84782ce4c568f1de54e65b1caa839d3a AS final
## webapps/ belongs to nonroot in this image, so Tomcat can expand the WAR without root; logs, temp and work are
## world-writable.
COPY --from=build --chown=65532:65532 /src/ROOT.war /usr/local/tomcat/webapps/ROOT.war
EXPOSE 8080
USER 65532
CMD ["catalina.sh", "run"]
