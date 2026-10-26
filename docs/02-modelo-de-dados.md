# Modelo de dados proposto

Começa com o que o +Vendas já tem e já reserva espaço para o foco em clientes. Dialeto: PostgreSQL (portável para SQLite com pequenos ajustes).

## Princípios

- **Raw primeiro:** guardar o payload bruto capturado (`raw_snapshot`) para reprocessar sem raspar de novo.
- **Idempotência:** chave natural composta, nunca só o nº do pedido (ver doc 01, ponto 4).
- **Histórico:** o sistema de origem altera vendas até o fechamento; usar `captured_at` e `source_hash` para detectar mudança.
- **LGPD:** CPF é dado pessoal. Guardar `cpf_hash` (para junção) e `cpf` cifrado, ou só o hash se não precisar do valor. Restringir o acesso do MCP.

## Tabelas — fase 1 (espelho do +Vendas)

```sql
CREATE TABLE salesperson (
  id            SERIAL PRIMARY KEY,
  code          TEXT UNIQUE NOT NULL,        -- 'S482'
  name          TEXT NOT NULL
);

CREATE TABLE sales_period (                    -- competência: 26/set a 25/out
  id              SERIAL PRIMARY KEY,
  salesperson_id  INT REFERENCES salesperson(id),
  starts_on       DATE NOT NULL,
  ends_on         DATE NOT NULL,
  quota           NUMERIC(12,2),              -- cota
  super_quota     NUMERIC(12,2),              -- supercota
  UNIQUE (salesperson_id, starts_on)
);

CREATE TABLE customer (
  id          SERIAL PRIMARY KEY,
  cpf_hash    TEXT UNIQUE,                    -- sha256(cpf + sal)
  cpf         TEXT,                           -- opcional, cifrado
  name        TEXT NOT NULL,
  created_at  TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE sales_order (
  id               SERIAL PRIMARY KEY,
  salesperson_id   INT NOT NULL REFERENCES salesperson(id),
  customer_id      INT REFERENCES customer(id),
  order_number     TEXT NOT NULL,             -- '00004751', 'v1161148crb-01'
  order_date       DATE NOT NULL,             -- data do faturamento (na URL de detalhe)
  kind             TEXT NOT NULL CHECK (kind IN ('sale','return','cancellation')),
  channel          TEXT,                      -- Linx | Online | Vitrine | Box | Vintage
  origin_screen    TEXT,                      -- 'cota' | 'omni'
  status           TEXT,                      -- 'Faturado', 'Cancelado'
  total            NUMERIC(12,2) NOT NULL,    -- negativo em devolução/cancelamento
  original_sale_on DATE,                      -- cancelamento: data da venda original
  cancelled_on     DATE,
  external_id      TEXT,                      -- 4º segmento da URL (45812850), a confirmar
  source_hash      TEXT NOT NULL,
  captured_at      TIMESTAMPTZ NOT NULL,
  UNIQUE (salesperson_id, order_number, order_date, kind)
);

CREATE TABLE order_item (
  id            SERIAL PRIMARY KEY,
  order_id      INT NOT NULL REFERENCES sales_order(id) ON DELETE CASCADE,
  line_no       INT NOT NULL,
  product_name  TEXT,                         -- 'Calça Inez'
  sku_ref       TEXT,                         -- '42CA500UIE_12182'
  size          TEXT,                         -- '38', 'P'
  price         NUMERIC(12,2),
  image_url     TEXT,                         -- URL de origem, só como referência
  UNIQUE (order_id, line_no)
);

-- Fotos ficam por produto (sku_ref), não por item de pedido.
CREATE TABLE product_image (
  id            SERIAL PRIMARY KEY,
  sku_ref       TEXT NOT NULL,                -- liga com order_item.sku_ref
  source_url    TEXT NOT NULL,                -- onde foi baixada
  storage_key   TEXT,                         -- caminho no seu storage (disco/S3/R2)
  sha256        TEXT,                         -- evita duplicata do mesmo arquivo
  mime_type     TEXT,
  width         INT,
  height        INT,
  bytes         INT,
  position      INT DEFAULT 0,                -- 0 = foto principal
  status        TEXT NOT NULL DEFAULT 'pending'
                CHECK (status IN ('pending','downloaded','failed')),
  downloaded_at TIMESTAMPTZ,
  UNIQUE (sku_ref, source_url)
);
CREATE INDEX ix_product_image_sku ON product_image (sku_ref);

CREATE TABLE period_kpi (                      -- foto dos cards, por captura
  id               SERIAL PRIMARY KEY,
  salesperson_id   INT REFERENCES salesperson(id),
  period_id        INT REFERENCES sales_period(id),
  scope            TEXT NOT NULL,             -- 'geral' | 'fisica' | 'online' | 'omni'
  total            NUMERIC(12,2),
  orders_count     INT,                       -- atendimentos / pedidos faturados
  items_count      INT,                       -- peças
  pa               NUMERIC(6,2),
  va               NUMERIC(12,2),
  tma_minutes      INT,                       -- só omni
  cancel_rate      NUMERIC(5,2),              -- só omni
  captured_at      TIMESTAMPTZ NOT NULL
);

CREATE TABLE raw_snapshot (
  id          BIGSERIAL PRIMARY KEY,
  screen      TEXT NOT NULL,
  params      JSONB,
  payload     JSONB NOT NULL,
  captured_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ix_order_date     ON sales_order (order_date);
CREATE INDEX ix_order_customer ON sales_order (customer_id);
CREATE INDEX ix_item_sku       ON order_item (sku_ref);
```

