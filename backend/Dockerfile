# Imagem da API Ambrosia + bundle de migrações EF Core.
# Contexto de build: backend/  (docker compose usa context: ./backend)
#   target "final"    -> API (porta 8080)
#   target "migrator" -> aplica as migrações pendentes e sai

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore em camada própria: só invalida quando um .csproj muda.
COPY Ambrosia.API/Ambrosia.API.csproj Ambrosia.API/
COPY Ambrosia.Application/Ambrosia.Application.csproj Ambrosia.Application/
COPY Ambrosia.Domain/Ambrosia.Domain.csproj Ambrosia.Domain/
COPY Ambrosia.DTO/Ambrosia.DTO.csproj Ambrosia.DTO/
COPY Ambrosia.Infra/Ambrosia.Infra.csproj Ambrosia.Infra/
COPY Ambrosia.Infra.Interfaces/Ambrosia.Infra.Interfaces.csproj Ambrosia.Infra.Interfaces/
RUN dotnet restore Ambrosia.API/Ambrosia.API.csproj

COPY . .
RUN dotnet publish Ambrosia.API/Ambrosia.API.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

# Bundle de migrações: executável único que aplica as migrações usando a mesma configuração da
# API (ConnectionStrings__AmbrosiaContext e demais variáveis validadas no startup).
FROM build AS migrator-build
RUN dotnet tool restore
RUN dotnet ef migrations bundle \
      --project Ambrosia.Infra \
      --startup-project Ambrosia.API \
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
COPY --from=build /app/publish .
# Chaves do Data Protection num diretório do usuário app, montado como volume no compose para
# sobreviver à recriação do container (cookies e tokens protegidos continuam válidos).
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R app:app /home/app/.aspnet
USER app
ENTRYPOINT ["dotnet", "Ambrosia.API.dll"]
