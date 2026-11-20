using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleHrms.Web.Services;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Controllers;

[Authorize]
public class AttendanceController : Controller
{
    private readonly IAttendanceService _attendance;
    private readonly IEmployeeService _employees;
    public AttendanceController(IAttendanceService attendance, IEmployeeService employees)
    { _attendance = attendance; _employees = employees; }
    private ActingUser Acting => ActingUserFactory.FromRequest(User, Request, GetRole());
    private string GetRole() => User.IsInRole("Admin") ? "Admin" : User.IsInRole("HR") ? "HR" : "Employee";

    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> My()
    {
        ViewData["AgentPage"] = "attendance-my";
        var result = await _attendance.GetMyAttendance(Acting);
        return View(result.Data as IEnumerable<AttendanceViewModel> ?? Enumerable.Empty<AttendanceViewModel>());
    }

    [Authorize(Roles = "HR")]
    public async Task<IActionResult> Manage()
    {
        ViewData["AgentPage"] = "attendance-manage";
        var attendance = await _attendance.GetAllAttendance(Acting);
        var employees = await _employees.GetEmployees(Acting);
        ViewBag.Employees = employees.Data as IEnumerable<EmployeeViewModel> ?? Enumerable.Empty<EmployeeViewModel>();
        return View(attendance.Data as IEnumerable<AttendanceViewModel> ?? Enumerable.Empty<AttendanceViewModel>());
    }

    [Authorize(Roles = "HR")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Manage(string userId, DateTime date, string? checkIn, string? checkOut, string status)
    {
        TimeSpan? parseIn = TimeSpan.TryParse(checkIn, out var inValue) ? inValue : null;
        TimeSpan? parseOut = TimeSpan.TryParse(checkOut, out var outValue) ? outValue : null;
        var result = await _attendance.CorrectAttendance(Acting, userId, date, parseIn, parseOut, status);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Manage));
    }
}
