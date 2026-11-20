using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SampleHrms.Web.Models;

namespace SampleHrms.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<LeaveBalance>().HasIndex(x => new { x.UserId, x.LeaveTypeId, x.Year }).IsUnique();
        builder.Entity<LeaveRequest>().Property(x => x.Status).HasMaxLength(20);
        builder.Entity<AttendanceRecord>().HasIndex(x => new { x.UserId, x.Date }).IsUnique();
    }
}
