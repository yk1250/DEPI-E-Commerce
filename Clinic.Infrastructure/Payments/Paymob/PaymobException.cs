namespace Clinic.Infrastructure.Payments.Paymob;

/// خطأ في الاتصال بـ Paymob أو رد مش متوقع منه. الرسالة بتخلو من أي أسرار.
public sealed class PaymobException : Exception
{
    public PaymobException(string message) : base(message)
    {
    }

    public PaymobException(string message, Exception inner) : base(message, inner)
    {
    }
}
