# Rotas comuns de homolog e produção (docs/README.md, "Endereços de produção").
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
    proxy_set_header X-Forwarded-Prefix /api;
    proxy_pass $api_upstream;
}

# Authorization server (HU 007) e MCP (HU 006): reservados até os serviços existirem.
location /auth/ {
    return 503 "Authorization server ainda nao implementado (HU 007).\n";
}

location /mcp {
    return 503 "Servidor MCP ainda nao implementado (HU 006).\n";
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
