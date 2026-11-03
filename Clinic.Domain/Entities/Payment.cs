using System;
using System.Collections.Generic;
using System.Text;

using Clinic.Domain.Enums;

namespace Clinic.Domain.Entities;

public class Payment
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int? CouponId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }

    // Amount = max(0, OriginalAmount - DiscountAmount)، يتحسب في Application
    public decimal Amount { get; set; }

    public string? PaymobOrderId { get; set; }
    public string? TransactionId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}