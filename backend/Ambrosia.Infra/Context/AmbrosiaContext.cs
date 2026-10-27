using Ambrosia.Domain.Models;
using Ambrosia.DTO.Enums;
using Microsoft.EntityFrameworkCore;

namespace Ambrosia.Infra.Context;

/// <summary>
/// Contexto único da solução Ambrosia (5 tabelas: <c>users</c>, <c>customers</c>,
/// <c>orders</c>, <c>order_items</c>, <c>stored_images</c>). Toda a semântica de banco está aqui, em Fluent API,
/// seguindo a constituição §V: snake_case, PK <c>{entidade}_id</c> bigint identity,
/// constraint PK <c>{tabela}_pkey</c>, <c>varchar(n)</c> com MaxLength,
/// <c>timestamp without time zone</c>, enums como <c>integer</c> e
/// <see cref="DeleteBehavior.ClientSetNull"/> em todas as FKs (nunca Cascade).
/// </summary>
public class AmbrosiaContext : DbContext
{
    public AmbrosiaContext(DbContextOptions<AmbrosiaContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }

    public DbSet<Customer> Customers { get; set; }

    public DbSet<Order> Orders { get; set; }

    public DbSet<OrderItem> OrderItems { get; set; }

    public DbSet<StoredImage> StoredImages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureCustomer(modelBuilder);
        ConfigureOrder(modelBuilder);
        ConfigureOrderItem(modelBuilder);
        ConfigureStoredImage(modelBuilder);
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

            entity.HasOne(e => e.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(e => e.OrderId)
                .HasConstraintName("fk_order_order_item")
                .OnDelete(DeleteBehavior.ClientSetNull);

            // Índice da FK com nome snake_case (ver nota em ConfigureOrder).
            entity.HasIndex(e => e.OrderId)
                .HasDatabaseName("ix_order_items_order");

            entity.HasIndex(e => new { e.OrderId, e.LineNo })
                .IsUnique()
                .HasDatabaseName("ux_order_items_order_line");

            entity.HasIndex(e => e.SkuRef)
                .HasDatabaseName("ix_order_items_sku");
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
}
