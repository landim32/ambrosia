---
name: wsl-docker
description: "How to run Docker on this machine. Docker Engine is installed inside WSL2 (Ubuntu 24.04) — there is no Docker Desktop and no `docker` binary on Windows. Use whenever you need to run docker, docker compose, build images, start/stop containers or inspect logs."
allowed-tools: Bash, PowerShell, Read, Grep, Glob
user-invocable: true
---

# Docker via WSL2

Nesta máquina o Docker **não** está disponível diretamente no Windows. O Docker Engine roda
nativamente dentro da distro WSL2 `Ubuntu`, e todo comando `docker` / `docker compose` DEVE ser
executado através do `wsl`.

## Ambiente

| Item | Valor |
|---|---|
| Host | Windows 11 (sem Docker Desktop, sem `docker` no PATH do Windows) |
| Distro WSL | `Ubuntu` (padrão, WSL 2, Ubuntu 24.04 LTS) |
| Usuário padrão | `landim32` (já está no grupo `docker` — não usar `sudo`) |
| Docker Engine | 29.x, serviço gerenciado pelo systemd (`systemd=true` em `/etc/wsl.conf`) |
| Docker Compose | Plugin v2 → usar `docker compose` (com espaço), nunca `docker-compose` |
| Socket | `unix:///var/run/docker.sock` (contexto `default`) |
| Recursos WSL | `~/.wslconfig`: 6 GB RAM, 4 CPUs, 8 GB swap |

## Mapeamento de caminhos

| Windows | WSL |
|---|---|
| `C:\repos\ambrosia` | `/mnt/c/repos/ambrosia` |
| `C:\<path>` | `/mnt/c/<path>` (barras normais, mesmo casing) |

O filesystem do Linux diferencia maiúsculas e minúsculas: o caminho e os nomes de arquivos
referenciados em `Dockerfile` / `docker-compose*.yml` DEVEM bater exatamente com o casing no disco.

## Como executar comandos

Sempre use `wsl.exe --cd <diretório> -e <comando>`. O diretório deve ser o que contém o
`docker-compose*.yml` (ou o contexto de build).

### Pela ferramenta PowerShell (preferida)

```powershell
wsl --cd /mnt/c/repos/ambrosia -e docker compose up -d --build
wsl --cd /mnt/c/repos/ambrosia -e docker compose ps
wsl -e docker ps -a
```

O `--cd` também aceita caminho Windows: `wsl --cd 'C:\repos\ambrosia' -e docker compose ps`.

### Pela ferramenta Bash (Git Bash / MSYS)

O Git Bash converte automaticamente argumentos que parecem caminhos POSIX (`/mnt/c/...` vira
`C:/Program Files/Git/mnt/c/...`) e o comando falha com `Wsl/ERROR_PATH_NOT_FOUND`. Para evitar:

```bash
# Opção 1: desabilitar a conversão de caminhos
MSYS_NO_PATHCONV=1 wsl.exe --cd /mnt/c/repos/ambrosia -e docker compose up -d

# Opção 2: passar o caminho no formato Windows
wsl.exe --cd 'C:\repos\ambrosia' -e docker compose up -d
```

### Pipes, variáveis e múltiplos comandos

Quando o comando precisar de pipe, `&&`, redirecionamento ou expansão de variável **dentro do
Linux**, envolva em `sh -c '...'` (aspas simples para o shell do Windows não interpretar):

```powershell
wsl --cd /mnt/c/repos/ambrosia -e sh -c 'docker compose logs --tail=100 api | grep -i error'
wsl -e sh -c 'docker ps --format "{{.Names}} {{.Status}}"'
```

## Comandos comuns

| Ação | Comando (PowerShell) |
|---|---|
| Subir serviços com rebuild | `wsl --cd <dir> -e docker compose up -d --build` |
| Subir com arquivo/ambiente específico | `wsl --cd <dir> -e docker compose --env-file .env -f docker-compose.yml up -d` |
| Status | `wsl --cd <dir> -e docker compose ps` |
| Logs (não seguir) | `wsl --cd <dir> -e docker compose logs --tail=200 <serviço>` |
| Parar | `wsl --cd <dir> -e docker compose stop` |
| Remover containers | `wsl --cd <dir> -e docker compose down` |
| Shell no container | `wsl -e docker exec <container> sh -c '<comando>'` |
| Listar containers | `wsl -e docker ps -a` |
| Listar imagens | `wsl -e docker images` |

## Regras

1. **Nunca** chame `docker` diretamente no PowerShell/Bash do Windows — o binário não existe.
   Sempre prefixe com `wsl` / `wsl.exe`.
2. **Não use `sudo`** — o usuário padrão já pertence ao grupo `docker`.
3. **Não use `-f` / `--follow` nem `-it`** em `logs`, `exec` ou `run`: as ferramentas não são
   interativas e o comando ficaria preso. Use `--tail=N`. Para processos longos (ex.: build
   demorado), use `run_in_background` da ferramenta.
4. **Não instale Docker Desktop** nem altere o engine do WSL sem pedido explícito do usuário.
5. **Ações destrutivas exigem confirmação**: `docker compose down -v`, `docker volume rm`,
   `docker system prune`, `docker rm -f` em containers que não foram criados na tarefa atual.
   Esta máquina hospeda containers de outros projetos (ex.: `anjoo_*`, `avabot-*`) — nunca os
   pare ou remova.
6. **Portas**: containers publicados no WSL2 ficam acessíveis no Windows via `localhost:<porta>`
   (localhost forwarding padrão). Antes de publicar uma porta, verifique conflitos com
   `wsl -e docker ps --format "{{.Names}} {{.Ports}}"`.
7. **Line endings**: scripts shell copiados para a imagem (`entrypoint.sh` etc.) DEVEM usar LF.
   Arquivos com CRLF falham no container com `/bin/sh^M: not found`.

## Troubleshooting

| Sintoma | Causa / Solução |
|---|---|
| `docker: command not found` no Windows | Esqueceu o prefixo `wsl`. |
| `Wsl/ERROR_PATH_NOT_FOUND` | Conversão de caminho do Git Bash — use `MSYS_NO_PATHCONV=1` ou caminho Windows. |
| `Cannot connect to the Docker daemon` | Serviço parado: `wsl -e sudo systemctl start docker` (pedir ao usuário se exigir senha). |
| Distro aparece `Stopped` em `wsl -l -v` | Normal — o WSL inicia sob demanda no primeiro `wsl -e ...`. |
| `permission denied` no socket | Verificar `wsl -e groups`; o usuário deve estar no grupo `docker`. |
| Build lento lendo `/mnt/c` | Esperado (I/O cross-filesystem). Garanta um `.dockerignore` excluindo `bin/`, `obj/`, `node_modules/`. |
| Container não acessível em `localhost` | Confirmar a porta publicada em `docker compose ps` e se o app escuta em `0.0.0.0`, não `127.0.0.1`. |

## Skills relacionadas

- `docker-compose-config` — padrão de `.env` e variáveis no `docker-compose.yml`.
- `dotnet-env` — ambientes Development / Docker / Production.
- `deploy-prod` — pipeline de deploy em produção (fora desta máquina).
