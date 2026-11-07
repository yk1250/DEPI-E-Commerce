using Clinic.Application.Common.Interfaces;
using Clinic.Infrastructure.Files;
using Clinic.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clinic.Infrastructure;

/// وحدة السجل الطبي (الشخص 3). الشخص 1 بيضيف السطر ده في DependencyInjection.cs عند الدمج:
///     services.AddMedicalModule(configuration);
public static class MedicalModule
{
    public static IServiceCollection AddMedicalModule(this IServiceCollection services, IConfiguration configuration)
    {
        var base64Key = configuration["Encryption:Key"];
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException(
                "Encryption:Key is missing. Generate one with: " +
                "[Convert]::ToBase64String((1..32 | % { Get-Random -Max 256 })) " +
                "then: dotnet user-secrets set \"Encryption:Key\" \"<value>\" --project Clinic.API");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Encryption:Key must be a valid Base64 string.");
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException("Encryption:Key must decode to exactly 32 bytes (AES-256).");
        }

        services.AddSingleton(new EncryptionSettings(key));
        services.AddSingleton<IFieldEncryptor, AesGcmFieldEncryptor>();

        // مكان تخزين المرفقات: من الإعداد Attachments:RootPath، وإلا فولدر برا المشروع بالكامل
        var attachmentsRoot = configuration["Attachments:RootPath"];
        if (string.IsNullOrWhiteSpace(attachmentsRoot))
        {
            attachmentsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClinicApi",
                "attachments");
        }

        services.AddSingleton(new AttachmentStorageSettings(attachmentsRoot));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        return services;
    }
}
