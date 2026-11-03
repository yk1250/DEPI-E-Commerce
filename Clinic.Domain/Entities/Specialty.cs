using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class Specialty
{
    public int Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
}