-- Carga para SC-003 (T095): ~2.000 clientes e ~10.000 pedidos com 1-3 itens para UM usuário.
--
-- Uso (usuário já existente, ex.: criado por /auth/register):
--   psql -U postgres -d ambrosia_dev -v user_id=1 -f backend/scripts/seed-load-test.sql
--
-- Os dados são fictícios e marcados: clientes "Carga Teste NNNN" e pedidos "LT-NNNNN".
-- O script é idempotente: remove a carga anterior do mesmo usuário antes de inserir.
-- Enums: kind 1=sale 2=return 3=cancellation; origin 1=cota 2=omni.

\set ON_ERROR_STOP on

BEGIN;

DELETE FROM order_items WHERE order_id IN
    (SELECT order_id FROM orders WHERE user_id = :user_id AND order_number LIKE 'LT-%');
DELETE FROM orders WHERE user_id = :user_id AND order_number LIKE 'LT-%';
DELETE FROM customers WHERE user_id = :user_id AND name LIKE 'Carga Teste %';

INSERT INTO customers (user_id, name, cpf_hash, cpf_encrypted, cpf_masked, created_at, updated_at)
SELECT :user_id,
       'Carga Teste ' || lpad(n::text, 4, '0') || CASE WHEN n % 3 = 0 THEN ' Ana' ELSE ' Maria' END,
       md5('lt-hash-' || :user_id || '-' || n) || md5('lt-hash2-' || :user_id || '-' || n),
       'ZmljdGljaW8tY2lmcmEtbm8tcmVhbA==',
       '***.' || lpad((n % 1000)::text, 3, '0') || '.' || lpad(((n * 7) % 1000)::text, 3, '0') || '-**',
       now() AT TIME ZONE 'utc',
       now() AT TIME ZONE 'utc'
FROM generate_series(1, 2000) AS n;

-- Pedidos: 80% venda, 10% devolução, 10% cancelamento; sinal do total respeita o tipo (FR-013).
INSERT INTO orders (user_id, customer_id, order_number, order_date, kind, channel, origin, status,
                    total, created_at, updated_at)
SELECT :user_id,
       c.customer_id,
       'LT-' || lpad(g.n::text, 5, '0'),
       (current_date - (g.n % 360))::date,
       k.kind,
       (ARRAY['Linx', 'Online', 'Vitrine', 'WhatsApp'])[1 + g.n % 4],
       1 + g.n % 2,
       CASE k.kind WHEN 1 THEN 'Faturado' WHEN 2 THEN 'Devolvido' ELSE 'Cancelado' END,
       CASE k.kind WHEN 1 THEN 1 ELSE -1 END * (50 + (g.n * 13) % 950)::numeric(12,2),
       now() AT TIME ZONE 'utc',
       now() AT TIME ZONE 'utc'
FROM generate_series(1, 10000) AS g(n)
CROSS JOIN LATERAL (SELECT CASE WHEN g.n % 10 = 0 THEN 2 WHEN g.n % 10 = 5 THEN 3 ELSE 1 END AS kind) AS k
JOIN LATERAL (
    SELECT customer_id FROM customers
    WHERE user_id = :user_id AND name LIKE 'Carga Teste %'
    ORDER BY customer_id OFFSET (g.n % 2000) LIMIT 1
) AS c ON true;

-- 1 a 3 itens por pedido, numerados a partir de 1.
INSERT INTO order_items (order_id, line_no, product_name, sku_ref, size, price)
SELECT o.order_id,
       l.line_no,
       'Produto ' || (o.order_id % 500),
       'SKU' || lpad(((o.order_id * l.line_no) % 900)::text, 4, '0'),
       (ARRAY['P', 'M', 'G', '38', '40'])[1 + (o.order_id % 5)::int],
       CASE WHEN o.kind = 1 THEN 1 ELSE -1 END * (20 + (o.order_id * l.line_no) % 300)::numeric(12,2)
FROM orders o
CROSS JOIN LATERAL generate_series(1, 1 + (o.order_id % 3)::int) AS l(line_no)
WHERE o.user_id = :user_id AND o.order_number LIKE 'LT-%';

COMMIT;

ANALYZE customers;
ANALYZE orders;
ANALYZE order_items;

\echo === contagens ===
SELECT (SELECT count(*) FROM customers WHERE user_id = :user_id) AS clientes,
       (SELECT count(*) FROM orders WHERE user_id = :user_id) AS pedidos,
       (SELECT count(*) FROM order_items i JOIN orders o USING (order_id) WHERE o.user_id = :user_id) AS itens;

\echo === GET /orders?page=1&pageSize=20 ===
EXPLAIN (ANALYZE, BUFFERS)
SELECT o.order_id, o.order_date, o.total
FROM orders o
WHERE o.user_id = :user_id
ORDER BY o.order_date DESC, o.order_id DESC
LIMIT 20;

\echo === GET /orders (filtros: from/to + kind + channel) ===
EXPLAIN (ANALYZE, BUFFERS)
SELECT o.order_id
FROM orders o
WHERE o.user_id = :user_id
  AND o.order_date >= current_date - 90 AND o.order_date <= current_date
  AND o.kind = 1 AND lower(o.channel) = 'linx'
ORDER BY o.order_date DESC
LIMIT 20;

\echo === GET /customers?search=a ===
EXPLAIN (ANALYZE, BUFFERS)
SELECT c.customer_id, c.name
FROM customers c
WHERE c.user_id = :user_id AND lower(c.name) LIKE '%a%'
ORDER BY c.name
LIMIT 20;
