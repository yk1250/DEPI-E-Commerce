using System.Text.Json;
using Clinic.Application.Features.Payments;

namespace Clinic.Application.Tests.Payments;

public class PaymobHmacTests
{
    // عينة من توثيق Paymob (Transaction Processed)
    private const string Example = """
{"amount_cents":100,"created_at":"2020-03-25T18:39:44.719228","currency":"OMR","error_occured":false,"has_parent_transaction":false,"id":25567066,"integration_id":741,"is_3d_secure":true,"is_auth":false,"is_capture":false,"is_refunded":false,"is_standalone_payment":true,"is_voided":false,"order":{"id":47782394},"owner":705,"pending":false,"source_data":{"pan":"2346","sub_type":"MasterCard","type":"card"},"success":true}
""";

    // النص المجمّع كما هو في التوثيق
    private const string ExamplePayload =
        "1002020-03-25T18:39:44.719228OMRfalsefalse25567066741truefalsefalsefalsetruefalse47782394705false2346MasterCardcardtrue";

    // HMAC-SHA512 للنص أعلاه بالسر "unit-test-secret"، محسوب بتنفيذ مستقل (Python)
    private const string Secret = "unit-test-secret";
    private const string ReferenceHmac =
        "d33957e7c43a03ef1dc19692cbf63f72fbecc7e914e3cba9a7911b86fb55f3be45a00abe2d253725f91c47a6a58b5f64d2b64d6d2d2425e6cd10e01b54126064";

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Payload_matches_the_documented_example()
    {
        Assert.Equal(ExamplePayload, PaymobHmac.BuildPayload(Parse(Example)));
    }

    [Fact]
    public void Hmac_matches_an_independent_implementation()
    {
        Assert.Equal(ReferenceHmac, PaymobHmac.Compute(Parse(Example), Secret));
    }

    [Fact]
    public void Correct_signature_is_accepted_in_any_hex_case_and_with_spaces()
    {
        var transaction = Parse(Example);

        Assert.True(PaymobHmac.IsValid(transaction, Secret, ReferenceHmac));
        Assert.True(PaymobHmac.IsValid(transaction, Secret, ReferenceHmac.ToUpperInvariant()));
        Assert.True(PaymobHmac.IsValid(transaction, Secret, $"  {ReferenceHmac}  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("deadbeef")]
    [InlineData("not-a-hex-value")]
    public void Missing_or_wrong_signature_is_rejected(string? provided)
    {
        Assert.False(PaymobHmac.IsValid(Parse(Example), Secret, provided));
    }

    [Fact]
    public void Wrong_secret_is_rejected()
    {
        Assert.False(PaymobHmac.IsValid(Parse(Example), "another-secret", ReferenceHmac));
    }

    [Theory]
    [InlineData("\"amount_cents\":100", "\"amount_cents\":101")]
    [InlineData("\"success\":true", "\"success\":false")]
    [InlineData("\"order\":{\"id\":47782394}", "\"order\":{\"id\":47782395}")]
    [InlineData("\"id\":25567066", "\"id\":25567067")]
    [InlineData("\"currency\":\"OMR\"", "\"currency\":\"EGP\"")]
    [InlineData("\"pending\":false", "\"pending\":true")]
    public void Changing_a_signed_field_invalidates_the_signature(string original, string changed)
    {
        Assert.Contains(original, Example);
        var tampered = Parse(Example.Replace(original, changed));

        Assert.False(PaymobHmac.IsValid(tampered, Secret, ReferenceHmac));
    }

    [Fact]
    public void Unsigned_fields_do_not_change_the_signature()
    {
        var withExtras = Example
            .Replace("\"order\":{\"id\":47782394}", "\"order\":{\"id\":47782394,\"merchant_order_id\":\"A99-evil\"}")
            .Replace("{\"amount_cents\"", "{\"extra\":\"x\",\"amount_cents\"");

        Assert.True(PaymobHmac.IsValid(Parse(withExtras), Secret, ReferenceHmac));
    }

    [Fact]
    public void Missing_and_null_fields_count_as_empty()
    {
        Assert.Equal(string.Empty, PaymobHmac.BuildPayload(Parse("{}")));
        Assert.Equal(string.Empty, PaymobHmac.BuildPayload(Parse("""{"success":null,"pending":null,"order":null,"source_data":null}""")));
    }

    [Fact]
    public void Non_object_input_gives_an_empty_payload_and_never_validates()
    {
        var array = Parse("[1,2,3]");

        Assert.Equal(string.Empty, PaymobHmac.BuildPayload(array));
        Assert.False(PaymobHmac.IsValid(array, Secret, ReferenceHmac));
    }

    [Fact]
    public void Numbers_keep_their_exact_json_text()
    {
        var transaction = Parse("""{"amount_cents":100.50,"owner":9007199254740993}""");

        var payload = PaymobHmac.BuildPayload(transaction);

        Assert.StartsWith("100.50", payload);
        Assert.Contains("9007199254740993", payload);
    }
}
