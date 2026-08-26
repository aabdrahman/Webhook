using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Provides administrative endpoints for monitoring the health and
/// operational status of the WebhookHub API and its dependencies.
/// </summary>
[Route("[controller]")]
[ApiController]
[AllowAnonymous]
public class AdminController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminController"/> class.
    /// </summary>
    /// <param name="healthCheckService">
    /// The ASP.NET Core health check service used to evaluate the status
    /// of all registered health checks.
    /// </param>
    public AdminController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Returns the health status of the API and its dependencies.
    /// </summary>
    /// <remarks>
    /// Evaluates all registered health checks and returns a consolidated
    /// status report. Registered checks include:
    /// <list type="bullet">
    ///   <item><description><b>PostgreSQL</b> — verifies the primary database connection is responsive.</description></item>
    ///   <item><description><b>Email Queue</b> — verifies the in-memory email channel is not growing beyond its threshold.</description></item>
    ///   <item><description><b>Event Raised Queue</b> — verifies the in-memory pending raised events channel is not growing beyond its threshold.</description></item>
    ///   <item><description><b>Webhook dead letter</b> — verifies that the deliveries are not dropping to dead letter incessantly.</description></item>
    ///   <item><description><b>Webhook pending deliveries</b> —checks and displays the total pending deliveries currently in the database.</description></item>
    ///   <item><description><b>System C Drive space</b> — verifies that the application is running on enough disk space(C Drive).</description></item>
    ///   <item><description><b>Application In Memory Cache</b> — verifies that the application cache is running optimally ad system can set new cache item, retrive cache with key and remove successfully.</description></item>
    /// </list>
    ///
    /// The overall status reflects the worst result across all individual checks:
    /// <list type="bullet">
    ///   <item><description><b>Healthy</b> — all checks passed. Returns <c>200 OK</c>.</description></item>
    ///   <item><description><b>Degraded</b> — one or more checks are degraded but the service is still operational. Returns <c>200 OK</c>.</description></item>
    ///   <item><description><b>Unhealthy</b> — one or more checks failed. Returns <c>503 Service Unavailable</c>.</description></item>
    /// </list>
    ///
    /// Each entry in the <c>checks</c> array includes the check name, its individual
    /// status, the time taken to evaluate it in milliseconds, and the exception
    /// message if the check threw an error.
    /// </remarks>
    /// <returns>
    /// A <see cref="HealthCheckResponse"/> containing the overall status, individual
    /// check results, and total evaluation duration.
    /// </returns>
    /// <response code="200">All health checks passed or are degraded — service is operational.</response>
    /// <response code="503">One or more health checks are unhealthy — service may be impaired.</response>
    [HttpGet("_health")]
    [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHealthStatus()
    {
        HealthReport report = await _healthCheckService.CheckHealthAsync();

        var response = new HealthCheckResponse(
            Status: report.Status.ToString(),
            Checks: report.Entries.Select(e => new HealthCheckEntry(Name: e.Key, Status: e.Value.Status.ToString(), Duration: e.Value.Duration.TotalMilliseconds, 
                                                                    Exception: e.Value.Exception?.Message, description: e.Value.Description, e.Value.Tags.ToList())),
            TotalDurationMs: report.TotalDuration.TotalMilliseconds);

        return report.Status == HealthStatus.Unhealthy
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, response)
            : Ok(response);
    }
}

// -------------------------------------------------------------------------
// Response DTOs
// -------------------------------------------------------------------------

/// <summary>
/// Represents the consolidated health status of the API returned by
/// <see cref="AdminController.GetHealthStatus"/>.
/// </summary>
/// <param name="Status">
/// The overall health status of the API. One of <c>Healthy</c>,
/// <c>Degraded</c>, or <c>Unhealthy</c>.
/// </param>
/// <param name="Checks">
/// A collection of individual health check results, one entry per
/// registered check.
/// </param>
/// <param name="TotalDurationMs">
/// The total time in milliseconds taken to evaluate all health checks.
/// </param>
public sealed record HealthCheckResponse(string Status, IEnumerable<HealthCheckEntry> Checks, double TotalDurationMs);

/// <summary>
/// Represents the result of a single health check evaluation.
/// </summary>
/// <param name="Name">
/// The registered name of the health check (e.g. <c>postgresql</c>,
/// <c>email-queue</c>).
/// </param>
/// <param name="Status">
/// The status of this individual check. One of <c>Healthy</c>,
/// <c>Degraded</c>, or <c>Unhealthy</c>.
/// </param>
/// <param name="Duration">
/// The time in milliseconds taken to evaluate this check.
/// </param>
/// <param name="Exception">
/// The exception message if this check threw an error during evaluation,
/// or <c>null</c> if the check completed without error.
/// </param>
/// <param name="description">
/// The description of the result
/// </param>
/// <param name="tags">
/// The assigned tags for the health check
/// </param>
public sealed record HealthCheckEntry(string Name, string Status, double Duration, string? Exception, string? description, List<string>? tags);