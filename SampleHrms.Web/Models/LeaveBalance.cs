namespace SampleHrms.Web.Models;

public class LeaveBalance
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public int LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public int Year { get; set; }
    public int Total { get; set; }
    public int Used { get; set; }
}
