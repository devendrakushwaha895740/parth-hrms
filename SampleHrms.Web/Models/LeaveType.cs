using System.ComponentModel.DataAnnotations;

namespace SampleHrms.Web.Models;

public class LeaveType
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public int DefaultYearlyBalance { get; set; }
    public bool IsActive { get; set; } = true;
}
