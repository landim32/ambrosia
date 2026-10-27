using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ambrosio.Infra.Context;

/// <summary>
/// Contexto único da solução Ambrosio (5 tabelas: <c>users</c>, <c>customers</c>,
/// <c>orders</c>, <c>order_items</c>, <c>stored_images</c>). Toda a semântica de banco está aqui, em Fluent API,
/// seguindo a constituição §V: snake_case, PK <c>{entidade}_id</c> bigint identity,
/// constraint PK <c>{tabela}_pkey</c>, <c>varchar(n)</c> com MaxLength,
/// <c>timestamp without time zone</c>, enums como <c>integer</c> e
/// <see cref="DeleteBehavior.ClientSetNull"/> em todas as FKs (nunca Cascade).
/// </summary>
public class AmbrosioContext : DbContext
{
    public AmbrosioContext(DbContextOptions<AmbrosioContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }

    public DbSet<Customer> Customers { get; set; }

    public DbSet<Order> Orders { get; set; }

    public DbSet<OrderItem> OrderItems { get; set; }

    public DbSet<StoredImage> StoredImages { get; set; }

    public DbSet<ImportRun> ImportRuns { get; set; }

    public DbSet<ImportRunBlock> ImportRunBlocks { get; set; }

    public DbSet<ImportRunChange> ImportRunChanges { get; set; }

