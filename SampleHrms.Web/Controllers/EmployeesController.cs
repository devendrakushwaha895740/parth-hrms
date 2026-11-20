using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SampleHrms.Web.Services;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Controllers;

[Authorize(Roles = "HR,Admin")]
public class EmployeesController : Controller
{
    private readonly IEmployeeService _employees;
    public EmployeesController(IEmployeeService employees) => _employees = employees;
    private ActingUser Acting => ActingUserFactory.FromRequest(User, Request, GetRole());
    private string GetRole() => User.IsInRole("Admin") ? "Admin" : "HR";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewData["AgentPage"] = "employees";
        var result = await _employees.GetEmployees(Acting);
        return View(result.Data as IEnumerable<EmployeeViewModel> ?? Enumerable.Empty<EmployeeViewModel>());
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult Create()
    { ViewData["AgentPage"] = "employee-create"; return View(new CreateEmployeeViewModel()); }

    [Authorize(Roles = "Admin")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateEmployeeViewModel model)
    {
        ViewData["AgentPage"] = "employee-create";
        if (!ModelState.IsValid) { TempData["Error"] = "Please complete all employee fields."; return View(model); }
        var result = await _employees.CreateEmployee(Acting, model.Email, model.FullName, model.EmployeeCode,
            model.Department, model.Password, model.Role);
        if (!result.Success) { TempData["Error"] = result.Message; return View(model); }
        TempData["Success"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(string id, bool isActive)
    {
        var result = await _employees.SetActive(Acting, id, isActive);
        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }
}
