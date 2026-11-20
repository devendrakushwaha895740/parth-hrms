using Microsoft.EntityFrameworkCore;
using SampleHrms.Web.Data;
using SampleHrms.Web.Models;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Services;

public class LeaveService : ILeaveService
{
    private readonly ApplicationDbContext _db;
    public LeaveService(ApplicationDbContext db) => _db = db;

    private static bool IsHrOrAdmin(ActingUser u) => u.Role is "HR" or "Admin";

    private async Task<Result> Audit(ActingUser u, string action, string details)
    {
        _db.AuditLogs.Add(new AuditLog { UserId = u.UserId, Action = action, Details = details, Source = u.Source });
        await _db.SaveChangesAsync();
        return Result.Ok();
    }

    public async Task<Result> GetBalances(ActingUser actingUser, string? userId = null)
    {
        var target = actingUser.UserId;
        if (IsHrOrAdmin(actingUser) && !string.IsNullOrWhiteSpace(userId)) target = userId;
        var year = DateTime.UtcNow.Year;
        var balances = await _db.LeaveBalances.Include(x => x.LeaveType)
            .Where(x => x.UserId == target && x.Year == year)
            .Select(x => new LeaveBalanceViewModel { LeaveType = x.LeaveType.Name, Total = x.Total, Used = x.Used })
            .ToListAsync();
        return Result.Ok(data: balances);
    }

    public async Task<Result> ApplyLeave(ActingUser actingUser, ApplyLeaveViewModel model)
    {
        if (actingUser.Role != "Employee") return Result.Fail("Only employees can apply for leave.");
        if (model.FromDate > model.ToDate) return Result.Fail("From date cannot be after to date.");
        var type = await _db.LeaveTypes.FindAsync(model.LeaveTypeId);
        if (type is null || !type.IsActive) return Result.Fail("Invalid leave type.");
        var today = DateTime.Today;
        if (model.FromDate.Date < today && type.Name != "Sick") return Result.Fail("Past dates are not allowed.");
        var overlaps = await _db.LeaveRequests.AnyAsync(x => x.UserId == actingUser.UserId &&
            x.Status != "Rejected" && x.Status != "Cancelled" &&
            model.FromDate.Date <= x.ToDate && model.ToDate.Date >= x.FromDate);
        if (overlaps) return Result.Fail("Leave dates overlap an existing request.");
        var days = CountWeekdays(model.FromDate, model.ToDate);
        if (days == 0) return Result.Fail("Leave period has no weekdays.");
        var year = model.FromDate.Year;
        var balance = await _db.LeaveBalances.FirstOrDefaultAsync(x => x.UserId == actingUser.UserId && x.LeaveTypeId == type.Id && x.Year == year);
        if (balance is null) balance = new LeaveBalance { UserId = actingUser.UserId, LeaveTypeId = type.Id, Year = year, Total = type.DefaultYearlyBalance };
        if (balance.Total - balance.Used < days) return Result.Fail("Insufficient leave balance.");
        var request = new LeaveRequest { UserId = actingUser.UserId, LeaveTypeId = type.Id, FromDate = model.FromDate.Date,
            ToDate = model.ToDate.Date, Days = days, Reason = model.Reason.Trim(), Status = "Pending" };
        _db.LeaveRequests.Add(request);
        if (balance.Id == 0) _db.LeaveBalances.Add(balance);
        await _db.SaveChangesAsync();
        await Audit(actingUser, "ApplyLeave", $"Leave request {request.Id} submitted for {days} day(s).");
        return Result.Ok("Leave request submitted.", request.Id);
    }

    public async Task<Result> GetHistory(ActingUser actingUser)
    {
        if (actingUser.Role != "Employee") return Result.Fail("Only employees can view personal leave history.");
        var items = await _db.LeaveRequests.Include(x => x.LeaveType).Include(x => x.User)
            .Where(x => x.UserId == actingUser.UserId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new LeaveRequestViewModel { Id = x.Id, EmployeeName = x.User.FullName, LeaveType = x.LeaveType.Name,
                FromDate = x.FromDate, ToDate = x.ToDate, Days = x.Days, Reason = x.Reason, Status = x.Status, ReviewComment = x.ReviewComment }).ToListAsync();
        return Result.Ok(data: items);
    }

