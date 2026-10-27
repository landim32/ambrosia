# Imagem da API Ambrosio + bundle de migrações EF Core.
# Contexto de build: backend/  (docker compose usa context: ./backend)
#   target "final"    -> API (porta 8080)
#   target "migrator" -> aplica as migrações pendentes e sai

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
WORKDIR /src

# Restore em camada própria: só invalida quando um .csproj muda.
COPY Ambrosio.API/Ambrosio.API.csproj Ambrosio.API/
COPY Ambrosio.Application/Ambrosio.Application.csproj Ambrosio.Application/
COPY Ambrosio.Domain/Ambrosio.Domain.csproj Ambrosio.Domain/
COPY Ambrosio.DTO/Ambrosio.DTO.csproj Ambrosio.DTO/
COPY Ambrosio.Infra/Ambrosio.Infra.csproj Ambrosio.Infra/
COPY Ambrosio.Infra.Interfaces/Ambrosio.Infra.Interfaces.csproj Ambrosio.Infra.Interfaces/
RUN dotnet restore Ambrosio.API/Ambrosio.API.csproj

# Chromium do coletor do +Vendas (feature 006, research §2). Parte da camada de restore — e não do
# publish — para só invalidar quando a VERSÃO FIXA do Microsoft.Playwright mudar no .csproj (o
# download é de ~260 MB). O pacote NuGet já traz o driver (node + cli.js) em .playwright/; é ele que
# o playwright.ps1 da saída do build chamaria, então não é preciso PowerShell na imagem.
FROM restore AS playwright
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN set -eu; \
    driver="$(ls -d /root/.nuget/packages/microsoft.playwright/*/.playwright | head -n 1)"; \
    chmod +x "$driver/node/linux-x64/node"; \
    "$driver/node/linux-x64/node" "$driver/package/cli.js" install chromium

FROM restore AS build
COPY . .
RUN dotnet publish Ambrosio.API/Ambrosio.API.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

# Bundle de migrações: executável único que aplica as migrações usando a mesma configuração da
# API (ConnectionStrings__AmbrosioContext e demais variáveis validadas no startup).
FROM build AS migrator-build
RUN dotnet tool restore
RUN dotnet ef migrations bundle \
      --project Ambrosio.Infra \
      --startup-project Ambrosio.API \
      --configuration Release \
      --self-contained -r linux-x64 \
      --output /app/efbundle --force

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS migrator
WORKDIR /app
COPY --from=migrator-build /app/efbundle ./efbundle
COPY --from=build /app/publish/appsettings*.json ./
USER app
ENTRYPOINT ["./efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Dependências de sistema do Chromium headless (Debian 12, a base da aspnet:8.0). Lista fixa em vez
# de `playwright install --with-deps`: a imagem final não tem SDK nem pwsh, e uma lista explícita é
# auditável. fonts-liberation dá métricas de fonte estáveis para a leitura do DOM.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libasound2 libatk-bridge2.0-0 libatk1.0-0 libatspi2.0-0 libcairo2 libcups2 libdbus-1-3 \
        libdrm2 libexpat1 libgbm1 libglib2.0-0 libnspr4 libnss3 libpango-1.0-0 libx11-6 libxcb1 \
        libxcomposite1 libxdamage1 libxext6 libxfixes3 libxkbcommon0 libxrandr2 fonts-liberation \
    && rm -rf /var/lib/apt/lists/*
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY --from=playwright --chown=app:app /ms-playwright /ms-playwright
COPY --from=build /app/publish .
# O driver do Playwright (node) sai do pacote NuGet como rwxr--r-- e dono root: o usuário app não
# consegue executá-lo e a coleta falha com "Permission denied". Libera a execução para todos.
# Chaves do Data Protection num diretório do usuário app, montado como volume no compose para
# sobreviver à recriação do container (cookies e tokens protegidos continuam válidos).
RUN chmod 755 /app/.playwright/node/linux-x64/node \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
USER app
ENTRYPOINT ["dotnet", "Ambrosio.API.dll"]
