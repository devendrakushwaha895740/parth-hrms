using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleHrms.Web.Services;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ILeaveService _leaves;
    private readonly IAttendanceService _attendance;
    public DashboardController(ILeaveService leaves, IAttendanceService attendance) { _leaves = leaves; _attendance = attendance; }

    private ActingUser Acting => new(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", GetRole());
    private string GetRole() => User.IsInRole("Admin") ? "Admin" : User.IsInRole("HR") ? "HR" : "Employee";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["AgentPage"] = "dashboard";
        ViewBag.Role = GetRole();
        if (User.IsInRole("Employee"))
        {
            var balances = await _leaves.GetBalances(Acting);
            var attendance = await _attendance.GetMyAttendance(Acting);
            ViewBag.Balances = balances.Data as List<LeaveBalanceViewModel> ?? new();
            ViewBag.AttendanceCount = (attendance.Data as List<AttendanceViewModel>)?.Count ?? 0;
        }
        return View();
    }
}
