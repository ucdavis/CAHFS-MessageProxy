using MessageProxyApi.Models;
using Microsoft.EntityFrameworkCore;

namespace MessageProxyApi.Data
{
    public class ProxyDbContext : DbContext
    {
        public ProxyDbContext(DbContextOptions<ProxyDbContext> options)
            : base(options)
        {
        }

        public DbSet<CProxyMessage> CProxyMessages { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CProxyMessage>(entity =>
            {
                entity.ToTable("C_Proxy_Message");

                entity.HasKey(e => e.MessageId);

                entity.Property(e => e.MessageId)
                    .HasColumnName("MessageId");

                entity.Property(e => e.MessageContent)
                    .HasColumnName("MessageContent")
                    .HasColumnType("text");

                entity.Property(e => e.Received)
                    .HasColumnName("Received")
                    .HasColumnType("datetime");

                entity.Property(e => e.ErrorMessage)
                    .HasColumnName("ErrorMessage")
                    .HasColumnType("varchar(max)");

                entity.Property(e => e.ResponseStatus)
                    .HasColumnName("ResponseStatus")
                    .HasMaxLength(50)
                    .HasColumnType("varchar(50)");

                entity.Property(e => e.ResponseContent)
                    .HasColumnName("ResponseContent")
                    .HasColumnType("text");
            });
        }
    }
}
