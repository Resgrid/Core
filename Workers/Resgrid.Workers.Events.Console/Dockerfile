#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/core/runtime:3.1-buster-slim AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["Workers/Resgrid.Workers.Events.Console/Resgrid.Workers.Events.Console.csproj", "Workers/Resgrid.Workers.Events.Console/"]
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
COPY ["Providers/Resgrid.Providers.Audio/Resgrid.Providers.Audio.csproj", "Providers/Resgrid.Providers.Audio/"]
COPY ["Providers/Resgrid.Providers.Marketing/Resgrid.Providers.Marketing.csproj", "Providers/Resgrid.Providers.Marketing/"]
COPY ["Providers/Resgrid.Providers.Pdf/Resgrid.Providers.Pdf.csproj", "Providers/Resgrid.Providers.Pdf/"]
COPY ["Providers/Resgrid.Providers.Claims/Resgrid.Providers.Claims.csproj", "Providers/Resgrid.Providers.Claims/"]
COPY ["Workers/Resgrid.Workers.Framework/Resgrid.Workers.Framework.csproj", "Workers/Resgrid.Workers.Framework/"]
COPY ["Providers/Resgrid.Providers.Migrations/Resgrid.Providers.Migrations.csproj", "Providers/Resgrid.Providers.Migrations/"]
RUN dotnet restore "Workers/Resgrid.Workers.Events.Console/Resgrid.Workers.Events.Console.csproj"
COPY . .
WORKDIR "/src/Workers/Resgrid.Workers.Events.Console"
RUN dotnet build "Resgrid.Workers.Events.Console.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Resgrid.Workers.Events.Console.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Resgrid.Workers.Events.Console.dll"]