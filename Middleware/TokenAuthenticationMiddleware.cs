namespace UserManagementAPI.Middleware;

// Validates a bearer token on incoming requests to protected API endpoints.
public class TokenAuthenticationMiddleware
{
    private const string AuthorizationHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly ILogger<TokenAuthenticationMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public TokenAuthenticationMiddleware(RequestDelegate next, ILogger<TokenAuthenticationMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only protect the API surface; allow root, health, and OpenAPI docs through unauthenticated.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var validToken = _configuration["Authentication:ApiToken"];

        if (string.IsNullOrWhiteSpace(validToken))
        {
            _logger.LogError("Authentication:ApiToken is not configured; rejecting request.");
            await WriteUnauthorizedAsync(context, "Authentication is not configured.");
            return;
        }

        var header = context.Request.Headers[AuthorizationHeader].ToString();

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected request to {Path}: missing or malformed Authorization header.", context.Request.Path);
            await WriteUnauthorizedAsync(context, "Missing or invalid Authorization header.");
            return;
        }

        var token = header[BearerPrefix.Length..].Trim();

        if (!string.Equals(token, validToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Rejected request to {Path}: invalid token.", context.Request.Path);
            await WriteUnauthorizedAsync(context, "Invalid token.");
            return;
        }

        await _next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}

public static class TokenAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseTokenAuthentication(this IApplicationBuilder app)
        => app.UseMiddleware<TokenAuthenticationMiddleware>();
}
