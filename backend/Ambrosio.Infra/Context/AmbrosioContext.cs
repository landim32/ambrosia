using Ambrosio.Domain.Models;
using Ambrosio.DTO.Enums;
using Ambrosio.Infra.Context.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    /// <summary>Álbum de fotos de cada cliente (feature 009).</summary>
    public DbSet<CustomerPhoto> CustomerPhotos { get; set; }

    public DbSet<Order> Orders { get; set; }

    public DbSet<OrderItem> OrderItems { get; set; }

    public DbSet<StoredImage> StoredImages { get; set; }

    public DbSet<ImportRun> ImportRuns { get; set; }

    public DbSet<ImportRunBlock> ImportRunBlocks { get; set; }

    public DbSet<ImportRunChange> ImportRunChanges { get; set; }

    public DbSet<MaisVendasAccount> MaisVendasAccounts { get; set; }

    // authorization server do plugin do ChatGPT (feature 007, R15): os modelos do OpenIddict com
    // chave long, no MESMO contexto para reaproveitar o migrator e as convenções do Princípio §V.
    public DbSet<OAuthApplication> OAuthApplications { get; set; }

    public DbSet<OAuthAuthorization> OAuthAuthorizations { get; set; }

    public DbSet<OAuthScope> OAuthScopes { get; set; }

    public DbSet<OAuthToken> OAuthTokens { get; set; }

    /// <summary>Trilha de auditoria das gravações do plugin (feature 007, FR-020).</summary>
    public DbSet<McpAuditEntry> McpAuditEntries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureCustomer(modelBuilder);
        ConfigureCustomerPhoto(modelBuilder);
        ConfigureOrder(modelBuilder);
        ConfigureOrderItem(modelBuilder);
        ConfigureStoredImage(modelBuilder);
        ConfigureImportRun(modelBuilder);
        ConfigureImportRunBlock(modelBuilder);
        ConfigureImportRunChange(modelBuilder);
        ConfigureMaisVendasAccount(modelBuilder);
        ConfigureOAuthApplication(modelBuilder);
        ConfigureOAuthAuthorization(modelBuilder);
        ConfigureOAuthScope(modelBuilder);
        ConfigureOAuthToken(modelBuilder);
        ConfigureMcpAuditEntry(modelBuilder);
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

            // Foto de perfil (feature 009). As duas colunas são nulas juntas ou descrevem o perfil:
            // a regra é do serviço (data-model §2), não do banco — não há check constraint aqui
            // porque profile_image_id depende de uma linha de OUTRA tabela.
            entity.Property(e => e.ProfilePhotoId)
                .HasColumnName("profile_photo_id");

            entity.Property(e => e.ProfileImageId)
                .HasColumnName("profile_image_id");

            // São dois FKs diferentes de `customers` para `customer_photos`: este é o da foto de onde
            // o perfil saiu; `CustomerPhoto.Customer` é o do álbum. Sem HasForeignKey explícito o EF
            // criaria uma coluna sombra para um dos dois.
            entity.HasOne(e => e.ProfilePhoto)
                .WithMany()
                .HasForeignKey(e => e.ProfilePhotoId)
                .HasConstraintName("fk_customer_photo_customer")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(e => e.ProfileImage)
                .WithMany()
                .HasForeignKey(e => e.ProfileImageId)
                .HasConstraintName("fk_profile_image_customer")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Índices das FKs em snake_case, e o de baixo serve à checagem de órfão do recorte (R6).
            entity.HasIndex(e => e.ProfilePhotoId)
                .HasDatabaseName("ix_customers_profile_photo");

            entity.HasIndex(e => e.ProfileImageId)
                .HasDatabaseName("ix_customers_profile_image");
        });
    }

    private static void ConfigureCustomerPhoto(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerPhoto>(entity =>
        {
            entity.ToTable("customer_photos");

            entity.HasKey(e => e.Id).HasName("customer_photos_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("customer_photo_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.CustomerId)
                .HasColumnName("customer_id")
                .IsRequired();

            entity.Property(e => e.ImageId)
                .HasColumnName("image_id")
                .IsRequired();

            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(500);

            entity.Property(e => e.IsFavorite)
                .HasColumnName("is_favorite")
                .HasDefaultValue(false)
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
                .WithMany(u => u.CustomerPhotos)
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_user_customer_photo")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(e => e.Customer)
                .WithMany()
                .HasForeignKey(e => e.CustomerId)
                .HasConstraintName("fk_customer_customer_photo")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(e => e.StoredImage)
                .WithMany()
                .HasForeignKey(e => e.ImageId)
                .HasConstraintName("fk_stored_image_customer_photo")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // FKs em snake_case (a convenção do EF geraria "IX_customer_photos_customer_id").
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("ix_customer_photos_user");

            entity.HasIndex(e => e.CustomerId)
                .HasDatabaseName("ix_customer_photos_customer");

            entity.HasIndex(e => e.ImageId)
                .HasDatabaseName("ix_customer_photos_image");

            // FR-006: a mesma imagem não entra duas vezes no álbum da mesma cliente. É índice e não
            // checagem só no serviço porque duas requisições paralelas do mesmo arquivo chegam juntas
            // (research R6) — o serviço trata a DuplicateException resultante relendo a existente.
            entity.HasIndex(e => new { e.CustomerId, e.ImageId })
                .IsUnique()
                .HasDatabaseName("ux_customer_photos_customer_image");

            // Ordem da grade: mais recente primeiro, com o id desempatando o instante (cursor, R8).
            entity.HasIndex(e => new { e.UserId, e.CustomerId, e.CreatedAt, e.Id })
                .IsDescending(false, false, true, true)
                .HasDatabaseName("ix_customer_photos_feed");

            // Filtro "só favoritas" (FR-010): índice parcial, porque a maioria das fotos não é.
            entity.HasIndex(e => new { e.UserId, e.CustomerId, e.CreatedAt, e.Id })
                .IsDescending(false, false, true, true)
                .HasFilter("is_favorite")
                .HasDatabaseName("ix_customer_photos_favorites");
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

            // Origem e tipo do pedido (feature 008, data-model §2). Os padrões (1 = tela, 1 = período)
            // são o que as execuções anteriores à 008 passam a ter: nenhuma tinha plugin nem
            // "Atualizar base", e a migração não precisa preencher nada.
            entity.Property(e => e.StartedVia)
                .HasColumnName("started_via")
                .HasConversion<int>()
                .HasDefaultValue(ImportRunStartedVia.Screen)
                .HasSentinel(ImportRunStartedVia.Screen)
                .IsRequired();

            entity.Property(e => e.Kind)
                .HasColumnName("kind")
                .HasConversion<int>()
                .HasDefaultValue(ImportRunKind.Period)
                .HasSentinel(ImportRunKind.Period)
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

            // Credenciais do +Vendas cifradas (feature 008, data-model §1). `text` e não varchar com
            // tamanho: o texto cifrado cresce com o valor em claro (login ≤ 120, senha ≤ 200) mais
            // nonce/tag/base64, e um teto fixo truncaria a cifra — truncada, ela não decifra nunca.
            // Quem escreve aqui é só o MaisVendasCredentialService, sempre com o par preenchido junto.
            entity.Property(e => e.LoginEncrypted)
                .HasColumnName("login_encrypted")
                .HasColumnType("text");

            entity.Property(e => e.PasswordEncrypted)
                .HasColumnName("password_encrypted")
                .HasColumnType("text");

            entity.Property(e => e.CredentialsSavedAt)
                .HasColumnName("credentials_saved_at")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.PasswordChangedAt)
                .HasColumnName("password_changed_at")
                .HasColumnType("timestamp without time zone");

            // DEFAULT 0 = nenhuma credencial guardada (Princípio V: status como integer com padrão). As
            // transições 0/1/2 estão em data-model §1 e são do modelo, não do banco.
            entity.Property(e => e.CredentialsStatus)
                .HasColumnName("credentials_status")
                .HasConversion<int>()
                .HasDefaultValue(MaisVendasCredentialsStatus.None)
                .HasSentinel(MaisVendasCredentialsStatus.None)
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

    // ---------------------------------------------------------------------
    // authorization server do plugin do ChatGPT (feature 007, data-model §1.1–§1.4, R15).
    //
    // Mapeamento 100% manual, como o resto do contexto: o OpenIddict traz os modelos, mas quem dá o
    // nome de cada coluna é este arquivo. Duas razões, ambas do Princípio §V:
    //
    // 1. Deixar qualquer propriedade ir por convenção produziria coluna PascalCase ("ClientId"),
    //    violando o snake_case — inclusive as propriedades de protocolo que a data-model não listou
    //    uma a uma (ApplicationType, ConsentType, Requirements, Settings, Properties...). Todas
    //    aparecem aqui, sem exceção.
    // 2. As FKs são shadow properties: os modelos do OpenIddict 7.7.2 expõem apenas a navegação
    //    (`Application`, `Authorization`), nunca `ApplicationId`. O nome dado em HasForeignKey vira o
    //    nome da coluna, e é assim que `oauth_application_id` existe sem alterar a classe do SDK.
    //
    // `concurrency_token` é string e leva IsConcurrencyToken(): é o controle otimista de concorrência
    // do OpenIddict, de que `PruneAsync` e a rotação de refresh dependem para não sobrescrever linha
    // alheia.
    // ---------------------------------------------------------------------

    private static void ConfigureOAuthApplication(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthApplication>(entity =>
        {
            entity.ToTable("oauth_applications");

            entity.HasKey(e => e.Id).HasName("oauth_applications_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("oauth_application_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.ClientId)
                .HasColumnName("client_id")
                .HasMaxLength(512);

            entity.Property(e => e.ClientSecret)
                .HasColumnName("client_secret")
                .HasMaxLength(512);

            entity.Property(e => e.ClientType)
                .HasColumnName("client_type")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.ApplicationType)
                .HasColumnName("application_type")
                .HasMaxLength(50);

            entity.Property(e => e.ConsentType)
                .HasColumnName("consent_type")
                .HasMaxLength(50);

            entity.Property(e => e.DisplayName)
                .HasColumnName("display_name")
                .HasMaxLength(200);

            // JSONs de protocolo do OpenIddict: ficam em text porque a dimensão é do fluxo, não nossa.
            entity.Property(e => e.DisplayNames).HasColumnName("display_names");
            entity.Property(e => e.RedirectUris).HasColumnName("redirect_uris");
            entity.Property(e => e.PostLogoutRedirectUris).HasColumnName("post_logout_redirect_uris");
            entity.Property(e => e.Permissions).HasColumnName("permissions");
            entity.Property(e => e.Requirements).HasColumnName("requirements");
            entity.Property(e => e.Settings).HasColumnName("settings");
            entity.Property(e => e.Properties).HasColumnName("properties");
            entity.Property(e => e.JsonWebKeySet).HasColumnName("json_web_key_set");

            entity.Property(e => e.ConcurrencyToken)
                .HasColumnName("concurrency_token")
                .HasMaxLength(50)
                .IsConcurrencyToken();

            // As duas colunas próprias deste feature (data-model §1.1).
            entity.Property(e => e.RegistrationSource)
                .HasColumnName("registration_source")
                .HasDefaultValue(2)
                .IsRequired();

            entity.Property(e => e.MetadataFetchedAt)
                .HasColumnName("metadata_fetched_at")
                .HasColumnType("timestamp without time zone");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            // Cliente por CIMD ou DCR é único pelo client_id; sem índice, a busca de cada requisição
            // de autorização viria varredura de tabela.
            entity.HasIndex(e => e.ClientId)
                .IsUnique()
                .HasDatabaseName("ux_oauth_applications_client_id");
        });
    }

    private static void ConfigureOAuthAuthorization(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthAuthorization>(entity =>
        {
            entity.ToTable("oauth_authorizations");

            entity.HasKey(e => e.Id).HasName("oauth_authorizations_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("oauth_authorization_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.Subject)
                .HasColumnName("subject")
                .HasMaxLength(400);

            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Scopes).HasColumnName("scopes");
            entity.Property(e => e.Properties).HasColumnName("properties");

            entity.Property(e => e.ConcurrencyToken)
                .HasColumnName("concurrency_token")
                .HasMaxLength(50)
                .IsConcurrencyToken();

            entity.Property(e => e.CreationDate)
                .HasColumnName("creation_date")
                .HasColumnType("timestamp without time zone")
                .HasConversion(OAuthUtcConverter);

            // FK shadow + ClientSetNull: excluir um cliente OAuth NÃO apaga os consentimentos da
            // vendedora. O padrão do OpenIddict é cascade; o Princípio §V proíbe, então sobrescrevemos
            // aqui (R15, "a confirmar na implementação" do research).
            entity.HasOne(e => e.Application)
                .WithMany(a => a.Authorizations)
                .HasForeignKey("oauth_application_id")
                .HasConstraintName("fk_oauth_application_oauth_authorization")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Consulta quente: "autorização permanente válida deste usuário para este cliente"
            // (AuthorizationEndpoint, T039).
            entity.HasIndex(e => new { e.Subject, e.Status, e.Type })
                .HasDatabaseName("ix_oauth_authorizations_subject_status_type");

            entity.HasIndex(e => e.CreationDate)
                .HasDatabaseName("ix_oauth_authorizations_creation_date");

            // Índice da FK shadow declarado explicitamente: sem isto o EF cria o seu, pelo nome de
            // convenção `IX_oauth_authorizations_oauth_application_id` (PascalCase), violando o
            // Princípio §V — ver a mesma armadilha comentada em ConfigureCustomer.
            entity.HasIndex("oauth_application_id")
                .HasDatabaseName("ix_oauth_authorizations_application");
        });
    }

    private static void ConfigureOAuthScope(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthScope>(entity =>
        {
            entity.ToTable("oauth_scopes");

            entity.HasKey(e => e.Id).HasName("oauth_scopes_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("oauth_scope_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.DisplayName)
                .HasColumnName("display_name")
                .HasMaxLength(400);

            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(400);

            entity.Property(e => e.DisplayNames).HasColumnName("display_names");
            entity.Property(e => e.Descriptions).HasColumnName("descriptions");
            entity.Property(e => e.Resources).HasColumnName("resources");
            entity.Property(e => e.Properties).HasColumnName("properties");

            entity.Property(e => e.ConcurrencyToken)
                .HasColumnName("concurrency_token")
                .HasMaxLength(50)
                .IsConcurrencyToken();

            entity.HasIndex(e => e.Name)
                .IsUnique()
                .HasDatabaseName("ux_oauth_scopes_name");
        });
    }

    private static void ConfigureOAuthToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthToken>(entity =>
        {
            entity.ToTable("oauth_tokens");

            entity.HasKey(e => e.Id).HasName("oauth_tokens_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("oauth_token_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.Subject)
                .HasColumnName("subject")
                .HasMaxLength(400);

            // 150 como no modelo do OpenIddict 7: o tipo é gravado como identificador URN
            // (urn:openiddict:params:oauth:token-type:...), que não cabe em 50.
            entity.Property(e => e.Type)
                .HasColumnName("type")
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .IsRequired();

            // Hash por referência (códigos e refresh tokens), nunca o valor em claro.
            entity.Property(e => e.ReferenceId)
                .HasColumnName("reference_id")
                .HasMaxLength(100);

            entity.Property(e => e.Payload).HasColumnName("payload");
            entity.Property(e => e.Properties).HasColumnName("properties");

            entity.Property(e => e.ConcurrencyToken)
                .HasColumnName("concurrency_token")
                .HasMaxLength(50)
                .IsConcurrencyToken();

            entity.Property(e => e.CreationDate)
                .HasColumnName("creation_date")
                .HasColumnType("timestamp without time zone")
                .HasConversion(OAuthUtcConverter);

            entity.Property(e => e.ExpirationDate)
                .HasColumnName("expiration_date")
                .HasColumnType("timestamp without time zone")
                .HasConversion(OAuthUtcConverter);

            entity.Property(e => e.RedemptionDate)
                .HasColumnName("redemption_date")
                .HasColumnType("timestamp without time zone")
                .HasConversion(OAuthUtcConverter);

            entity.HasOne(e => e.Application)
                .WithMany(a => a.Tokens)
                .HasForeignKey("oauth_application_id")
                .HasConstraintName("fk_oauth_application_oauth_token")
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(e => e.Authorization)
                .WithMany(a => a.Tokens)
                .HasForeignKey("oauth_authorization_id")
                .HasConstraintName("fk_oauth_authorization_oauth_token")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Duas buscas que precisam de índice dedicado, senão viram varredura numa tabela que
            // cresce a cada login: a rotação de refresh (por reference_id) e o PruneAsync (por
            // expiração).
            entity.HasIndex(e => e.ReferenceId)
                .HasDatabaseName("ix_oauth_tokens_reference_id");

            entity.HasIndex(e => e.ExpirationDate)
                .HasDatabaseName("ix_oauth_tokens_expiration_date");

            entity.HasIndex(e => new { e.Status, e.Type })
                .HasDatabaseName("ix_oauth_tokens_status_type");

            // Índices das FKs shadow declarados explicitamente pelo mesmo motivo de
            // ConfigureOAuthAuthorization: por convenção o EF os nomeia em PascalCase
            // (`IX_oauth_tokens_oauth_application_id`), e o Princípio §V não aceita.
            entity.HasIndex("oauth_application_id")
                .HasDatabaseName("ix_oauth_tokens_application");

            entity.HasIndex("oauth_authorization_id")
                .HasDatabaseName("ix_oauth_tokens_authorization");
        });
    }

    /// <summary>
    /// Datas das tabelas <c>oauth_*</c>: o OpenIddict trabalha com <c>DateTime</c> de
    /// <see cref="DateTimeKind.Utc"/>, e o Npgsql recusa esse <c>Kind</c> em
    /// <c>timestamp without time zone</c>. A convenção do projeto é UTC com <c>Kind</c> não
    /// especificado (<c>SystemClock</c>): o valor é gravado assim e volta marcado como UTC.
    /// </summary>
    private static readonly ValueConverter<DateTime?, DateTime?> OAuthUtcConverter = new(
        value => value.HasValue
            ? DateTime.SpecifyKind(
                value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value,
                DateTimeKind.Unspecified)
            : value,
        value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

    private static void ConfigureMcpAuditEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<McpAuditEntry>(entity =>
        {
            entity.ToTable("mcp_audit_entries");

            entity.HasKey(e => e.Id).HasName("mcp_audit_entries_pkey");

            entity.Property(e => e.Id)
                .HasColumnName("mcp_audit_entry_id")
                .UseIdentityAlwaysColumn();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.ToolName)
                .HasColumnName("tool_name")
                .HasMaxLength(100)
                .IsRequired();

            // Mesmo tamanho do client_id de oauth_applications: a coluna guarda o valor do outro lado
            // da relação, e um client_id CIMD é uma URL https inteira.
            entity.Property(e => e.ClientId)
                .HasColumnName("client_id")
                .HasMaxLength(512);

            // JSON sanitizado (CPF mascarado, sem senha, sem conteúdo de arquivo, sem URL assinada).
            entity.Property(e => e.Arguments)
                .HasColumnName("arguments")
                .IsRequired();

            entity.Property(e => e.Outcome)
                .HasColumnName("outcome")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(e => e.ResultSummary)
                .HasColumnName("result_summary")
                .HasMaxLength(500);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp without time zone")
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_user_mcp_audit_entry")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // A única leitura da tabela é "últimas ações desta vendedora, mais recentes primeiro"
            // (T059) — o índice composto com created_at descendente atende o filtro e a ordem sem
            // ordenar em memória.
            entity.HasIndex(e => new { e.UserId, e.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_mcp_audit_entries_user_created");
        });
    }
}
