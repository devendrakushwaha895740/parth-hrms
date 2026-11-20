namespace SampleHrms.Web.Models;

public class LeaveRequest
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public int LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int Days { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string? ReviewedById { get; set; }
    public string? ReviewComment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
