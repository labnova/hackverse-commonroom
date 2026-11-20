using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Commonroom.ReferenceApi.Data;

public sealed class Conversation
{
    public Guid Id { get; set; }
    public string OwnerSub { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int LastReadStep { get; set; }
    public bool Deleted { get; set; }
}

public sealed class Message
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public int Step { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public sealed class Feedback
{
    public Guid ConversationId { get; set; }
    public int Step { get; set; }
    public string Rating { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ChatDb(DbContextOptions<ChatDb> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Feedback> Feedback => Set<Feedback>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OwnerSub).IsRequired().HasMaxLength(128);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.MetadataJson).IsRequired();
            e.HasIndex(x => new { x.OwnerSub, x.Deleted, x.UpdatedAt });
        });

        b.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).IsRequired().HasMaxLength(16);
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.CorrelationId).IsRequired().HasMaxLength(128);
            e.Property(x => x.SessionId).IsRequired().HasMaxLength(128);
            e.HasIndex(x => new { x.ConversationId, x.Step }).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Feedback>(e =>
        {
            e.HasKey(x => new { x.ConversationId, x.Step });
            e.Property(x => x.Rating).IsRequired().HasMaxLength(8);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Used by `dotnet ef` only.</summary>
public sealed class ChatDbDesignTimeFactory : IDesignTimeDbContextFactory<ChatDb>
{
    public ChatDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ChatDb>().UseSqlite("Data Source=commonroom-ref.db").Options);
}
