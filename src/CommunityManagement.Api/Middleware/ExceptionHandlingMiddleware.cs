using CommunityManagement.Application.Common;
using System.Text.Json;

namespace CommunityManagement.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new { error = ex.Message });
            await context.Response.WriteAsync(body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beklenmeyen hata.");
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new { error = "Sunucu hatası." });
            await context.Response.WriteAsync(body);
        }
    }
}
