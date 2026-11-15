using System.Text.RegularExpressions;
using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;

namespace Clinic.Infrastructure.Files;

/// تخزين محلي على القرص. اسم الملف المخزن Guid عشوائي، فمفيش حاجة من المستخدم بتدخل في المسار.
/// المسار المرجّع نسبي بالشكل: 2026/11/<guid>.pdf
public sealed class LocalFileStorage : IFileStorage
{
    private static readonly Regex ExtensionPattern = new(@"^\.[a-z0-9]{1,5}$", RegexOptions.Compiled);

    private readonly string _root;
    private readonly string _rootWithSeparator;

    public LocalFileStorage(AttachmentStorageSettings settings)
    {
        _root = Path.GetFullPath(settings.RootPath);
        _rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(extension) || !ExtensionPattern.IsMatch(extension))
        {
            throw new ArgumentException("Invalid file extension.", nameof(extension));
        }

        var now = DateTime.UtcNow;
        var relativePath = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var fullPath = Resolve(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            await using var file = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(file, ct);
        }
        catch
        {
            // ما نسيبش ملف ناقص على القرص لو النسخ فشل أو اتلغى
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            throw;
        }

        return relativePath;
    }

    public Task<Stream> OpenReadAsync(string storedPath, CancellationToken ct = default)
    {
        var fullPath = Resolve(storedPath);

        if (!File.Exists(fullPath))
        {
            throw new NotFoundException("Attachment file was not found.");
        }

        Stream stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storedPath, CancellationToken ct = default)
    {
        var fullPath = Resolve(storedPath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// بيحوّل المسار النسبي لمسار كامل، وبيرفض أي مسار يطلع برا مكان التخزين (../ أو مسار مطلق).
    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new NotFoundException("Attachment file was not found.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(_rootWithSeparator, comparison))
        {
            throw new NotFoundException("Attachment file was not found.");
        }

        return fullPath;
    }
}
