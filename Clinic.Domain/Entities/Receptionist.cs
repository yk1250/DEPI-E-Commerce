using System;
using System.Collections.Generic;
using System.Text;

namespace Clinic.Domain.Entities;

public class Receptionist
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int ClinicId { get; set; }
}