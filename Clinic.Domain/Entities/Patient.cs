using Clinic.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class Patient
{
    public int Id { get; set; }

    // يربط المريض بحساب ApplicationUser (في Infrastructure)
    public Guid UserId { get; set; }

    public DateTime DateOfBirth { get; set; }

    public Gender Gender { get; set; }
}