using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoxScript.Core.History;
using VoxScript.Core.Persistence;
using Xunit;

namespace VoxScript.Tests.Persistence;

public sealed class AppDbContextTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TranscriptionRepository _repo;

    public AppDbContextTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new TranscriptionRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AddAsync_persists_and_returns_record_with_id()
    {
        var record = new TranscriptionRecord { Text = "hello world", DurationSeconds = 1.5 };
        var saved = await _repo.AddAsync(record, default);
        saved.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchAsync_finds_by_text_fragment()
    {
        await _repo.AddAsync(new TranscriptionRecord { Text = "the quick brown fox" }, default);
        await _repo.AddAsync(new TranscriptionRecord { Text = "unrelated content" }, default);

        var results = await _repo.SearchAsync("brown", 10, default);
        results.Should().HaveCount(1);
        results[0].Text.Should().Contain("brown");
    }

    [Fact]
    public async Task DeleteOlderThanAsync_removes_old_records()
    {
        var old = new TranscriptionRecord
        {
            Text = "old",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-31)
        };
        var recent = new TranscriptionRecord { Text = "recent" };
        await _repo.AddAsync(old, default);
        await _repo.AddAsync(recent, default);

        await _repo.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-7), default);

        var count = await _repo.CountAsync(default);
        count.Should().Be(1);
    }
}
