using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clinic.Application.Features.Payments;

/// توقيع إشعار Paymob (Transaction Processed): HMAC-SHA512 على 20 حقل بترتيب ثابت، من غير فواصل.
/// القيم: النص زي ما هو، الأرقام زي ما جت في الـ JSON، والمنطقي true/false بحروف صغيرة، والناقص أو null فاضي.
/// الناتج hex بحروف صغيرة، ويتقارن بقيمة ?hmac= في الرابط.
public static class PaymobHmac
{
    private static readonly string[][] FieldPaths =
    {
        new[] { "amount_cents" },
        new[] { "created_at" },
        new[] { "currency" },
        new[] { "error_occured" },
        new[] { "has_parent_transaction" },
        new[] { "id" },
        new[] { "integration_id" },
        new[] { "is_3d_secure" },
        new[] { "is_auth" },
        new[] { "is_capture" },
        new[] { "is_refunded" },
        new[] { "is_standalone_payment" },
        new[] { "is_voided" },
        new[] { "order", "id" },
        new[] { "owner" },
        new[] { "pending" },
        new[] { "source_data", "pan" },
        new[] { "source_data", "sub_type" },
        new[] { "source_data", "type" },
        new[] { "success" }
    };

    /// النص المجمّع اللي بيتوقّع عليه (للاختبار والتشخيص).
    public static string BuildPayload(JsonElement transaction)
    {
        var builder = new StringBuilder();
        foreach (var path in FieldPaths)
        {
            builder.Append(ValueAt(transaction, path));
        }

        return builder.ToString();
    }

    public static string Compute(JsonElement transaction, string secret)
    {
        var hash = HMACSHA512.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(BuildPayload(transaction)));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// مقارنة ثابتة الزمن، ومش حساسة لحالة حروف الـ hex.
    public static bool IsValid(JsonElement transaction, string secret, string? provided)
    {
        if (string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var expected = Compute(transaction, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(provided.Trim().ToLowerInvariant()));
    }

    private static string ValueAt(JsonElement root, string[] path)
    {
        var current = root;

        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return string.Empty;
            }

            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }
}
