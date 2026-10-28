# Rotas do gateway de homologação. Em produção as mesmas rotas ficam no server block ambrosiaai.site
# do nginx.conf do repositório emagine-deploy (deploy/README.md).
# Os upstreams são resolvidos em tempo de requisição (resolver do Docker): se um serviço ainda não
# existir — frontend (HU 002), auth (HU 007), mcp (HU 006) —, o nginx sobe mesmo assim e a rota
# responde 503 em vez de derrubar o gateway.

resolver 127.0.0.11 valid=10s ipv6=off;

client_max_body_size 20m;

proxy_http_version 1.1;
proxy_set_header Host              $host;
proxy_set_header X-Real-IP         $remote_addr;
proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $forwarded_proto;
proxy_set_header X-Forwarded-Host  $host;

# API REST (e futuramente /api/graphql). O prefixo /api é removido antes de chegar à API.
location = /api {
    return 301 /api/;
}

location /api/ {
    set $api_upstream http://api:8080;
    rewrite ^/api/(.*)$ /$1 break;
    # `proxy_set_header` só herda do nível de cima quando a location não declara nenhum: como esta
    # declara o X-Forwarded-Prefix, os demais cabeçalhos se repetem aqui.
    proxy_set_header Host               $host;
    proxy_set_header X-Real-IP          $remote_addr;
    proxy_set_header X-Forwarded-For    $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto  $forwarded_proto;
    proxy_set_header X-Forwarded-Host   $host;
    proxy_set_header X-Forwarded-Prefix /api;
    proxy_pass $api_upstream;
}

# Authorization server e MCP do plugin do ChatGPT (HU 005 / feature 007): um único upstream, o
# serviço `mcp` (R8). Sem reescrita de caminho — o host recebe `/auth/...` via `UsePathBase("/auth")`
# e `/mcp` no caminho raiz, então o URI original tem de chegar íntegro.
location /auth/ {
    set $mcp_upstream http://mcp:8080;
    proxy_pass $mcp_upstream;
}

# Transporte HTTP streamable do MCP: respostas em SSE e chamadas longas. Sem `proxy_buffering off`
# o nginx acumula o stream e o cliente não vê o `tools/list` chegar; o timeout alto cobre o
# `initialize` + a primeira listagem. Chamadas do ChatGPT são servidor-a-servidor (sem Origin).
location /mcp {
    set $mcp_upstream http://mcp:8080;
    proxy_buffering off;
    proxy_read_timeout 300s;
    proxy_pass $mcp_upstream;
}

# Descoberta OAuth (RFC 9728 e RFC 8414) — anônima por definição, e o ChatGPT a consulta antes de
# conectar a conta. Os dois prefixos vão para o mesmo upstream; os demais `/.well-known/` seguem 404.
location /.well-known/oauth-protected-resource {
    set $mcp_upstream http://mcp:8080;
    proxy_pass $mcp_upstream;
}

location /.well-known/oauth-authorization-server/ {
    set $mcp_upstream http://mcp:8080;
    proxy_pass $mcp_upstream;
}

location /.well-known/ {
    return 404;
}

# Frontend (HU 002): serviço "frontend" do profile homônimo no docker compose.
location / {
    set $frontend_upstream http://frontend:80;
    proxy_pass $frontend_upstream;
    error_page 502 503 504 = @frontend_ausente;
}

location @frontend_ausente {
    default_type text/plain;
    return 503 "Frontend ainda nao implementado (HU 002). API em /api/swagger\n";
}
