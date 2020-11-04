#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["Web/Resgrid.Web.Eventing/Resgrid.Web.Eventing.csproj", "Web/Resgrid.Web.Eventing/"]
RUN dotnet restore "Web/Resgrid.Web.Eventing/Resgrid.Web.Eventing.csproj"
COPY . .
WORKDIR "/src/Web/Resgrid.Web.Eventing"
RUN dotnet build "Resgrid.Web.Eventing.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Resgrid.Web.Eventing.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Resgrid.Web.Eventing.dll"]