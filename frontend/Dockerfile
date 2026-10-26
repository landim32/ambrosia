# Frontend da HU 002 (T089): build em Node, servido por nginx.
#
# O compose passa BUILD_MODE (homolog|production), VITE_DEV_LOGIN e
# VITE_GOOGLE_CLIENT_ID. As variaveis VITE_* sao embutidas no bundle EM BUILD
# TIME — por isso existem so no primeiro estagio e nao precisam de ENV no runtime
# (research §8, SC-008: nao ha como "ligar" o dev-login depois da imagem pronta).

# --- Estagio 1: build -------------------------------------------------------
FROM node:22-alpine AS build

WORKDIR /app

ARG BUILD_MODE=production
ARG VITE_DEV_LOGIN
ARG VITE_GOOGLE_CLIENT_ID

# So o lockfile: a camada de dependencias sobrevive a mudanca de codigo.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

ENV VITE_DEV_LOGIN=${VITE_DEV_LOGIN}
ENV VITE_GOOGLE_CLIENT_ID=${VITE_GOOGLE_CLIENT_ID}

# `--mode` escolhe o .env.<mode> do frontend (homolog | production). O script
# `build` roda `tsc -b && vite build`: a imagem nao nasce com tipo quebrado.
RUN npm run build -- --mode "${BUILD_MODE}"

# A prova da SC-008 so vale para producao: em homolog o dev-login PRECISA existir
# (VITE_DEV_LOGIN=true), entao o teste ali seria uma falha falsa.
RUN if [ "${BUILD_MODE}" = "production" ]; then node scripts/verify-prod.mjs; fi

# --- Estagio 2: runtime -----------------------------------------------------
FROM nginx:1.27-alpine

# SPA de origem unica: sem locale e sem SSL aqui (o gateway termina HTTPS).
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget -q --spider http://127.0.0.1/index.html || exit 1
