using Infrastructure.Database;

namespace API.Homologation;

internal static class OperationalEndpoints
{
    internal static void MapOperationalEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", (HttpContext context) => Status(context, true)).AllowAnonymous();
        app.MapGet("/health/ready", async (HttpContext context, MySqlConnectionFactory factory) =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                await using var connection = factory.Create();
                await connection.OpenAsync(timeout.Token);
                var journal = context.RequestServices.GetService<MigrationJournal>()
                    ?? new MigrationJournal(Path.Combine(AppContext.BaseDirectory, "database"));
                return Status(context, await journal.IsReadyAsync(connection, timeout.Token));
            }
            catch (Exception exception) when (exception is MySqlConnector.MySqlException
                or InvalidOperationException or OperationCanceledException or IOException)
            {
                return Status(context, false);
            }
        }).AllowAnonymous();
    }

    private static IResult Status(HttpContext context, bool healthy)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { status = healthy ? "ok" : "unavailable" },
            statusCode: healthy ? 200 : 503);
    }

    internal static void UseHomologationWeb(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; style-src 'self' 'unsafe-inline'";
            await next();
        });
        app.UseWhen(context => !IsReserved(context.Request.Path), branch => branch.UseStaticFiles());
        app.MapFallback(async context =>
        {
            if (IsReserved(context.Request.Path) || !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = 404;
                return;
            }
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"),
                context.RequestAborted);
        });
    }

    private static bool IsReserved(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/health");
}
