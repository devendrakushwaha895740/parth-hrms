using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SampleHrms.Web.Services;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Controllers;

[Authorize]
public class LeaveController : Controller
{
    private readonly ILeaveService _leaves;
    private readonly IEmployeeService _employees;
    public LeaveController(ILeaveService leaves, IEmployeeService employees) { _leaves = leaves; _employees = employees; }
    private ActingUser Acting => ActingUserFactory.FromRequest(User, Request, GetRole());
    private string GetRole() => User.IsInRole("Admin") ? "Admin" : User.IsInRole("HR") ? "HR" : "Employee";

    [Authorize(Roles = "Employee")]
    [HttpGet]
    public async Task<IActionResult> Apply()
    {
        ViewData["AgentPage"] = "leave-apply";
        var types = await _employees.GetLeaveTypes(Acting);
        var model = new ApplyLeaveViewModel { LeaveTypes = new() };
        if (types.Success)
            model.LeaveTypes = ((IEnumerable<LeaveTypeViewModel>)types.Data!).Where(x => x.IsActive)
                .Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name }).ToList();
        return View(model);
    }

    [Authorize(Roles = "Employee")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(ApplyLeaveViewModel model)
    {
        ViewData["AgentPage"] = "leave-apply";
        if (!ModelState.IsValid) { TempData["Error"] = "Please complete all leave fields."; return RedirectToAction(nameof(Apply)); }
        var result = await _leaves.ApplyLeave(Acting, model);
        if (!result.Success) { TempData["Error"] = result.Message; return RedirectToAction(nameof(Apply)); }
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(History));
    }

    [Authorize(Roles = "Employee")]
    public async Task<IActionResult> History()
    {
        ViewData["AgentPage"] = "leave-history";
        var result = await _leaves.GetHistory(Acting);
        return View(result.Data as IEnumerable<LeaveRequestViewModel> ?? Enumerable.Empty<LeaveRequestViewModel>());
    }

    [Authorize(Roles = "HR,Admin")]
    public async Task<IActionResult> Pending()
    {
        ViewData["AgentPage"] = "leave-pending";
        var result = await _leaves.GetPending(Acting);
        return View(result.Data as IEnumerable<LeaveRequestViewModel> ?? Enumerable.Empty<LeaveRequestViewModel>());
    }

    [Authorize(Roles = "HR,Admin")]
    public async Task<IActionResult> All()
    {
        ViewData["AgentPage"] = "leave-all";
        var result = await _leaves.GetAll(Acting);
        return View(result.Data as IEnumerable<LeaveRequestViewModel> ?? Enumerable.Empty<LeaveRequestViewModel>());
    }

    [Authorize(Roles = "HR,Admin")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string comment)
    {
        var result = await _leaves.Approve(Acting, id, comment ?? "");
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Pending));
    }

    [Authorize(Roles = "HR,Admin")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string comment)
    {
        var result = await _leaves.Reject(Acting, id, comment ?? "");
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Pending));
    }

    [Authorize(Roles = "Employee")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var result = await _leaves.Cancel(Acting, id);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(History));
    }
}
