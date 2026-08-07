FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY TelegramAiBot.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine
WORKDIR /app

COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "TelegramAiBot.dll"]
