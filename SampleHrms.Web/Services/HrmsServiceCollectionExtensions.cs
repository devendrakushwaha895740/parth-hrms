using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using SampleHrms.Web.Services.Agent;
using SampleHrms.Web.Data;
using SampleHrms.Web.Models;

namespace SampleHrms.Web.Services;

public static class HrmsServiceCollectionExtensions
{
    public static IServiceCollection AddHrmsServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=hrms.db";
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
        services.Configure<OpenRouterOptions>(configuration.GetSection("OpenRouter"));
        services.Configure<LlmOptions>(configuration.GetSection("Llm"));
        services.Configure<ParthOptions>(configuration.GetSection("Parth"));
        services.AddHttpClient<OpenRouterProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddTransient<ILlmProvider>(provider =>
        {
            var selected = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value.Provider;
            return selected.ToLowerInvariant() switch
            {
                "claude" => new ClaudeProvider(),
                "gemini" => new GeminiProvider(),
                _ => provider.GetRequiredService<OpenRouterProvider>()
            };
        });
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequiredLength = 6;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });
        services.AddScoped<ILeaveService, LeaveService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAgentSessionStore, AgentSessionStore>();
        services.AddSingleton<AgentRateLimiter>();
        services.AddScoped<IAgentClientGateway, SignalRAgentClientGateway>();
        services.AddScoped<IAgentAuditLogger, AgentAuditLogger>();
        services.AddScoped<IAgentIdentityResolver, AgentIdentityResolver>();
        services.AddScoped<IAgentToolExecutor, AgentToolExecutor>();
        services.AddSingleton<IAgentToolCatalog, AgentToolCatalog>();
        services.AddScoped<AgentOrchestrator>();
        return services;
    }

    public static async Task SeedHrmsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { "Admin", "HR", "Employee" })
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

        var types = new[] { ("Casual", 12), ("Sick", 10), ("Earned", 15) };
        foreach (var (name, balance) in types)
            if (!await db.LeaveTypes.AnyAsync(x => x.Name == name))
                db.LeaveTypes.Add(new LeaveType { Name = name, DefaultYearlyBalance = balance });
        await db.SaveChangesAsync();

        async Task<ApplicationUser> EnsureUser(string email, string name, string code, string dept)
        {
            var user = await users.FindByEmailAsync(email);
            if (user != null) return user;
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true,
                FullName = name, EmployeeCode = code, Department = dept, IsActive = true };
            var created = await users.CreateAsync(user, "Demo@123");
            if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(x => x.Description)));
            return user;
        }

        var admin = await EnsureUser("admin@demo.com", "Demo Admin", "ADM001", "Administration");
        var hr = await EnsureUser("hr@demo.com", "Demo HR", "HR001", "Human Resources");
        var employee = await EnsureUser("employee@demo.com", "Demo Employee", "EMP001", "Engineering");
        await users.AddToRoleAsync(admin, "Admin");
        await users.AddToRoleAsync(hr, "HR");
        await users.AddToRoleAsync(employee, "Employee");

        var year = DateTime.UtcNow.Year;
        var leaveTypes = await db.LeaveTypes.ToListAsync();
        foreach (var u in new[] { employee, hr, admin })
            foreach (var type in leaveTypes)
                if (!await db.LeaveBalances.AnyAsync(x => x.UserId == u.Id && x.LeaveTypeId == type.Id && x.Year == year))
                    db.LeaveBalances.Add(new LeaveBalance { UserId = u.Id, LeaveTypeId = type.Id, Year = year, Total = type.DefaultYearlyBalance });
        await db.SaveChangesAsync();

        if (!await db.LeaveRequests.AnyAsync(x => x.UserId == employee.Id))
        {
            var casual = leaveTypes.First(x => x.Name == "Casual");
            var sick = leaveTypes.First(x => x.Name == "Sick");
            db.LeaveRequests.AddRange(
                new LeaveRequest { UserId = employee.Id, LeaveTypeId = casual.Id, FromDate = DateTime.Today.AddDays(7),
                    ToDate = DateTime.Today.AddDays(8), Days = 2, Reason = "Personal work", Status = "Pending" },
                new LeaveRequest { UserId = employee.Id, LeaveTypeId = sick.Id, FromDate = DateTime.Today.AddDays(-20),
                    ToDate = DateTime.Today.AddDays(-20), Days = 1, Reason = "Doctor visit", Status = "Approved", ReviewedById = hr.Id, ReviewComment = "Approved" });
            var balance = await db.LeaveBalances.FirstAsync(x => x.UserId == employee.Id && x.LeaveTypeId == sick.Id && x.Year == year);
            balance.Used = 1;
        }
        await db.SaveChangesAsync();

        if (!await db.AttendanceRecords.AnyAsync(x => x.UserId == employee.Id))
        {
            for (var i = 29; i >= 0; i--)
            {
                var date = DateTime.Today.AddDays(-i);
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;
                db.AttendanceRecords.Add(new AttendanceRecord { UserId = employee.Id, Date = date,
                    CheckIn = new TimeSpan(9, i % 3 == 0 ? 15 : 0, 0), CheckOut = new TimeSpan(18, 0, 0), Status = "Present" });
            }
            await db.SaveChangesAsync();
        }
    }
}
