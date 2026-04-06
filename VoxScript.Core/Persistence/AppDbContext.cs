using Microsoft.EntityFrameworkCore;

namespace VoxScript.Core.Persistence;

public sealed class AppDbContext : DbContext
{
    public DbSet<TranscriptionRecord> Transcriptions => Set<TranscriptionRecord>();
    public DbSet<VocabularyWordRecord> VocabularyWords => Set<VocabularyWordRecord>();
    public DbSet<WordReplacementRecord> WordReplacements => Set<WordReplacementRecord>();
    public DbSet<CorrectionRecord> Corrections => Set<CorrectionRecord>();
    public DbSet<PowerModeConfigRecord> PowerModeConfigs => Set<PowerModeConfigRecord>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TranscriptionRecord>()
            .Property(e => e.CreatedAt)
            .HasConversion(
                v => v.ToUnixTimeMilliseconds(),
                v => DateTimeOffset.FromUnixTimeMilliseconds(v));
    }
}
