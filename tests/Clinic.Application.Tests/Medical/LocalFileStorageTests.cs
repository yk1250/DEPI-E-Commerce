using Clinic.Application.Common.Exceptions;
using Clinic.Infrastructure.Files;

namespace Clinic.Application.Tests.Medical;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "clinic-attachments-test-" + Guid.NewGuid().ToString("N"));

    private LocalFileStorage Create() => new(new AttachmentStorageSettings(_root));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Save_then_Open_returns_same_bytes()
    {
        var sut = Create();
        var bytes = "%PDF-1.7 hello"u8.ToArray();

        var path = await sut.SaveAsync(new MemoryStream(bytes), ".pdf");

        await using var stream = await sut.OpenReadAsync(path);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task Stored_path_is_relative_and_uses_a_random_name()
    {
        var sut = Create();

        var path = await sut.SaveAsync(new MemoryStream(new byte[] { 1, 2, 3 }), ".png");

        Assert.Matches(@"^\d{4}/\d{2}/[0-9a-f]{32}\.png$", path);
    }

    [Fact]
    public async Task Two_saves_never_share_a_path()
    {
        var sut = Create();

        var a = await sut.SaveAsync(new MemoryStream(new byte[] { 1 }), ".pdf");
        var b = await sut.SaveAsync(new MemoryStream(new byte[] { 1 }), ".pdf");

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData(".")]
    [InlineData("/../evil")]
    [InlineData(".toolongext")]
    [InlineData("")]
    public async Task Invalid_extension_is_rejected(string extension)
    {
        var sut = Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.SaveAsync(new MemoryStream(new byte[] { 1 }), extension));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("..\\outside.pdf")]
    [InlineData("2026/11/../../../outside.pdf")]
    public async Task Open_rejects_paths_outside_the_storage_root(string path)
    {
        var sut = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => sut.OpenReadAsync(path));
    }

    [Fact]
    public async Task Open_missing_file_throws_not_found()
    {
        var sut = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => sut.OpenReadAsync("2026/11/missing.pdf"));
    }

    [Fact]
    public async Task Delete_removes_the_file_and_is_safe_to_repeat()
    {
        var sut = Create();
        var path = await sut.SaveAsync(new MemoryStream(new byte[] { 1 }), ".pdf");

        await sut.DeleteAsync(path);
        await sut.DeleteAsync(path);

        await Assert.ThrowsAsync<NotFoundException>(() => sut.OpenReadAsync(path));
    }

    [Fact]
    public async Task Failed_copy_leaves_no_partial_file_behind()
    {
        var sut = Create();

        await Assert.ThrowsAsync<IOException>(() => sut.SaveAsync(new ThrowingStream(), ".pdf"));

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("boom");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new IOException("boom");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("boom");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
