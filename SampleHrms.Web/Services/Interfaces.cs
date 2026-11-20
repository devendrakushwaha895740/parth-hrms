using SampleHrms.Web.ViewModels;

namespace SampleHrms.Web.Services;

public interface ILeaveService
{
    Task<Result> GetBalances(ActingUser actingUser, string? userId = null);
    Task<Result> ApplyLeave(ActingUser actingUser, ApplyLeaveViewModel model);
    Task<Result> GetHistory(ActingUser actingUser);
    Task<Result> GetPending(ActingUser actingUser);
    Task<Result> Approve(ActingUser actingUser, int requestId, string comment);
    Task<Result> Reject(ActingUser actingUser, int requestId, string comment);
    Task<Result> Cancel(ActingUser actingUser, int requestId);
    Task<Result> GetAll(ActingUser actingUser);
}

public interface IEmployeeService
{
    Task<Result> GetEmployees(ActingUser actingUser);
    Task<Result> CreateEmployee(ActingUser actingUser, string email, string fullName, string employeeCode, string department, string password, string role);
    Task<Result> SetActive(ActingUser actingUser, string userId, bool isActive);
    Task<Result> GetLeaveTypes(ActingUser actingUser);
    Task<Result> CreateLeaveType(ActingUser actingUser, string name, int balance);
    Task<Result> UpdateLeaveType(ActingUser actingUser, int id, string name, int balance, bool isActive);
    Task<Result> DeleteLeaveType(ActingUser actingUser, int id);
}

public interface IAttendanceService
{
    Task<Result> GetMyAttendance(ActingUser actingUser);
    Task<Result> GetAllAttendance(ActingUser actingUser);
    Task<Result> CorrectAttendance(ActingUser actingUser, string userId, DateTime date, TimeSpan? checkIn, TimeSpan? checkOut, string status);
}
