using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SampleHrms.Web.Models;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    public AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users)
    { _signIn = signIn; _users = users; }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    { ViewData["ReturnUrl"] = returnUrl; return View(new LoginViewModel()); }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["AgentPage"] = "login";
        if (!ModelState.IsValid) { TempData["Error"] = "Please enter valid login details."; return View(model); }
        var user = await _users.FindByEmailAsync(model.Email);
        if (user == null || !user.IsActive) { TempData["Error"] = "Invalid login or inactive account."; return View(model); }
        var result = await _signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, true);
        if (result.Succeeded) return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/Dashboard" : returnUrl);
        TempData["Error"] = "Invalid email or password.";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    { await _signIn.SignOutAsync(); return RedirectToAction(nameof(Login)); }

    public IActionResult AccessDenied() { ViewData["AgentPage"] = "access-denied"; return View(); }
}
