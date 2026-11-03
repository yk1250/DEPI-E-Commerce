using Clinic.API.Middleware;
using Clinic.Application.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Clinic.Tests;

public class GlobalExceptionHandlerTests
{
    private static async Task<(DefaultHttpContext Context, string Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context, body);
    }

    [Fact]
    public async Task NotFound_maps_to_404()
    {
        var (context, body) = await HandleAsync(new NotFoundException("Doctor", 5));

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Contains("Doctor (5) was not found.", body);
    }

    [Fact]
    public async Task Unauthorized_maps_to_401_with_a_bearer_challenge()
    {
        var (context, _) = await HandleAsync(new UnauthorizedException("Invalid email or password."));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers["WWW-Authenticate"].ToString());
    }

    [Fact]
    public async Task Forbidden_maps_to_403()
    {
        var (context, _) = await HandleAsync(new ForbiddenException());

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task Conflict_maps_to_409()
    {
        var (context, _) = await HandleAsync(new ConflictException("duplicate"));

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Concurrency_failure_maps_to_409()
    {
        var (context, _) = await HandleAsync(new DbUpdateConcurrencyException("row version changed"));

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Validation_maps_to_400_and_returns_the_errors()
    {
        var errors = new Dictionary<string, string[]> { ["items"] = ["Item 0: endTime must be after startTime."] };

        var (context, body) = await HandleAsync(new AppValidationException(errors));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Contains("\"errors\"", body);
        Assert.Contains("endTime must be after startTime", body);
    }

    [Fact]
    public async Task Unknown_exception_maps_to_500_without_leaking_details()
    {
        var (context, body) = await HandleAsync(new InvalidOperationException("password=SuperSecret123"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("SuperSecret123", body);
        Assert.Contains("An unexpected error occurred.", body);
    }
}
