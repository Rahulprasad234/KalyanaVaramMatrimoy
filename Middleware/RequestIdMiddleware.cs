namespace KalyanaVaran.Api.Middleware;

/// <summary>
/// 1st in the pipeline. Reads X-Request-Id from the client (or creates one), stores it for logs and
/// meta.requestId, and echoes it back in the response header.
/// </summary>
public sealed class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public const string Header = "X-Request-Id";
    private const string ItemKey = "__RequestId";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) id = Guid.NewGuid().ToString();

        ctx.Items[ItemKey] = id;
        ctx.TraceIdentifier = id;
        ctx.Response.Headers[Header] = id;

        // Every log line written during this request carries RequestId
        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = id }))
        {
            await next(ctx);
        }
    }

    public static string Get(HttpContext ctx) =>
        ctx.Items.TryGetValue(ItemKey, out var v) && v is string s ? s : ctx.TraceIdentifier;
}
