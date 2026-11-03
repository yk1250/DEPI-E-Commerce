using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities
{
    public class Doctor
    {
        public int Id { get; set; }

        // يربط الدكتور بحساب ApplicationUser (في Infrastructure)
        public Guid UserId { get; set; }

        public int SpecialtyId { get; set; }
        public Specialty Specialty { get; set; } = null!;

        public int ClinicId { get; set; }
        public MedicalClinic Clinic { get; set; } = null!;

        public string? Bio { get; set; }
        public int YearsOfExperience { get; set; }

        // سعر الكشف العادي
        public decimal ConsultationFee { get; set; }

        // نسبة سعر كشف المتابعة من السعر الأساسي (0 يعني كشف مجاني)
        public decimal FollowUpFeePercent { get; set; }

        // المدة اللي يقدر فيها المريض يرجع بسعر المتابعة
        public int FollowUpWindowDays { get; set; }

        // بيتحدّثوا من التقييمات
        public decimal AverageRating { get; set; }
        public int ReviewsCount { get; set; }

        // بتتضبط من الإدارة
        public bool IsApproved { get; set; }
    }
}