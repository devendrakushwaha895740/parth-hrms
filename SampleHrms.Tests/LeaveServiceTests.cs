using Microsoft.EntityFrameworkCore;
using SampleHrms.Web.Data;
using SampleHrms.Web.Models;
using SampleHrms.Web.Services;
using SampleHrms.Web.ViewModels;

namespace SampleHrms.Tests;

public class LeaveServiceTests
{
    private static async Task<(ApplicationDbContext db, LeaveService service, string employeeId, int casualId)> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new ApplicationDbContext(options);
        var employee = new ApplicationUser { Id = "emp-1", FullName = "Employee", UserName = "employee@test.com", IsActive = true };
        db.Users.Add(employee);
        var type = new LeaveType { Name = "Casual", DefaultYearlyBalance = 12, IsActive = true };
        db.LeaveTypes.Add(type);
        await db.SaveChangesAsync();
        db.LeaveBalances.Add(new LeaveBalance { UserId = employee.Id, LeaveTypeId = type.Id, Year = DateTime.Today.Year, Total = 12, Used = 0 });
        await db.SaveChangesAsync();
        return (db, new LeaveService(db), employee.Id, type.Id);
    }

    [Fact]
    public async Task ApplyLeave_FromAfterTo_Fails()
    {
        var (db, service, emp, type) = await CreateAsync();
        var result = await service.ApplyLeave(new ActingUser(emp, "Employee"),
            new ApplyLeaveViewModel { FromDate = DateTime.Today.AddDays(5), ToDate = DateTime.Today.AddDays(4), LeaveTypeId = type, Reason = "Test" });
        Assert.False(result.Success);
        db.Dispose();
    }

    [Fact]
    public async Task ApplyLeave_OverlappingRequest_Fails()
    {
        var (db, service, emp, type) = await CreateAsync();
        var from = DateTime.Today.AddDays(10);
        db.LeaveRequests.Add(new LeaveRequest { UserId = emp, LeaveTypeId = type, FromDate = from, ToDate = from.AddDays(2), Days = 3, Reason = "Existing", Status = "Pending" });
        await db.SaveChangesAsync();
        var result = await service.ApplyLeave(new ActingUser(emp, "Employee"),
            new ApplyLeaveViewModel { FromDate = from.AddDays(1), ToDate = from.AddDays(3), LeaveTypeId = type, Reason = "New" });
        Assert.False(result.Success);
        Assert.Contains("overlap", result.Message, StringComparison.OrdinalIgnoreCase);
        db.Dispose();
    }

    [Fact]
    public async Task ApplyLeave_InsufficientBalance_Fails()
    {
        var (db, service, emp, type) = await CreateAsync();
        var balance = await db.LeaveBalances.SingleAsync();
        balance.Used = 12;
        await db.SaveChangesAsync();
        var result = await service.ApplyLeave(new ActingUser(emp, "Employee"),
            new ApplyLeaveViewModel { FromDate = DateTime.Today.AddDays(10), ToDate = DateTime.Today.AddDays(11), LeaveTypeId = type, Reason = "Test" });
        Assert.False(result.Success);
        Assert.Contains("Insufficient", result.Message);
        db.Dispose();
    }

    [Fact]
    public async Task ApplyLeave_WeekendOnly_Fails()
    {
        var (db, service, emp, type) = await CreateAsync();
        var saturday = DateTime.Today;
        while (saturday.DayOfWeek != DayOfWeek.Saturday) saturday = saturday.AddDays(1);
        var result = await service.ApplyLeave(new ActingUser(emp, "Employee"),
            new ApplyLeaveViewModel { FromDate = saturday, ToDate = saturday.AddDays(1), LeaveTypeId = type, Reason = "Test" });
        Assert.False(result.Success);
        Assert.Contains("weekdays", result.Message);
        db.Dispose();
    }

    [Fact]
    public async Task ApplyLeave_EmployeeCanSubmit()
    {
        var (db, service, emp, type) = await CreateAsync();
        var result = await service.ApplyLeave(new ActingUser(emp, "Employee"),
            new ApplyLeaveViewModel { FromDate = DateTime.Today.AddDays(10), ToDate = DateTime.Today.AddDays(12), LeaveTypeId = type, Reason = "Test" });
        Assert.True(result.Success);
        db.Dispose();
    }

    [Fact]
    public async Task Approve_EmployeeRole_Fails()
    {
        var (db, service, emp, type) = await CreateAsync();
        db.LeaveRequests.Add(new LeaveRequest { UserId = emp, LeaveTypeId = type, FromDate = DateTime.Today.AddDays(10),
            ToDate = DateTime.Today.AddDays(11), Days = 2, Reason = "Test", Status = "Pending" });
        await db.SaveChangesAsync();
        var requestId = (await db.LeaveRequests.SingleAsync()).Id;
        var result = await service.Approve(new ActingUser(emp, "Employee"), requestId, "");
        Assert.False(result.Success);
        db.Dispose();
    }

    [Fact]
    public async Task Approve_HrRole_SetsApprovedAndUpdatesUsed()
    {
        var (db, service, emp, type) = await CreateAsync();
        db.LeaveRequests.Add(new LeaveRequest { UserId = emp, LeaveTypeId = type, FromDate = DateTime.Today.AddDays(10),
            ToDate = DateTime.Today.AddDays(11), Days = 2, Reason = "Test", Status = "Pending" });
        await db.SaveChangesAsync();
        var request = await db.LeaveRequests.SingleAsync();
        var result = await service.Approve(new ActingUser("hr-1", "HR"), request.Id, "Approved");
        Assert.True(result.Success);
        Assert.Equal("Approved", (await db.LeaveRequests.SingleAsync()).Status);
        Assert.Equal(2, (await db.LeaveBalances.SingleAsync()).Used);
        db.Dispose();
    }

    [Fact]
    public async Task Approve_UserCannotApproveOwnLeave()
    {
        var (db, service, emp, type) = await CreateAsync();
        db.LeaveRequests.Add(new LeaveRequest { UserId = emp, LeaveTypeId = type, FromDate = DateTime.Today.AddDays(10),
            ToDate = DateTime.Today.AddDays(11), Days = 2, Reason = "Test", Status = "Pending" });
        await db.SaveChangesAsync();
        var request = await db.LeaveRequests.SingleAsync();

        var result = await service.Approve(new ActingUser(emp, "HR"), request.Id, "Approved");

        Assert.False(result.Success);
        Assert.Contains("own leave", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Pending", (await db.LeaveRequests.SingleAsync()).Status);
        Assert.Equal(0, (await db.LeaveBalances.SingleAsync()).Used);
        db.Dispose();
    }

    [Fact]
    public async Task Reject_HrRole_SetsRejected()
    {
        var (db, service, emp, type) = await CreateAsync();
        db.LeaveRequests.Add(new LeaveRequest { UserId = emp, LeaveTypeId = type, FromDate = DateTime.Today.AddDays(10),
            ToDate = DateTime.Today.AddDays(11), Days = 2, Reason = "Test", Status = "Pending" });
        await db.SaveChangesAsync();
        var request = await db.LeaveRequests.SingleAsync();
        var result = await service.Reject(new ActingUser("hr-1", "HR"), request.Id, "Not enough notice");
        Assert.True(result.Success);
        Assert.Equal("Rejected", (await db.LeaveRequests.SingleAsync()).Status);
        db.Dispose();
    }
}
