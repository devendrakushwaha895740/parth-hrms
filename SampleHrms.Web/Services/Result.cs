namespace SampleHrms.Web.Services;

public class Result
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public object? Data { get; init; }
    public static Result Ok(string message = "", object? data = null) => new() { Success = true, Message = message, Data = data };
    public static Result Fail(string message) => new() { Success = false, Message = message };
}
