namespace HVTradingBot.Dashboard;

/// <summary>Outcome of a dashboard action, independent of how it is delivered (HTTP in the API, the app's bridge on Android).</summary>
public abstract record DashboardResult
{
    public static DashboardResult Ok(object? value) => new OkResult(value);

    public static readonly DashboardResult NoContent = new NoContentResult();

    public static readonly DashboardResult NotFound = new NotFoundResult();

    public static DashboardResult Invalid(IDictionary<string, string[]> errors) => new InvalidResult(errors);

    public static DashboardResult Invalid(string field, string message) => new InvalidResult(new Dictionary<string, string[]> { [field] = [message] });

    public sealed record OkResult(object? Value) : DashboardResult;

    public sealed record NoContentResult : DashboardResult;

    public sealed record NotFoundResult : DashboardResult;

    /// <summary>Validation errors per field, shown next to the form (ValidationProblem in HTTP).</summary>
    public sealed record InvalidResult(IDictionary<string, string[]> Errors) : DashboardResult;
}

/// <summary>Who made a dashboard request, for the audit log.</summary>
public sealed record DashboardCaller(string Actor, string? CorrelationId);
