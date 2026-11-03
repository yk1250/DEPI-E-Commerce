using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Enums;

public enum AppointmentStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    CheckedIn = 2,
    Completed = 3,
    Cancelled = 4,
    NoShow = 5,
    Expired = 6
}