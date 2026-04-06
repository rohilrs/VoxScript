using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoxScript.Core.Notes;
using VoxScript.Core.Persistence;
using Xunit;

namespace VoxScript.Tests.Notes;

public sealed class NoteRepositoryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly NoteRepository _repo;

    public NoteRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new NoteRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_persists_and_returns_record_with_id()
    {
        var note = new NoteRecord { Title = "Test Note", ContentPlainText = "hello" };
        var saved = await _repo.CreateAsync(note, default);
        saved.Id.Should().BeGreaterThan(0);
        saved.Title.Should().Be("Test Note");
    }

    [Fact]
    public async Task GetAllAsync_returns_ordered_by_modified_descending()
    {
        var older = new NoteRecord { Title = "Old", ModifiedAt = DateTime.UtcNow.AddDays(-2) };
        var newer = new NoteRecord { Title = "New", ModifiedAt = DateTime.UtcNow };
        await _repo.CreateAsync(older, default);
        await _repo.CreateAsync(newer, default);

        var all = await _repo.GetAllAsync(default);
        all.Should().HaveCount(2);
        all[0].Title.Should().Be("New");
        all[1].Title.Should().Be("Old");
    }

    [Fact]
    public async Task SearchAsync_finds_by_title_and_content()
    {
        await _repo.CreateAsync(new NoteRecord { Title = "Meeting notes", ContentPlainText = "discuss Q2" }, default);
        await _repo.CreateAsync(new NoteRecord { Title = "Shopping", ContentPlainText = "buy groceries" }, default);

        var byTitle = await _repo.SearchAsync("Meeting", default);
        byTitle.Should().HaveCount(1);
        byTitle[0].Title.Should().Be("Meeting notes");

        var byContent = await _repo.SearchAsync("groceries", default);
        byContent.Should().HaveCount(1);
        byContent[0].Title.Should().Be("Shopping");
    }

    [Fact]
    public async Task UpdateAsync_modifies_fields()
    {
        var note = await _repo.CreateAsync(new NoteRecord { Title = "Draft" }, default);
        note.Title = "Final";
        note.ContentPlainText = "updated content";
        await _repo.UpdateAsync(note, default);

        var fetched = await _repo.GetByIdAsync(note.Id, default);
        fetched!.Title.Should().Be("Final");
        fetched.ContentPlainText.Should().Be("updated content");
    }

    [Fact]
    public async Task DeleteAsync_removes_record()
    {
        var note = await _repo.CreateAsync(new NoteRecord { Title = "Delete me" }, default);
        await _repo.DeleteAsync(note.Id, default);

        var fetched = await _repo.GetByIdAsync(note.Id, default);
        fetched.Should().BeNull();
    }
}
