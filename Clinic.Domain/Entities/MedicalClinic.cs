using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class MedicalClinic
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}