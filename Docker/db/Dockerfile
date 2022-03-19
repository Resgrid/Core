#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.
ARG BUILD_VERSION=3.5.0

# Choose ubuntu version
FROM mcr.microsoft.com/mssql/server:2019-CU13-ubuntu-20.04

# Create app directory
WORKDIR /usr/src/app

# Copy initialization scripts
COPY . /usr/src/app

# Expose port 1433 in case accessing from other container
# Expose port externally from docker-compose.yml
EXPOSE 1433

# Run Microsoft SQl Server and initialization script (at the same time)
CMD /bin/bash /usr/src/app/entrypoint.sh
