using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebhookServices;

// =========================================================================
// 1. INFRASTRUCTURE LAYER: THE BACKGROUND SERVICE WORKER
// =========================================================================
public sealed class WebhookLongerPendingServiceBackground : BackgroundService
{
    private readonly ILogger<WebhookLongerPendingServiceBackground> _logger;
    private readonly IServiceProvider _serviceProvider; // Used to create isolated scopes inside the loop

    public WebhookLongerPendingServiceBackground(
        ILogger<WebhookLongerPendingServiceBackground> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Webhook Background Worker. Lifecycle state: {MethodName}", nameof(StartAsync));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Entering background execution loop frame: {MethodName}", nameof(ExecuteAsync));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // CREATE AN ISOLATED SCOPE PER LOOP ITERATION
                using (IServiceScope scope = _serviceProvider.CreateScope())
                {
                    _logger.LogDebug("Processing isolated namespace operation scope starting...");

                    // Example of instantiating pure local domain models safely inside the loop frame
                    IProduct processingItem = new ClassA();
                    Manager localAssignedManager = new Manager("System Automator");

                    // Execute domain behaviors
                    localAssignedManager.PrintName();

                    // NOTE: If you need a database context or scoped repository, resolve it here:
                    // var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                }
                // Scope cleanly terminates here. All resources are marked for Garbage Collection immediately.

                // Throttles execution loop safely to prevent high CPU usage
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("System cancellation requested. Safely breaking the background loop pipeline.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled critical fault occurred inside the background execution thread.");
            }
        }
    }
}

// =========================================================================
// 2. DOMAIN LAYER: CORE MODELS AND BUSINESS INTERFACES
// =========================================================================
public abstract class BaseEmployee
{
    protected string EmployeeName { get; init; }

    protected BaseEmployee(string name)
    {
        EmployeeName = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Employee name cannot be null or empty.", nameof(name))
            : name;
    }
}

public class Manager : BaseEmployee
{
    public Manager(string name) : base(name) { }

    public void PrintName()
    {
        Console.WriteLine($"Manager Context Name Reference: {EmployeeName}");
    }
}

public interface IProduct { }

public class ClassA : IProduct { }
