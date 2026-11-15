using Clinic.Application.Common.Exceptions;
using Clinic.Application.Features.Attachments;

namespace Clinic.Application.Tests.Medical;

public class AttachmentRulesTests
{
    private static readonly byte[] Pdf = "%PDF-1.7"u8.ToArray();
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
    private static readonly byte[] Exe = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };

    [Fact]
    public void Valid_pdf_is_accepted()
    {
        var result = AttachmentRules.Validate("report.pdf", "application/pdf", 2048, Pdf);

        Assert.Equal("report.pdf", result.SafeFileName);
        Assert.Equal(".pdf", result.Extension);
        Assert.Equal("application/pdf", result.ContentType);
    }

    [Fact]
    public void Valid_png_is_accepted()
    {
        var result = AttachmentRules.Validate("xray.png", "image/png", 2048, Png);

        Assert.Equal(".png", result.Extension);
    }

    [Fact]
    public void Valid_jpeg_is_accepted_with_both_extensions()
    {
        Assert.Equal(".jpg", AttachmentRules.Validate("a.jpg", "image/jpeg", 100, Jpeg).Extension);
        Assert.Equal(".jpeg", AttachmentRules.Validate("a.JPEG", "image/jpeg", 100, Jpeg).Extension);
    }

    [Fact]
    public void Empty_file_is_rejected()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("report.pdf", "application/pdf", 0, Pdf));
    }

    [Fact]
    public void File_over_limit_is_rejected_but_exact_limit_is_accepted()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("report.pdf", "application/pdf", AttachmentRules.MaxSizeBytes + 1, Pdf));

        var ok = AttachmentRules.Validate("report.pdf", "application/pdf", AttachmentRules.MaxSizeBytes, Pdf);
        Assert.Equal(".pdf", ok.Extension);
    }

    [Fact]
    public void Disallowed_extension_is_rejected()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("virus.exe", "application/octet-stream", 100, Exe));
    }

    [Fact]
    public void File_without_extension_is_rejected()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("report", "application/pdf", 100, Pdf));
    }

    [Fact]
    public void Exe_renamed_to_pdf_is_rejected_by_content_check()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("report.pdf", "application/pdf", 100, Exe));
    }

    [Fact]
    public void Png_content_with_pdf_extension_is_rejected()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("report.pdf", "application/pdf", 100, Png));
    }

    [Fact]
    public void Wrong_or_missing_content_type_is_rejected()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("report.pdf", "image/png", 100, Pdf));
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("report.pdf", null, 100, Pdf));
    }

    [Fact]
    public void Content_type_comparison_ignores_case()
    {
        var result = AttachmentRules.Validate("report.pdf", "Application/PDF", 100, Pdf);

        Assert.Equal("application/pdf", result.ContentType);
    }

    [Theory]
    [InlineData("../../evil.pdf")]
    [InlineData("..\\..\\evil.pdf")]
    [InlineData("C:\\Windows\\evil.pdf")]
    public void Path_parts_are_stripped_from_file_name(string name)
    {
        var result = AttachmentRules.Validate(name, "application/pdf", 100, Pdf);

        Assert.Equal("evil.pdf", result.SafeFileName);
    }

    [Fact]
    public void Missing_file_name_is_rejected()
    {
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("", "application/pdf", 100, Pdf));
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate(null, "application/pdf", 100, Pdf));
        Assert.Throws<AppValidationException>(() =>
            AttachmentRules.Validate("..", "application/pdf", 100, Pdf));
    }

    [Fact]
    public void Long_file_name_is_trimmed_to_255_and_keeps_extension()
    {
        var name = new string('a', 300) + ".pdf";

        var result = AttachmentRules.Validate(name, "application/pdf", 100, Pdf);

        Assert.Equal(255, result.SafeFileName.Length);
        Assert.EndsWith(".pdf", result.SafeFileName);
    }
}
