using Ecommerce.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Comprador> Compradores => Set<Comprador>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Comprador>(entity =>
        {
            entity.HasKey(comprador => comprador.Id);

            entity.Property(comprador => comprador.Nome)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(comprador => comprador.Email)
                .HasMaxLength(200)
                .IsRequired();

            entity.HasIndex(comprador => comprador.Email)
                .IsUnique();

            entity.HasMany(comprador => comprador.Pedidos)
                .WithOne(pedido => pedido.Comprador)
                .HasForeignKey(pedido => pedido.CompradorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Produto>(entity =>
        {
            entity.HasKey(produto => produto.Id);

            entity.Property(produto => produto.Nome)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(produto => produto.Preco)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            entity.HasMany(produto => produto.ItensPedido)
                .WithOne(item => item.Produto)
                .HasForeignKey(item => item.ProdutoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Pedido>(entity =>
        {
            entity.HasKey(pedido => pedido.Id);

            entity.Property(pedido => pedido.Status)
                .IsRequired();

            entity.HasMany(pedido => pedido.Itens)
                .WithOne(item => item.Pedido)
                .HasForeignKey(item => item.PedidoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemPedido>(entity =>
        {
            entity.HasKey(item => item.Id);

            entity.Property(item => item.Quantidade)
                .IsRequired();

            entity.Property(item => item.PrecoUnitario)
                .HasColumnType("decimal(18,2)")
                .IsRequired();
        });

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(usuario => usuario.Id);

            entity.Property(usuario => usuario.Nome)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(usuario => usuario.Email)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(usuario => usuario.SenhaHash)
                .HasMaxLength(500)
                .IsRequired();

            entity.HasIndex(usuario => usuario.Email)
                .IsUnique();
        });
    }
}
