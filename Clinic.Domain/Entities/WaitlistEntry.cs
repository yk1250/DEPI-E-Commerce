using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class WaitlistEntry
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public int PatientId { get; set; }
    public DateOnly DesiredDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? NotifiedAt { get; set; }

    // ملاحظة: ربط القائمة بالميعاد المفتوح لسه متحسمش، هنضيف الحقل بعد القرار
}