using Clinic.Application.Common.Exceptions;
using Clinic.Application.Common.Interfaces;
using Clinic.Application.Common.Security;
using Clinic.Application.Features.Attachments;
using Clinic.Application.Features.PatientAccess;
using Clinic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Clinic.Application.Tests.Medical;

public class AttachmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PdfBytes = "%PDF-1.7 lab results"u8.ToArray();
    private static readonly byte[] ExeBytes = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04 };

    /// تخزين في الذاكرة بدل القرص.
    private sealed class FakeFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public bool FailOnSave { get; set; }

        public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
        {
            if (FailOnSave)
            {
                throw new IOException("disk full");
            }

            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            var path = $"fake/{Guid.NewGuid():N}{extension}";
            Files[path] = copy.ToArray();
            return path;
        }

        public Task<Stream> OpenReadAsync(string storedPath, CancellationToken ct = default)
        {
            if (!Files.TryGetValue(storedPath, out var bytes))
            {
                throw new NotFoundException("Attachment file was not found.");
            }

            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public Task DeleteAsync(string storedPath, CancellationToken ct = default)
        {
            Files.Remove(storedPath);
            return Task.CompletedTask;
        }
    }

    private static AttachmentService Service(MedicalTestEnv env, FakeFileStorage storage, ICurrentUser user) =>
        new(env.Db, storage, new PatientAccessGuard(env.Db, user), user, new TestClock(Now));

    private static TestCurrentUser PatientA(MedicalTestEnv env) =>
        new() { Role = Roles.Patient, PatientId = env.PatientA, UserId = env.PatientAUserId };

    private static TestCurrentUser PatientB(MedicalTestEnv env) =>
        new() { Role = Roles.Patient, PatientId = env.PatientB, UserId = env.PatientBUserId };

    private static TestCurrentUser Doctor(MedicalTestEnv env) =>
        new() { Role = Roles.Doctor, DoctorId = env.Doctor1 };

    private static Task<AttachmentDto> UploadPdf(AttachmentService sut, int appointmentId, string name = "lab.pdf") =>
        sut.UploadAsync(appointmentId, name, "application/pdf", new MemoryStream(PdfBytes));

    // ---------- الرفع ----------

    [Fact]
    public async Task Patient_uploads_a_pdf_to_own_appointment()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var storage = new FakeFileStorage();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        var dto = await UploadPdf(Service(env, storage, PatientA(env)), appointmentId);

        Assert.Equal(appointmentId, dto.AppointmentId);
        Assert.Equal("lab.pdf", dto.FileName);
        Assert.Equal("application/pdf", dto.ContentType);
        Assert.Equal(PdfBytes.Length, dto.SizeBytes);
        Assert.Equal(Now.UtcDateTime, dto.UploadedAt);

        var row = await env.Db.Attachments.AsNoTracking().SingleAsync();
        Assert.Equal(env.PatientA, row.PatientId);
        Assert.Equal(env.PatientAUserId, row.UploadedByUserId);
        Assert.Equal(appointmentId, row.AppointmentId);
        Assert.EndsWith(".pdf", row.StoredPath);
        Assert.Equal(PdfBytes, storage.Files[row.StoredPath]);
    }

    [Theory]
    [InlineData(AppointmentStatus.PendingPayment)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Upload_is_allowed_for_active_appointments(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);

        var dto = await UploadPdf(Service(env, new FakeFileStorage(), PatientA(env)), appointmentId);

        Assert.True(dto.Id > 0);
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Expired)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Upload_is_rejected_for_cancelled_expired_or_missed_appointments(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var storage = new FakeFileStorage();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);

        await Assert.ThrowsAsync<ConflictException>(() =>
            UploadPdf(Service(env, storage, PatientA(env)), appointmentId));

        Assert.Empty(storage.Files);
        Assert.Equal(0, await env.Db.Attachments.CountAsync());
    }

    [Fact]
    public async Task Upload_to_another_patients_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var storage = new FakeFileStorage();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientB, AppointmentStatus.Confirmed);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            UploadPdf(Service(env, storage, PatientA(env)), appointmentId));

        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task Upload_to_a_missing_appointment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            UploadPdf(Service(env, new FakeFileStorage(), PatientA(env)), 9999));
    }

    [Fact]
    public async Task Exe_renamed_to_pdf_is_rejected_and_nothing_is_stored()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var storage = new FakeFileStorage();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var sut = Service(env, storage, PatientA(env));

        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.UploadAsync(appointmentId, "report.pdf", "application/pdf", new MemoryStream(ExeBytes)));

        Assert.Empty(storage.Files);
        Assert.Equal(0, await env.Db.Attachments.CountAsync());
    }

    [Fact]
    public async Task Empty_file_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var sut = Service(env, new FakeFileStorage(), PatientA(env));

        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.UploadAsync(appointmentId, "empty.pdf", "application/pdf", new MemoryStream()));
    }

    [Fact]
    public async Task Eleventh_attachment_on_one_appointment_is_rejected()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var storage = new FakeFileStorage();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var sut = Service(env, storage, PatientA(env));

        for (var i = 0; i < AttachmentService.MaxPerAppointment; i++)
        {
            await UploadPdf(sut, appointmentId, $"file{i}.pdf");
        }

        await Assert.ThrowsAsync<ConflictException>(() => UploadPdf(sut, appointmentId, "one-too-many.pdf"));
        Assert.Equal(AttachmentService.MaxPerAppointment, storage.Files.Count);
    }

    [Fact]
    public async Task Failed_storage_leaves_no_database_row()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var storage = new FakeFileStorage { FailOnSave = true };
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);

        await Assert.ThrowsAsync<IOException>(() =>
            UploadPdf(Service(env, storage, PatientA(env)), appointmentId));

        Assert.Equal(0, await env.Db.Attachments.CountAsync());
    }

    [Theory]
    [InlineData(Roles.Doctor)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Receptionist)]
    public async Task Only_patients_can_upload(string role)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var user = new TestCurrentUser { Role = role, DoctorId = env.Doctor1, PatientId = env.PatientA };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            UploadPdf(Service(env, new FakeFileStorage(), user), appointmentId));
    }

    [Fact]
    public async Task Unauthenticated_upload_gets_401()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var user = new TestCurrentUser { IsAuthenticated = false };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            UploadPdf(Service(env, new FakeFileStorage(), user), appointmentId));
    }

    // ---------- التحميل ----------

    private static async Task<(AttachmentDto Dto, FakeFileStorage Storage)> UploadAsOwnerAsync(MedicalTestEnv env, int appointmentId)
    {
        var storage = new FakeFileStorage();
        var dto = await UploadPdf(Service(env, storage, PatientA(env)), appointmentId, "lab.pdf");
        return (dto, storage);
    }

    [Fact]
    public async Task Owner_downloads_the_file()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var (dto, storage) = await UploadAsOwnerAsync(env, appointmentId);

        var file = await Service(env, storage, PatientA(env)).DownloadAsync(dto.Id);

        await using var stream = file.Content;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(PdfBytes, copy.ToArray());
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("lab.pdf", file.FileName);
    }

    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.CheckedIn)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Doctor_with_an_active_appointment_downloads_the_file(AppointmentStatus status)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, status);
        var (dto, storage) = await UploadAsOwnerAsync(env, appointmentId);

        var file = await Service(env, storage, Doctor(env)).DownloadAsync(dto.Id);

        await using var stream = file.Content;
        Assert.Equal("lab.pdf", file.FileName);
    }

    [Fact]
    public async Task Doctor_loses_access_when_the_appointment_is_cancelled()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var (dto, storage) = await UploadAsOwnerAsync(env, appointmentId);

        // الموعد اتلغى: الدكتور مبقاش له موعد فعلي مع المريض
        var appointment = await env.Db.Appointments.SingleAsync(a => a.Id == appointmentId);
        appointment.Status = AppointmentStatus.Cancelled;
        await env.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, storage, Doctor(env)).DownloadAsync(dto.Id));
    }

    [Fact]
    public async Task Another_patient_cannot_download()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var (dto, storage) = await UploadAsOwnerAsync(env, appointmentId);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, storage, PatientB(env)).DownloadAsync(dto.Id));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Receptionist)]
    public async Task Admin_and_receptionist_cannot_download(string role)
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var (dto, storage) = await UploadAsOwnerAsync(env, appointmentId);
        var user = new TestCurrentUser { Role = role, PatientId = env.PatientA, DoctorId = env.Doctor1 };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Service(env, storage, user).DownloadAsync(dto.Id));
    }

    [Fact]
    public async Task Unknown_attachment_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(env, new FakeFileStorage(), PatientA(env)).DownloadAsync(9999));
    }

    [Fact]
    public async Task Missing_file_on_disk_is_not_found()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var (dto, _) = await UploadAsOwnerAsync(env, appointmentId);

        // تخزين فاضي: الملف مش موجود (زي صف الـ seed)
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(env, new FakeFileStorage(), PatientA(env)).DownloadAsync(dto.Id));
    }

    [Fact]
    public async Task Unauthenticated_download_gets_401()
    {
        using var env = await MedicalTestEnv.CreateAsync();
        var appointmentId = await env.AddAppointmentAsync(env.Doctor1, env.PatientA, AppointmentStatus.Confirmed);
        var (dto, storage) = await UploadAsOwnerAsync(env, appointmentId);
        var user = new TestCurrentUser { IsAuthenticated = false };

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service(env, storage, user).DownloadAsync(dto.Id));
    }
}
