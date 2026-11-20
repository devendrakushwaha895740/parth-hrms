using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleHrms.Web.Services;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Controllers;

[Authorize(Roles = "Admin")]
public class LeaveTypesController : Controller
{
    private readonly IEmployeeService _employees;
    public LeaveTypesController(IEmployeeService employees) => _employees = employees;
    private ActingUser Acting => ActingUserFactory.FromRequest(User, Request, "Admin");

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["AgentPage"] = "leave-types";
        var result = await _employees.GetLeaveTypes(Acting);
        return View(result.Data as IEnumerable<LeaveTypeViewModel> ?? Enumerable.Empty<LeaveTypeViewModel>());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, int balance)
    {
        var result = await _employees.CreateLeaveType(Acting, name, balance);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int id, string name, int balance, bool isActive)
    {
        var result = await _employees.UpdateLeaveType(Acting, id, name, balance, isActive);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _employees.DeleteLeaveType(Acting, id);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