    public async Task<Result> GetPending(ActingUser actingUser)
    {
        if (!IsHrOrAdmin(actingUser)) return Result.Fail("Not authorized to view pending requests.");
        return await GetByStatus("Pending", actingUser);
    }

    public async Task<Result> GetAll(ActingUser actingUser)
    {
        if (!IsHrOrAdmin(actingUser)) return Result.Fail("Not authorized to view all requests.");
        return await GetByStatus(null, actingUser);
    }

    private async Task<Result> GetByStatus(string? status, ActingUser actingUser)
    {
        var query = _db.LeaveRequests.Include(x => x.LeaveType).Include(x => x.User).AsQueryable();
        if (status != null) query = query.Where(x => x.Status == status);
        var items = await query.OrderByDescending(x => x.CreatedAt).Select(x => new LeaveRequestViewModel
        { Id = x.Id, EmployeeName = x.User.FullName, LeaveType = x.LeaveType.Name, FromDate = x.FromDate, ToDate = x.ToDate,
          Days = x.Days, Reason = x.Reason, Status = x.Status, ReviewComment = x.ReviewComment }).ToListAsync();
        return Result.Ok(data: items);
    }

    public async Task<Result> Approve(ActingUser actingUser, int requestId, string comment)
        => await Review(actingUser, requestId, "Approved", comment);

    public async Task<Result> Reject(ActingUser actingUser, int requestId, string comment)
        => await Review(actingUser, requestId, "Rejected", comment);

    private async Task<Result> Review(ActingUser actingUser, int requestId, string status, string comment)
    {
        if (!IsHrOrAdmin(actingUser)) return Result.Fail("Only HR or Admin can review leave.");
        var request = await _db.LeaveRequests.Include(x => x.LeaveType).FirstOrDefaultAsync(x => x.Id == requestId);
        if (request is null) return Result.Fail("Leave request not found.");
        if (request.Status != "Pending") return Result.Fail("Only pending requests can be reviewed.");
        if (status == "Approved")
        {
            if (request.UserId == actingUser.UserId) return Result.Fail("You cannot approve your own leave request.");
            var balance = await _db.LeaveBalances.FirstOrDefaultAsync(x => x.UserId == request.UserId &&
                x.LeaveTypeId == request.LeaveTypeId && x.Year == request.FromDate.Year);
            if (balance is null || balance.Total - balance.Used < request.Days) return Result.Fail("Insufficient leave balance.");
            balance.Used += request.Days;
        }
        request.Status = status;
        request.ReviewedById = actingUser.UserId;
        request.ReviewComment = comment.Trim();
        await _db.SaveChangesAsync();
        await Audit(actingUser, status == "Approved" ? "ApproveLeave" : "RejectLeave",
            $"Leave request {requestId} {status.ToLowerInvariant()}. Comment: {comment.Trim()}");
        return Result.Ok($"Leave request {status.ToLowerInvariant()}.");
    }

    public async Task<Result> Cancel(ActingUser actingUser, int requestId)
    {
        var request = await _db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == requestId);
        if (request is null) return Result.Fail("Leave request not found.");
        if (request.UserId != actingUser.UserId) return Result.Fail("You can cancel only your own leave.");
        if (request.Status != "Pending") return Result.Fail("Only pending requests can be cancelled.");
        request.Status = "Cancelled";
        await _db.SaveChangesAsync();
        await Audit(actingUser, "CancelLeave", $"Leave request {requestId} cancelled.");
        return Result.Ok("Leave request cancelled.");
    }

    public static int CountWeekdays(DateTime from, DateTime to)
    {
        var count = 0;
        for (var date = from.Date; date <= to.Date; date = date.AddDays(1))
            if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday) count++;
        return count;
    }
}
