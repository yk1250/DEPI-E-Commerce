using System.Net;
using System.Text.Json;
using Clinic.Application.Common.Interfaces;
using Clinic.Infrastructure.Payments.Paymob;

namespace Clinic.Application.Tests.Payments;

public class PaymobClientTests
{
    private static readonly PaymobSettings Settings =
        new("test-api-key", "test-hmac", 4242, 555, "https://accept.test", "EGP");

    private static readonly PaymobCheckoutRequest Request = new(
        27000,
        "A5-abc",
        900,
        new PaymobBilling("Patient", "P2", "patient2@clinic.local", "NA"));

    private sealed record Call(string Path, string Body, string? BearerToken);

    /// بيسجّل كل طلب، وبيرد بالرد المناسب للمسار.
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode Status, string Json)> _respond;

        public FakeHandler(Func<string, (HttpStatusCode, string)> respond)
        {
            _respond = respond;
        }

        public List<Call> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Calls.Add(new Call(path, body, request.Headers.Authorization?.Parameter));

            var (status, json) = _respond(path);
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network down");
    }

    private static (HttpStatusCode, string) HappyResponses(string path) => path switch
    {
        "api/auth/tokens" => (HttpStatusCode.Created, """{"token":"AUTH123","profile":{"id":1}}"""),
        "api/ecommerce/orders" => (HttpStatusCode.Created, """{"id":987654321,"amount_cents":27000}"""),
        "api/acceptance/payment_keys" => (HttpStatusCode.Created, """{"token":"PAYKEY"}"""),
        _ => (HttpStatusCode.NotFound, "{}")
    };

    private static PaymobClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://accept.test/") }, Settings);

    private static JsonElement Body(Call call) => JsonDocument.Parse(call.Body).RootElement;

    [Fact]
    public async Task Creates_a_checkout_with_three_calls_in_order()
    {
        var handler = new FakeHandler(HappyResponses);

        var result = await Client(handler).CreateCheckoutAsync(Request);

        Assert.Equal(
            new[] { "api/auth/tokens", "api/ecommerce/orders", "api/acceptance/payment_keys" },
            handler.Calls.Select(c => c.Path).ToArray());
        Assert.Equal("987654321", result.OrderId);
        Assert.Equal("PAYKEY", result.PaymentToken);
        Assert.Equal("https://accept.test/api/acceptance/iframes/555?payment_token=PAYKEY", result.IframeUrl);
    }

    [Fact]
    public async Task Auth_call_sends_the_api_key_and_the_next_calls_use_the_returned_token()
    {
        var handler = new FakeHandler(HappyResponses);

        await Client(handler).CreateCheckoutAsync(Request);

        Assert.Equal("test-api-key", Body(handler.Calls[0]).GetProperty("api_key").GetString());
        Assert.Null(handler.Calls[0].BearerToken);

        Assert.Equal("AUTH123", Body(handler.Calls[1]).GetProperty("auth_token").GetString());
        Assert.Equal("AUTH123", handler.Calls[1].BearerToken);
        Assert.Equal("AUTH123", Body(handler.Calls[2]).GetProperty("auth_token").GetString());
        Assert.Equal("AUTH123", handler.Calls[2].BearerToken);
    }

    [Fact]
    public async Task Order_call_has_the_amount_currency_and_merchant_order_id()
    {
        var handler = new FakeHandler(HappyResponses);

        await Client(handler).CreateCheckoutAsync(Request);

        var order = Body(handler.Calls[1]);
        Assert.Equal(27000, order.GetProperty("amount_cents").GetInt64());
        Assert.Equal("EGP", order.GetProperty("currency").GetString());
        Assert.Equal("A5-abc", order.GetProperty("merchant_order_id").GetString());
        Assert.False(order.GetProperty("delivery_needed").GetBoolean());
        Assert.Equal(0, order.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Payment_key_call_has_the_order_integration_expiration_and_billing_data()
    {
        var handler = new FakeHandler(HappyResponses);

        await Client(handler).CreateCheckoutAsync(Request);

        var key = Body(handler.Calls[2]);
        Assert.Equal(27000, key.GetProperty("amount_cents").GetInt64());
        Assert.Equal(900, key.GetProperty("expiration").GetInt32());
        Assert.Equal(987654321, key.GetProperty("order_id").GetInt64());
        Assert.Equal(4242, key.GetProperty("integration_id").GetInt32());
        Assert.Equal("EGP", key.GetProperty("currency").GetString());

        var billing = key.GetProperty("billing_data");
        Assert.Equal("Patient", billing.GetProperty("first_name").GetString());
        Assert.Equal("P2", billing.GetProperty("last_name").GetString());
        Assert.Equal("patient2@clinic.local", billing.GetProperty("email").GetString());
        Assert.Equal("NA", billing.GetProperty("phone_number").GetString());
        foreach (var field in new[] { "apartment", "floor", "street", "building", "shipping_method", "postal_code", "city", "country", "state" })
        {
            Assert.Equal("NA", billing.GetProperty(field).GetString());
        }
    }

    [Fact]
    public async Task Payment_token_is_url_escaped_in_the_iframe_link()
    {
        var handler = new FakeHandler(path => path == "api/acceptance/payment_keys"
            ? (HttpStatusCode.Created, """{"token":"a+b/c="}""")
            : HappyResponses(path));

        var result = await Client(handler).CreateCheckoutAsync(Request);

        Assert.EndsWith("?payment_token=a%2Bb%2Fc%3D", result.IframeUrl);
    }

    [Fact]
    public async Task Failed_authentication_stops_after_the_first_call()
    {
        var handler = new FakeHandler(path => path == "api/auth/tokens"
            ? (HttpStatusCode.Unauthorized, """{"detail":"bad key"}""")
            : HappyResponses(path));

        var ex = await Assert.ThrowsAsync<PaymobException>(() => Client(handler).CreateCheckoutAsync(Request));

        Assert.Contains("401", ex.Message);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task Error_message_never_contains_the_api_key()
    {
        var handler = new FakeHandler(_ => (HttpStatusCode.BadRequest, """{"detail":"nope"}"""));

        var ex = await Assert.ThrowsAsync<PaymobException>(() => Client(handler).CreateCheckoutAsync(Request));

        Assert.DoesNotContain("test-api-key", ex.Message);
    }

    [Fact]
    public async Task Auth_response_without_a_token_is_rejected()
    {
        var handler = new FakeHandler(path => path == "api/auth/tokens"
            ? (HttpStatusCode.Created, """{"profile":{}}""")
            : HappyResponses(path));

        await Assert.ThrowsAsync<PaymobException>(() => Client(handler).CreateCheckoutAsync(Request));
    }

    [Fact]
    public async Task Order_response_without_an_id_is_rejected()
    {
        var handler = new FakeHandler(path => path == "api/ecommerce/orders"
            ? (HttpStatusCode.Created, """{"amount_cents":27000}""")
            : HappyResponses(path));

        await Assert.ThrowsAsync<PaymobException>(() => Client(handler).CreateCheckoutAsync(Request));
    }

    [Fact]
    public async Task Payment_key_response_without_a_token_is_rejected()
    {
        var handler = new FakeHandler(path => path == "api/acceptance/payment_keys"
            ? (HttpStatusCode.Created, """{"detail":"x"}""")
            : HappyResponses(path));

        await Assert.ThrowsAsync<PaymobException>(() => Client(handler).CreateCheckoutAsync(Request));
    }

    [Fact]
    public async Task Invalid_json_is_rejected()
    {
        var handler = new FakeHandler(_ => (HttpStatusCode.OK, "<html>oops</html>"));

        await Assert.ThrowsAsync<PaymobException>(() => Client(handler).CreateCheckoutAsync(Request));
    }

    [Fact]
    public async Task Network_failure_is_wrapped_in_a_paymob_exception()
    {
        await Assert.ThrowsAsync<PaymobException>(() => Client(new ThrowingHandler()).CreateCheckoutAsync(Request));
    }

    [Fact]
    public void Settings_do_not_print_their_secrets()
    {
        var text = Settings.ToString();

        Assert.DoesNotContain("test-api-key", text);
        Assert.DoesNotContain("test-hmac", text);
    }
}
