FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY TelegramAiBot.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        chromium \
        fonts-dejavu \
        fonts-liberation \
        nodejs \
        npm \
    && rm -rf /var/lib/apt/lists/*

COPY package.json ./
RUN npm install --omit=dev --no-audit --no-fund \
    && npm cache clean --force

COPY --from=build /app/publish ./
COPY scripts ./scripts

ENV CHROMIUM_PATH=/usr/bin/chromium

ENTRYPOINT ["dotnet", "TelegramAiBot.dll"]
