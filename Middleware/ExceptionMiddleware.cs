using KalyanaVaran.Api.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace KalyanaVaran.Api.Middleware;

/// <summary>Catches every exception and turns it into the standard error envelope.</summary>
public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IWebHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex) when (!ctx.Response.HasStarted)
        {
            await ApiEnvelopeWriter.WriteErrorAsync(ctx, ex.Status, ex.Code, ex.Message, ex.Details);
        }
        catch (BadHttpRequestException ex) when (!ctx.Response.HasStarted)
        {
            var tooLarge = ex.StatusCode == StatusCodes.Status413PayloadTooLarge;
            await ApiEnvelopeWriter.WriteErrorAsync(ctx, ex.StatusCode,
                tooLarge ? ErrorCodes.PayloadTooLarge : ErrorCodes.ValidationError,
                tooLarge ? "The uploaded file is too large." : "The request could not be read.");
        }
        catch (InvalidDataException) when (!ctx.Response.HasStarted)
        {
            // Multipart body/section limits exceeded
            await ApiEnvelopeWriter.WriteErrorAsync(ctx, 413, ErrorCodes.PayloadTooLarge, "The uploaded file is too large.");
        }
        catch (DbUpdateException ex) when (!ctx.Response.HasStarted &&
                                           ex.InnerException is SqlException { Number: 2601 or 2627 }) // unique index / constraint
        {
            await ApiEnvelopeWriter.WriteErrorAsync(ctx, 409, ErrorCodes.Conflict, "This record already exists.");
        }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
        {
            // client went away; nothing to write
        }
        catch (Exception ex) when (!ctx.Response.HasStarted)
        {
            logger.LogError(ex, "Unhandled error for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

            // Development only: show the real cause in Scalar (never in production)
            IDictionary<string, string>? details = null;
            if (env.IsDevelopment())
            {
                var root = ex.GetBaseException();
                details = new Dictionary<string, string>
                {
                    ["exception"] = $"{ex.GetType().Name}: {ex.Message}",
                    ["rootCause"] = $"{root.GetType().Name}: {root.Message}",
                    ["at"] = ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("KalyanaVaran"))?.Trim() ?? ""
                };
            }
            await ApiEnvelopeWriter.WriteErrorAsync(ctx, 500, ErrorCodes.InternalError,
                "Something went wrong on our side. Please try again.", details);
        }
    }
}
