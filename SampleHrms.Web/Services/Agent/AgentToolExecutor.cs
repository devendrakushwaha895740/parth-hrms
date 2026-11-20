using System.Text.Json;
using SampleHrms.Web.Services;

namespace SampleHrms.Web.Services.Agent;

public interface IAgentToolExecutor
{
    Task<string> ExecuteServerToolAsync(ActingUser actingUser, string name, JsonElement arguments);
}

public sealed class AgentToolExecutor : IAgentToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILeaveService _leaves;
    private readonly IEmployeeService _employees;

    public AgentToolExecutor(ILeaveService leaves, IEmployeeService employees)
    {
        _leaves = leaves;
        _employees = employees;
    }

    public async Task<string> ExecuteServerToolAsync(ActingUser actingUser, string name, JsonElement arguments)
    {
        Result result = name switch
        {
            "get_leave_balances" => await _leaves.GetBalances(actingUser),
            "get_my_leave_history" => await _leaves.GetHistory(actingUser),
            "list_pending_leaves" => await _leaves.GetPending(actingUser),
            "list_employees" => await _employees.GetEmployees(actingUser),
            _ => Result.Fail("Unknown server tool.")
        };
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
