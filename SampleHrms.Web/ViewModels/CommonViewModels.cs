using Microsoft.AspNetCore.Mvc.Rendering;

namespace SampleHrms.Web.ViewModels;

public class LeaveBalanceViewModel
{
    public string LeaveType { get; set; } = "";
    public int Total { get; set; }
    public int Used { get; set; }
    public int Remaining => Total - Used;
}

public class LeaveRequestViewModel
{
    public int Id { get; set; }
    public string EmployeeName { get; set; } = "";
    public string LeaveType { get; set; } = "";
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int Days { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ReviewComment { get; set; }
}

public class ApplyLeaveViewModel
{
    [System.ComponentModel.DataAnnotations.Required]
    public DateTime FromDate { get; set; } = DateTime.Today;
    [System.ComponentModel.DataAnnotations.Required]
    public DateTime ToDate { get; set; } = DateTime.Today;
    [System.ComponentModel.DataAnnotations.Required]
    public int LeaveTypeId { get; set; }
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(500)]
    public string Reason { get; set; } = "";
    public List<SelectListItem>? LeaveTypes { get; set; }
}

public class EmployeeViewModel
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmployeeCode { get; set; } = "";
    public string Department { get; set; } = "";
    public bool IsActive { get; set; }
    public string Role { get; set; } = "";
}

public class AttendanceViewModel
{
    public int Id { get; set; }
    public string EmployeeName { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime Date { get; set; }
    public TimeSpan? CheckIn { get; set; }
    public TimeSpan? CheckOut { get; set; }
    public string Status { get; set; } = "";
}

public class LeaveTypeViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int DefaultYearlyBalance { get; set; }
    public bool IsActive { get; set; }
}
