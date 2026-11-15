using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Clinic.Application.Common.Interfaces;

namespace Clinic.Infrastructure.Payments.Paymob;

/// Paymob Accept (الـ legacy flow): توكن مصادقة، ثم تسجيل أوردر، ثم مفتاح دفع، ثم رابط الـ iframe.
public sealed class PaymobClient : IPaymobClient
{
    private readonly HttpClient _http;
    private readonly PaymobSettings _settings;

    public PaymobClient(HttpClient http, PaymobSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<PaymobCheckout> CreateCheckoutAsync(PaymobCheckoutRequest request, CancellationToken ct = default)
    {
        using var auth = await PostAsync("api/auth/tokens", new { api_key = _settings.ApiKey }, null, ct);
        var authToken = ReadString(auth, "token");

        using var order = await PostAsync(
            "api/ecommerce/orders",
            new
            {
                auth_token = authToken,
                delivery_needed = false,
                amount_cents = request.AmountCents,
                currency = _settings.Currency,
                merchant_order_id = request.MerchantOrderId,
                items = Array.Empty<object>()
            },
            authToken,
            ct);
        var orderId = ReadOrderId(order);

        using var key = await PostAsync(
            "api/acceptance/payment_keys",
            new
            {
                auth_token = authToken,
                amount_cents = request.AmountCents,
                expiration = request.ExpirationSeconds,
                order_id = orderId,
                billing_data = new
                {
                    first_name = request.Billing.FirstName,
                    last_name = request.Billing.LastName,
                    email = request.Billing.Email,
                    phone_number = request.Billing.Phone,
                    apartment = "NA",
                    floor = "NA",
                    street = "NA",
                    building = "NA",
                    shipping_method = "NA",
                    postal_code = "NA",
                    city = "NA",
                    country = "NA",
                    state = "NA"
                },
                currency = _settings.Currency,
                integration_id = _settings.IntegrationId
            },
            authToken,
            ct);
        var paymentToken = ReadString(key, "token");

        var iframeUrl = $"{_settings.BaseUrl.TrimEnd('/')}/api/acceptance/iframes/{_settings.IframeId}"
                        + $"?payment_token={Uri.EscapeDataString(paymentToken)}";

        return new PaymobCheckout(orderId.ToString(), paymentToken, iframeUrl);
    }

    private async Task<JsonDocument> PostAsync(string path, object body, string? bearerToken, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (bearerToken is not null)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymobException($"Could not reach Paymob ({path}).", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var shortText = text.Length > 300 ? text[..300] : text;
                throw new PaymobException($"Paymob request to {path} failed with status {(int)response.StatusCode}: {shortText}");
            }

            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new PaymobException($"Paymob returned an invalid response for {path}.", ex);
            }
        }
    }

    private static string ReadString(JsonDocument doc, string name)
    {
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(element.GetString()))
        {
            return element.GetString()!;
        }

        throw new PaymobException($"Paymob response is missing '{name}'.");
    }

    private static long ReadOrderId(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("id", out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out var id))
        {
            return id;
        }

        throw new PaymobException("Paymob response is missing the order 'id'.");
    }
}