    public DbSet<MaisVendasAccount> MaisVendasAccounts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureCustomer(modelBuilder);
        ConfigureOrder(modelBuilder);
        ConfigureOrderItem(modelBuilder);
        ConfigureStoredImage(modelBuilder);
        ConfigureImportRun(modelBuilder);
        ConfigureImportRunBlock(modelBuilder);
        ConfigureImportRunChange(modelBuilder);
        ConfigureMaisVendasAccount(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("user_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.Email)
                .HasColumnName("email")
                .HasMaxLength(260)
                .IsRequired();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.PasswordHash)
                .HasColumnName("password_hash")
                .HasMaxLength(260);

            entity.Property(e => e.Provider)
                .HasColumnName("provider")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(e => e.ProviderSubject)
                .HasColumnName("provider_subject")
                .HasMaxLength(260);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.HasIndex(e => e.Email)
                .IsUnique()
                .HasDatabaseName("ux_users_email");
        });
    }

    private static void ConfigureCustomer(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers");

            entity.HasKey(e => e.Id).HasName("customers_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("customer_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.CpfHash)
                .HasColumnName("cpf_hash")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(e => e.CpfEncrypted)
                .HasColumnName("cpf_encrypted")
                .HasMaxLength(120)
                .IsRequired();

            entity.Property(e => e.CpfMasked)
                .HasColumnName("cpf_masked")
                .HasMaxLength(14)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.Customers)
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_user_customer")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Índice da FK declarado explicitamente: sem isso o EF gera "IX_customers_user_id"
            // (PascalCase), violando o snake_case da constituição §V.
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("ix_customers_user");

            // O mesmo CPF pode existir para usuários diferentes (US4 cenário 4).
            entity.HasIndex(e => new { e.UserId, e.CpfHash })
                .IsUnique()
                .HasDatabaseName("ux_customers_user_cpf");

            entity.HasIndex(e => new { e.UserId, e.Name })
                .HasDatabaseName("ix_customers_user_name");
        });
    }

    private static void ConfigureOrder(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");

            entity.HasKey(e => e.Id).HasName("orders_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("order_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.CustomerId)
                .HasColumnName("customer_id")
                .IsRequired();

            entity.Property(e => e.OrderNumber)
                .HasColumnName("order_number")
                .HasMaxLength(60)
                .IsRequired();

            entity.Property(e => e.OrderDate)
                .HasColumnName("order_date")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(e => e.Kind)
                .HasColumnName("kind")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(e => e.Channel)
                .HasColumnName("channel")
                .HasMaxLength(60);

            entity.Property(e => e.Origin)
                .HasColumnName("origin")
                .HasConversion<int>();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(60);

            entity.Property(e => e.Total)
                .HasColumnName("total")
                .HasColumnType("numeric(12,2)")
                .IsRequired();

            entity.Property(e => e.OriginalSaleOn)
                .HasColumnName("original_sale_on")
                .HasColumnType("date");

            entity.Property(e => e.CancelledOn)
                .HasColumnName("cancelled_on")
                .HasColumnType("date");

            entity.Property(e => e.ExternalId)
                .HasColumnName("external_id")
                .HasMaxLength(60);

            // Importação do +Vendas (feature 006, data-model §1): origem do pedido. DEFAULT 1 =
            // manual, então as linhas existentes da feature 001 continuam manuais sem backfill, e o
            // ALTER TABLE da migração usa exatamente esse default. A sentinela é Manual — o mesmo valor
            // do default — para que um pedido novo com Source não definido e um pedido com o default
            // explícito produzam o mesmo INSERT (técnica de StoredImage.Status); o CLR default 0 não é
            // um OrderSource válido, então nunca pode significar "omita a coluna".
            entity.Property(e => e.Source)
                .HasColumnName("source")
                .HasConversion<int>()
                .HasDefaultValue(OrderSource.Manual)
                .HasSentinel(OrderSource.Manual)
                .IsRequired();

            // SHA-256 hex do conteúdo vindo da origem (research §6). Nulo em pedido manual e num
            // importado editado à mão — é o que faz a reimportação seguinte reverter a edição.
            entity.Property(e => e.SourceHash)
                .HasColumnName("source_hash")
                .HasMaxLength(64);

            entity.Property(e => e.CapturedAt)
                .HasColumnName("captured_at")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.SourceChangedAt)
                .HasColumnName("source_changed_at")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_user_order")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Sem cascade: excluir um cliente com pedidos falha no banco (edge case da spec).
            entity.HasOne(e => e.Customer)
                .WithMany(c => c.Orders)
                .HasForeignKey(e => e.CustomerId)
                .HasConstraintName("fk_customer_order")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Índices das FKs declarados explicitamente para manter o snake_case da constituição
            // §V (a convenção do EF geraria "IX_orders_user_id"/"IX_orders_customer_id").
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("ix_orders_user");

            entity.HasIndex(e => e.CustomerId)
                .HasDatabaseName("ix_orders_customer");

            // FR-014: mesmo número só se diferenciar pela data ou pelo tipo.
            entity.HasIndex(e => new { e.UserId, e.OrderNumber, e.OrderDate, e.Kind })
                .IsUnique()
                .HasDatabaseName("ux_orders_user_number_date_kind");

            entity.HasIndex(e => new { e.UserId, e.OrderDate })
                .IsDescending(false, true)
                .HasDatabaseName("ix_orders_user_date");

            entity.HasIndex(e => new { e.UserId, e.CustomerId })
                .HasDatabaseName("ix_orders_user_customer");

            // GET /imports/mais-vendas/defaults (research §11) e as chaves do intervalo do bloco
            // (research §17): o padrão de acesso é sempre dono + origem + data mais recente primeiro.
            entity.HasIndex(e => new { e.UserId, e.Source, e.OrderDate })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_orders_user_source_date");
        });
    }

    private static void ConfigureOrderItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("order_items");

            entity.HasKey(e => e.Id).HasName("order_items_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("order_item_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.OrderId)
                .HasColumnName("order_id")
                .IsRequired();

            entity.Property(e => e.LineNo)
                .HasColumnName("line_no")
                .IsRequired();

            entity.Property(e => e.ProductName)
                .HasColumnName("product_name")
                .HasMaxLength(200);

            entity.Property(e => e.SkuRef)
                .HasColumnName("sku_ref")
                .HasMaxLength(60);

            entity.Property(e => e.Size)
                .HasColumnName("size")
                .HasMaxLength(20);

            entity.Property(e => e.Price)
                .HasColumnName("price")
                .HasColumnType("numeric(12,2)");

            entity.Property(e => e.ImageUrl)
                .HasColumnName("image_url")
                .HasMaxLength(500);

            // Foto copiada da origem para o storage da vendedora na importação (feature 006, FR-037).
            // Nula em item de pedido manual e em item cuja foto falhou ou foi recusada (FR-039) — o
            // pedido continua gravado. ImageUrl segue sendo a URL de origem.
            entity.Property(e => e.ImageId)
                .HasColumnName("image_id");

            entity.HasOne(e => e.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(e => e.OrderId)
                .HasConstraintName("fk_order_order_item")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // ClientSetNull (constituição §V, nunca cascata): apagar a imagem do storage desliga o
            // item, sem apagar o pedido nem a linha (a URL de origem continua ali).
            entity.HasOne(e => e.Image)
                .WithMany()
                .HasForeignKey(e => e.ImageId)
                .HasConstraintName("fk_stored_image_order_item")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Índice da FK com nome snake_case (ver nota em ConfigureOrder).
            entity.HasIndex(e => e.OrderId)
                .HasDatabaseName("ix_order_items_order");

            entity.HasIndex(e => new { e.OrderId, e.LineNo })
                .IsUnique()
                .HasDatabaseName("ux_order_items_order_line");

            entity.HasIndex(e => e.SkuRef)
                .HasDatabaseName("ix_order_items_sku");

            // Localização da foto importada (a ligação 1:1 do item com stored_images); a varredura do
            // reuso por URL (FR-040) e o GetLinkedImageIdsAsync passam por aqui.
            entity.HasIndex(e => e.ImageId)
                .HasDatabaseName("ix_order_items_image");
        });
    }

    private static void ConfigureStoredImage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredImage>(entity =>
        {
            entity.ToTable("stored_images");

            entity.HasKey(e => e.Id).HasName("stored_images_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("stored_image_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.StorageKey)
                .HasColumnName("storage_key")
                .HasMaxLength(260)
                .IsRequired();

            entity.Property(e => e.Sha256)
                .HasColumnName("sha256")
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(e => e.MimeType)
                .HasColumnName("mime_type")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.SizeBytes)
                .HasColumnName("size_bytes")
                .IsRequired();

            entity.Property(e => e.Width)
                .HasColumnName("width");

            entity.Property(e => e.Height)
                .HasColumnName("height");

            // Sentinela explícita: quando o valor da propriedade é a sentinela, o EF considera a
            // propriedade "não definida" e deixa o banco decidir. Escolhida Pending — o mesmo valor do
            // default — para que o INSERT do CreatePending e uma inserção sem a coluna deem o mesmo 1,
            // e para o CLR default 0 (que não é um ImageStatus válido) nunca significar "omita". Sem
            // isto o modelo emite o aviso 20601 no startup.
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .HasDefaultValue(ImageStatus.Pending)
                .HasSentinel(ImageStatus.Pending)
                .IsRequired();

            entity.Property(e => e.Source)
                .HasColumnName("source")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.StoredImages)
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_user_stored_image")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Índice da FK em snake_case (a convenção do EF geraria "IX_stored_images_user_id").
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("ix_stored_images_user");

            // FR-011 + clarificação 5: deduplicação por usuário apenas entre imagens ATIVAS. Como o
            // filtro exclui status = 3 (failed), um reenvio do mesmo conteúdo depois de uma recusa
            // cria registro novo — com a MESMA chave de objeto, pois o da tentativa falha já foi
            // apagado (research §8). Um índice único total quebraria isso.
            entity.HasIndex(e => new { e.UserId, e.Sha256 })
                .IsUnique()
                .HasDatabaseName("ux_stored_images_user_sha256_active")
                .HasFilter("status IN (1, 2)");

            // Uma chave de objeto ativa por vez: dois envios simultâneos do mesmo conteúdo (edge
            // case da spec) disputam este índice, e o perdido é relido pelo serviço (research §8).
            entity.HasIndex(e => e.StorageKey)
                .IsUnique()
                .HasDatabaseName("ux_stored_images_storage_key_active")
                .HasFilter("status IN (1, 2)");

            // Varredura do job de limpeza (FR-018): pendentes/falhas mais antigas que o TTL.
            entity.HasIndex(e => new { e.Status, e.CreatedAt })
                .HasDatabaseName("ix_stored_images_status_created_at");
        });
    }

    /// <summary>Nomes das nove contagens (data-model §2), idênticos em <c>import_runs</c> e
    /// <c>import_run_blocks</c>: a mesma lista é aplicada nos dois, para nenhum contador ser esquecido.</summary>
    private static readonly (System.Linq.Expressions.Expression<Func<ImportRun, int>> Run,
        System.Linq.Expressions.Expression<Func<ImportRunBlock, int>> Block,
        string Column)[] CountColumns =
    [
        (r => r.InsertedCount, b => b.InsertedCount, "inserted_count"),
        (r => r.UpdatedCount, b => b.UpdatedCount, "updated_count"),
        (r => r.UnchangedCount, b => b.UnchangedCount, "unchanged_count"),
        (r => r.MissingCount, b => b.MissingCount, "missing_count"),
        (r => r.FailedCount, b => b.FailedCount, "failed_count"),
        (r => r.ConflictCount, b => b.ConflictCount, "conflict_count"),
        (r => r.CustomersCreatedCount, b => b.CustomersCreatedCount, "customers_created_count"),
        (r => r.ImagesStoredCount, b => b.ImagesStoredCount, "images_stored_count"),
        (r => r.ImagesFailedCount, b => b.ImagesFailedCount, "images_failed_count")
    ];

    private static void ConfigureImportRun(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportRun>(entity =>
        {
            entity.ToTable("import_runs");

            entity.HasKey(e => e.Id).HasName("import_runs_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("import_run_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.Mode)
                .HasColumnName("mode")
                .HasConversion<int>()
                .IsRequired();

            // DEFAULT 1 = queued: a linha nasce do POST /runs antes de o worker pegar a vaga (FR-016).
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .HasDefaultValue(ImportRunStatus.Queued)
                .HasSentinel(ImportRunStatus.Queued)
                .IsRequired();

            entity.Property(e => e.StartDate)
                .HasColumnName("start_date")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(e => e.EndDate)
                .HasColumnName("end_date")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(e => e.BlockDays)
                .HasColumnName("block_days")
                .IsRequired();

            entity.Property(e => e.BlocksTotal)
                .HasColumnName("blocks_total")
                .IsRequired();

            entity.Property(e => e.CancelRequested)
                .HasColumnName("cancel_requested")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(e => e.RequestedAt)
                .HasColumnName("requested_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.Property(e => e.StartedAt)
                .HasColumnName("started_at")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.FinishedAt)
                .HasColumnName("finished_at")
                .HasColumnType("timestamp without time zone");

            ConfigureRunCounts(entity);

            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message")
                .HasMaxLength(500);

            entity.Property(e => e.CollectorVersion)
                .HasColumnName("collector_version")
                .HasMaxLength(40)
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_user_import_run")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasMany(e => e.Blocks)
                .WithOne(b => b.ImportRun)
                .HasForeignKey(b => b.ImportRunId)
                .HasConstraintName("fk_import_run_import_run_block")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Índices das FKs em snake_case (a convenção do EF geraria "IX_import_runs_user_id").
            // Não há índice simples por user_id de propósito: em import_runs a coluna já é a chave do
            // índice único parcial ux_import_runs_user_active, e abaixo dele a listagem por
            // requested_at — um terceiro índice sobre a mesma coluna só custa escrita.
            entity.HasIndex(e => new { e.UserId, e.RequestedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_import_runs_user_requested");

            // FR-016/SC-008: UMA execução ativa por vendedora, e o índice é parcial de propósito — só
            // queued(1)/running(2) competem; o histórico concluído pode ter quantas linhas a vendedora
            // quiser. A corrida no INSERT perde com 23505 e o repositório traduz em 409.
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasDatabaseName("ux_import_runs_user_active")
                .HasFilter("status IN (1, 2)");

            // Aviso de queda (FR-051, research §18): a comparação é sempre com a última execução do
            // MESMO período da mesma vendedora.
            entity.HasIndex(e => new { e.UserId, e.StartDate, e.EndDate, e.FinishedAt })
                .IsDescending(false, false, false, true)
                .HasDatabaseName("ix_import_runs_user_period");
        });
    }

    private static void ConfigureImportRunBlock(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportRunBlock>(entity =>
        {
            entity.ToTable("import_run_blocks");

            entity.HasKey(e => e.Id).HasName("import_run_blocks_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("import_run_block_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.ImportRunId)
                .HasColumnName("import_run_id")
                .IsRequired();

            entity.Property(e => e.BlockNo)
                .HasColumnName("block_no")
                .IsRequired();

            entity.Property(e => e.StartDate)
                .HasColumnName("start_date")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(e => e.EndDate)
                .HasColumnName("end_date")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .HasDefaultValue(ImportBlockStatus.Pending)
                .HasSentinel(ImportBlockStatus.Pending)
                .IsRequired();

            ConfigureBlockCounts(entity);

            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message")
                .HasMaxLength(500);

            entity.Property(e => e.StartedAt)
                .HasColumnName("started_at")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.FinishedAt)
                .HasColumnName("finished_at")
                .HasColumnType("timestamp without time zone");

            entity.HasOne(e => e.ImportRun)
                .WithMany(r => r.Blocks)
                .HasForeignKey(e => e.ImportRunId)
                .HasConstraintName("fk_import_run_import_run_block")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasIndex(e => e.ImportRunId)
                .HasDatabaseName("ix_import_run_blocks_run");

            // O número do bloco é a posição no período: repeti-lo criaria um "progresso" impossível de
            // ler e faria a retomada gravar duas vezes a mesma fatia.
            entity.HasIndex(e => new { e.ImportRunId, e.BlockNo })
                .IsUnique()
                .HasDatabaseName("ux_import_run_blocks_run_no");
        });
    }

    private static void ConfigureImportRunChange(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportRunChange>(entity =>
        {
            entity.ToTable("import_run_changes");

            entity.HasKey(e => e.Id).HasName("import_run_changes_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("import_run_change_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.ImportRunId)
                .HasColumnName("import_run_id")
                .IsRequired();

            entity.Property(e => e.ImportRunBlockId)
                .HasColumnName("import_run_block_id")
                .IsRequired();

            // Nulo em simulação (nada foi gravado), em error e em missing sem pedido no banco
            // (contrato imports-api.md): é o que faz o link /pedidos/{id} aparecer só onde tem destino.
            entity.Property(e => e.OrderId)
                .HasColumnName("order_id");

            entity.Property(e => e.OrderNumber)
                .HasColumnName("order_number")
                .HasMaxLength(60)
                .IsRequired();

            entity.Property(e => e.OrderDate)
                .HasColumnName("order_date")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(e => e.Kind)
                .HasColumnName("kind")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(e => e.Channel)
                .HasColumnName("channel")
                .HasMaxLength(60);

            entity.Property(e => e.Origin)
                .HasColumnName("origin")
                .HasConversion<int>();

            entity.Property(e => e.Action)
                .HasColumnName("action")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(e => e.ChangedFields)
                .HasColumnName("changed_fields")
                .HasMaxLength(500);

            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message")
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.HasOne(e => e.ImportRun)
                .WithMany()
                .HasForeignKey(e => e.ImportRunId)
                .HasConstraintName("fk_import_run_import_run_change")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(e => e.ImportRunBlock)
                .WithMany()
                .HasForeignKey(e => e.ImportRunBlockId)
                .HasConstraintName("fk_import_run_block_import_run_change")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .HasConstraintName("fk_order_import_run_change")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasIndex(e => e.ImportRunId)
                .HasDatabaseName("ix_import_run_changes_run");

            entity.HasIndex(e => e.ImportRunBlockId)
                .HasDatabaseName("ix_import_run_changes_block");

            entity.HasIndex(e => e.OrderId)
                .HasDatabaseName("ix_import_run_changes_order");
        });
    }

    private static void ConfigureMaisVendasAccount(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MaisVendasAccount>(entity =>
        {
            entity.ToTable("mais_vendas_accounts");

            entity.HasKey(e => e.Id).HasName("mais_vendas_accounts_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("mais_vendas_account_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            // HMAC-SHA256 com pepper do identificador da conta (research §8). Nulo até o primeiro
            // vínculo em modo gravação. É a única coisa que sobra da conta do +Vendas: nem login, nem
            // senha, nem o identificador em claro existem em alguma coluna (FR-006).
            entity.Property(e => e.AccountHash)
                .HasColumnName("account_hash")
                .HasMaxLength(64);

            entity.Property(e => e.LinkedAt)
                .HasColumnName("linked_at")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.FailedLoginCount)
                .HasColumnName("failed_login_count")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(e => e.LockedUntil)
                .HasColumnName("locked_until")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_user_mais_vendas_account")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Uma linha de vínculo por vendedora (a corrida no GetOrCreateAsync recai aqui). É também o
            // índice que atende a leitura por user_id: não se declara um índice simples duplicado sobre a
            // mesma coluna — o EF os colapsa e restaria apenas a chave única.
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasDatabaseName("ux_mais_vendas_accounts_user");
        });
    }

    private static void ConfigureRunCounts(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ImportRun> entity)
    {
        foreach (var (run, _, column) in CountColumns)
        {
            entity.Property(run)
                .HasColumnName(column)
                .HasDefaultValue(0)
                .IsRequired();
        }
    }

    private static void ConfigureBlockCounts(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<ImportRunBlock> entity)
    {
        foreach (var (_, block, column) in CountColumns)
        {
            entity.Property(block)
                .HasColumnName(column)
                .HasDefaultValue(0)
                .IsRequired();
        }
    }
}
