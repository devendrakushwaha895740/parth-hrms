using Microsoft.EntityFrameworkCore;
using SampleHrms.Web.Data;
using SampleHrms.Web.Models;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Services;

public class AttendanceService : IAttendanceService
{
    private readonly ApplicationDbContext _db;
    public AttendanceService(ApplicationDbContext db) => _db = db;

    private async Task Audit(ActingUser u, string action, string details)
    {
        _db.AuditLogs.Add(new AuditLog { UserId = u.UserId, Action = action, Details = details, Source = u.Source });
        await _db.SaveChangesAsync();
    }

    public async Task<Result> GetMyAttendance(ActingUser actingUser)
    {
        if (actingUser.Role != "Employee") return Result.Fail("Only employees can view personal attendance.");
        var items = await _db.AttendanceRecords.Include(x => x.User).Where(x => x.UserId == actingUser.UserId)
            .OrderByDescending(x => x.Date).Select(x => new AttendanceViewModel
            { Id = x.Id, UserId = x.UserId, EmployeeName = x.User.FullName, Date = x.Date, CheckIn = x.CheckIn, CheckOut = x.CheckOut, Status = x.Status }).ToListAsync();
        return Result.Ok(data: items);
    }

    public async Task<Result> GetAllAttendance(ActingUser actingUser)
    {
        if (actingUser.Role is not ("HR" or "Admin")) return Result.Fail("Not authorized.");
        var items = await _db.AttendanceRecords.Include(x => x.User).OrderByDescending(x => x.Date)
            .Select(x => new AttendanceViewModel { Id = x.Id, UserId = x.UserId, EmployeeName = x.User.FullName,
                Date = x.Date, CheckIn = x.CheckIn, CheckOut = x.CheckOut, Status = x.Status }).ToListAsync();
        return Result.Ok(data: items);
    }

    public async Task<Result> CorrectAttendance(ActingUser actingUser, string userId, DateTime date, TimeSpan? checkIn, TimeSpan? checkOut, string status)
    {
        if (actingUser.Role != "HR") return Result.Fail("Only HR can correct attendance.");
        if (checkIn.HasValue && checkOut.HasValue && checkOut < checkIn) return Result.Fail("Check-out cannot be before check-in.");
        var normalized = date.Date;
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(x => x.UserId == userId && x.Date == normalized);
        if (record is null)
        {
            record = new AttendanceRecord { UserId = userId, Date = normalized };
            _db.AttendanceRecords.Add(record);
        }
        record.CheckIn = checkIn; record.CheckOut = checkOut; record.Status = status;
        await _db.SaveChangesAsync();
        await Audit(actingUser, "CorrectAttendance", $"Corrected attendance for user {userId} on {normalized:yyyy-MM-dd}: {status}.");
        return Result.Ok("Attendance corrected.");
    }
}