## Tabelas — fase 2 (foco em clientes)

```sql
CREATE TABLE customer_contact (
  id SERIAL PRIMARY KEY, customer_id INT REFERENCES customer(id),
  type TEXT CHECK (type IN ('phone','whatsapp','email','address')),
  value TEXT NOT NULL, is_primary BOOLEAN DEFAULT false
);

CREATE TABLE customer_profile (
  customer_id INT PRIMARY KEY REFERENCES customer(id),
  birthday DATE, preferred_sizes TEXT[], preferred_categories TEXT[],
  style_notes TEXT, consent_marketing BOOLEAN, consent_at TIMESTAMPTZ
);

CREATE TABLE customer_interaction (
  id SERIAL PRIMARY KEY, customer_id INT REFERENCES customer(id),
  occurred_at TIMESTAMPTZ NOT NULL, channel TEXT, summary TEXT, next_action_on DATE
);

CREATE TABLE customer_tag (customer_id INT REFERENCES customer(id), tag TEXT, PRIMARY KEY (customer_id, tag));

CREATE TABLE product (                         -- catálogo derivado dos itens
  sku_ref TEXT PRIMARY KEY, name TEXT, category TEXT, collection TEXT
);
```

## Armazenamento de fotos

- O banco guarda só metadados e o `storage_key`; o arquivo fica fora dele.
- Começar com disco local (`data/images/{sha256[0:2]}/{sha256}.{ext}`) e migrar para S3/R2 se precisar.
- Deduplicar por `sha256`: a mesma peça vendida 10 vezes gera 1 arquivo.
- Opcional: gerar miniatura (ex.: 400 px) para o MCP devolver mais leve.
- Imagens de produto podem ter direitos autorais da marca; usar só para uso interno.

## Views úteis para o MCP

```sql
CREATE VIEW v_customer_summary AS
SELECT c.id, c.name,
       COUNT(*) FILTER (WHERE o.kind='sale')                       AS purchases,
       SUM(o.total)                                                 AS net_total,
       MAX(o.order_date) FILTER (WHERE o.kind='sale')              AS last_purchase,
       CURRENT_DATE - MAX(o.order_date) FILTER (WHERE o.kind='sale') AS days_since_last
FROM customer c LEFT JOIN sales_order o ON o.customer_id = c.id
GROUP BY c.id, c.name;
```

## Regras de negócio a respeitar

- Vendas líquidas = soma de `total` (devoluções e cancelamentos já são negativos).
- PA = peças / atendimentos; VA = valor / atendimentos (conferir: 63 peças / 41 = 1,54 ✔; R$ 123.300,58 / 41 = R$ 3.007,33 ✔).
- Competência fecha no dia 25. Calcular `sales_period` a partir dessa regra, não do mês civil.
- A data na listagem não tem ano (`24 Out`); o ano vem da URL de detalhe ou do período selecionado.
