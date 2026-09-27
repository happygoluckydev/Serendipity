using Microsoft.EntityFrameworkCore;
using Serendipity.Api.Domain;

namespace Serendipity.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Mark> Marks => Set<Mark>();
    public DbSet<Adjacency> Adjacencies => Set<Adjacency>();
    public DbSet<Pop> Pops => Set<Pop>();
    public DbSet<Preview> Previews => Set<Preview>();
    public DbSet<Engagement> Engagements => Set<Engagement>();
    public DbSet<Save> Saves => Set<Save>();
    public DbSet<Shop> Shops => Set<Shop>();
    public DbSet<ShopItem> ShopItems => Set<ShopItem>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<DomainRule> DomainRules => Set<DomainRule>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // 列挙型は文字列で保存し、DBを直接見ても読めるようにする。
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(40);
        });

        b.Entity<Device>(e =>
        {
            e.HasKey(x => x.KeyHash);
            e.Property(x => x.KeyHash).HasMaxLength(64);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<Mark>(e =>
        {
            e.Property(x => x.LinkUrl).HasMaxLength(2048);
            e.Property(x => x.Domain).HasMaxLength(255);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => new { x.SeedX, x.SeedY });
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId);
            e.HasMany(x => x.Pops).WithOne(x => x.Mark).HasForeignKey(x => x.MarkId);
            e.HasOne(x => x.Preview).WithOne().HasForeignKey<Preview>(x => x.MarkId);
        });

        b.Entity<Adjacency>(e =>
        {
            e.HasIndex(x => x.MarkAId);
            e.HasIndex(x => x.MarkBId);
            e.HasIndex(x => new { x.MarkAId, x.MarkBId }).IsUnique();
            e.HasOne<Mark>().WithMany().HasForeignKey(x => x.MarkAId);
            e.HasOne<Mark>().WithMany().HasForeignKey(x => x.MarkBId);
        });

        b.Entity<Pop>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(200);
            e.Property(x => x.PhotoKey).HasMaxLength(200);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorUserId);
            e.HasIndex(x => x.Status);
        });

        b.Entity<Preview>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.SiteName).HasMaxLength(200);
            e.Property(x => x.ImageKey).HasMaxLength(200);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.FetchError).HasMaxLength(500);
        });

        b.Entity<Engagement>(e =>
        {
            e.Property(x => x.ActorKey).HasMaxLength(64);
            e.HasIndex(x => new { x.MarkId, x.CreatedAt });
            e.HasIndex(x => new { x.ActorKey, x.MarkId, x.Kind, x.CreatedAt });
            e.HasOne<Mark>().WithMany().HasForeignKey(x => x.MarkId);
        });

        b.Entity<Save>(e =>
        {
            e.Property(x => x.DeviceKeyHash).HasMaxLength(64);
            e.Property(x => x.SnapshotJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.DeviceKeyHash, x.MarkId }).IsUnique().HasFilter("\"DeviceKeyHash\" IS NOT NULL");
            e.HasIndex(x => new { x.UserId, x.MarkId }).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
        });

        b.Entity<Shop>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(40);
            e.HasIndex(x => x.OwnerUserId).IsUnique();
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ShopId);
        });

        b.Entity<ShopItem>(e =>
        {
            e.HasIndex(x => new { x.ShopId, x.SaveId }).IsUnique();
            e.HasOne(x => x.Save).WithMany().HasForeignKey(x => x.SaveId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Report>(e =>
        {
            e.Property(x => x.ReporterKey).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Contact).HasMaxLength(200);
            e.HasIndex(x => x.MarkId);
            e.HasIndex(x => x.Status);
        });

        b.Entity<DomainRule>(e =>
        {
            e.HasKey(x => x.Domain);
            e.Property(x => x.Domain).HasMaxLength(255);
            e.Property(x => x.Note).HasMaxLength(200);
        });
    }
}
