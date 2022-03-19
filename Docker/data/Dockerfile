FROM microsoft/mssql-server-linux:latest
RUN mkdir -p /usr/work
WORKDIR /usr/work
COPY . /usr/work/
RUN chmod +x /usr/work/run-initialization.sh
EXPOSE 1433
CMD /bin/bash ./entrypoint.sh