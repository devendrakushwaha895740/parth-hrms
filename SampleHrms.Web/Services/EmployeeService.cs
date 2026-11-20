using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SampleHrms.Web.Data;
using SampleHrms.Web.Models;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Services;

public class EmployeeService : IEmployeeService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    public EmployeeService(ApplicationDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles)
    { _db = db; _users = users; _roles = roles; }

    private async Task Audit(ActingUser u, string action, string details)
    {
        _db.AuditLogs.Add(new AuditLog { UserId = u.UserId, Action = action, Details = details, Source = u.Source });
        await _db.SaveChangesAsync();
    }

    public async Task<Result> GetEmployees(ActingUser actingUser)
    {
        if (actingUser.Role is not ("HR" or "Admin")) return Result.Fail("Not authorized.");
        var users = await _db.Users.OrderBy(x => x.FullName).ToListAsync();
        var list = new List<EmployeeViewModel>();
        foreach (var u in users)
        {
            var roles = await _users.GetRolesAsync(u);
            list.Add(new EmployeeViewModel { Id = u.Id, FullName = u.FullName, Email = u.Email ?? "", EmployeeCode = u.EmployeeCode,
                Department = u.Department, IsActive = u.IsActive, Role = roles.FirstOrDefault() ?? "" });
        }
        return Result.Ok(data: list);
    }

    public async Task<Result> CreateEmployee(ActingUser actingUser, string email, string fullName, string employeeCode,
        string department, string password, string role)
    {
        if (actingUser.Role != "Admin") return Result.Fail("Only Admin can create users.");
        if (role is not ("Admin" or "HR" or "Employee")) return Result.Fail("Invalid role.");
        if (await _users.FindByEmailAsync(email) != null) return Result.Fail("A user with this email already exists.");
        var user = new ApplicationUser { UserName = email, Email = email, FullName = fullName.Trim(),
            EmployeeCode = employeeCode.Trim(), Department = department.Trim(), EmailConfirmed = true };
        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded) return Result.Fail(string.Join(" ", created.Errors.Select(x => x.Description)));
        await _users.AddToRoleAsync(user, role);
        await _db.LeaveBalances.AddRangeAsync(
            await _db.LeaveTypes.Where(x => x.IsActive).Select(x =>
                new LeaveBalance { UserId = user.Id, LeaveTypeId = x.Id, Year = DateTime.UtcNow.Year, Total = x.DefaultYearlyBalance }).ToListAsync());
        await _db.SaveChangesAsync();
        await Audit(actingUser, "CreateUser", $"Created user {email} with role {role}.");
        return Result.Ok("Employee account created.");
    }

    public async Task<Result> SetActive(ActingUser actingUser, string userId, bool isActive)
    {
        if (actingUser.Role != "Admin") return Result.Fail("Only Admin can activate or deactivate users.");
        var user = await _users.FindByIdAsync(userId);
        if (user is null) return Result.Fail("User not found.");
        user.IsActive = isActive;
        await _users.UpdateAsync(user);
        await Audit(actingUser, "SetUserActive", $"Set user {user.Email} active={isActive}.");
        return Result.Ok(isActive ? "User activated." : "User deactivated.");
    }

    public async Task<Result> GetLeaveTypes(ActingUser actingUser)
    {
        if (actingUser.Role is not ("Admin" or "HR" or "Employee")) return Result.Fail("Not authorized.");
        var types = await _db.LeaveTypes.OrderBy(x => x.Name).Select(x =>
            new LeaveTypeViewModel { Id = x.Id, Name = x.Name, DefaultYearlyBalance = x.DefaultYearlyBalance, IsActive = x.IsActive }).ToListAsync();
        return Result.Ok(data: types);
    }

    public async Task<Result> CreateLeaveType(ActingUser actingUser, string name, int balance)
    {
        if (actingUser.Role != "Admin") return Result.Fail("Only Admin can manage leave types.");
        if (string.IsNullOrWhiteSpace(name) || balance < 0) return Result.Fail("Valid name and balance are required.");
        _db.LeaveTypes.Add(new LeaveType { Name = name.Trim(), DefaultYearlyBalance = balance });
        await _db.SaveChangesAsync();
        await Audit(actingUser, "CreateLeaveType", $"Created leave type {name.Trim()} with balance {balance}.");
        return Result.Ok("Leave type created.");
    }

    public async Task<Result> UpdateLeaveType(ActingUser actingUser, int id, string name, int balance, bool isActive)
    {
        if (actingUser.Role != "Admin") return Result.Fail("Only Admin can manage leave types.");
        var type = await _db.LeaveTypes.FindAsync(id);
        if (type is null) return Result.Fail("Leave type not found.");
        type.Name = name.Trim(); type.DefaultYearlyBalance = balance; type.IsActive = isActive;
        await _db.SaveChangesAsync();
        await Audit(actingUser, "UpdateLeaveType", $"Updated leave type {id}: {name.Trim()}, balance={balance}, active={isActive}.");
        return Result.Ok("Leave type updated.");
    }

    public async Task<Result> DeleteLeaveType(ActingUser actingUser, int id)
    {
        if (actingUser.Role != "Admin") return Result.Fail("Only Admin can manage leave types.");
        var type = await _db.LeaveTypes.FindAsync(id);
        if (type is null) return Result.Fail("Leave type not found.");
        var inUse = await _db.LeaveRequests.AnyAsync(x => x.LeaveTypeId == id);
        if (inUse) { type.IsActive = false; await _db.SaveChangesAsync(); }
        else _db.LeaveTypes.Remove(type);
        await _db.SaveChangesAsync();
        await Audit(actingUser, "DeleteLeaveType", $"Deleted or deactivated leave type {id}.");
        return Result.Ok("Leave type removed.");
    }
}
