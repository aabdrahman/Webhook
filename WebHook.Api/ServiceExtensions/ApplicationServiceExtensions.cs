using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using System.Xml.XPath;
using WebHook.Api.ApplicationFilters;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.CustomHealthChecks;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.EventPublishers;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Api.ServiceExtensions;

/// <summary>
/// <see cref="IServiceCollection"/> extensions that register the API's services, workers and middleware options.
/// Called from <c>Program.cs</c>.
/// </summary>
internal static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registers OpenAPI document generation. Controller action summaries are read from this
    /// assembly's XML documentation file when it exists.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureSwagger(this IServiceCollection services)
    {
        //services.AddSwaggerGen(opts =>
        //{
        //    opts.SwaggerDoc("v1", new Microsoft.OpenApi()
        //    {
        //        Contact = new Microsoft.OpenApi.OpenApiContact() { Email = "akandeabdrahman@gmail.com", Name = "Akande Abdrahman", Url = new Uri("https://github.com/aabdrahman") },
        //        Description = "API for webhook implementation",
        //        Title = "Webhook API",
        //        Version = "v1",
        //        Summary = "Swager documentation for the webhook api"
        //    });
        //});

        var securityScheme = new OpenApiSecurityScheme()
        {
            Description = "Kindly enter your bearer token here in the format: Bearer <token>",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Scheme = "bearer",
            Type = SecuritySchemeType.Http,
            BearerFormat = "JWT",
            //Reference = new BaseOpenApiReference        // ← add this
            //{
            //    Type = ReferenceType.SecurityScheme,
            //    Id = "Bearer"
            //}
        };

        services.AddOpenApi(opts =>
        {
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

            if (File.Exists(xmlPath))
            {
                var xmlPathDocument = new XPathDocument(xmlPath);
                var navigator = xmlPathDocument.CreateNavigator();

                opts.AddOperationTransformer((operation, context, cancellationToken) =>
                {
                    if (context.Description.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor actionDescriptor)
                    {
                        var method = actionDescriptor.MethodInfo;

                        // Construct the exact XML path syntax for the method: M:Namespace.Class.Method
                        var methodNodePath = $"/doc/members/member[@name='M:{method.DeclaringType?.FullName}.{method.Name}']";
                        var methodNode = navigator.SelectSingleNode(methodNodePath);

                        if (methodNode != null)
                        {
                            // Fetch the <summary> block contents
                            var summaryNode = methodNode.SelectSingleNode("summary");
                            if (summaryNode != null)
                            {
                                // Clean up spacing and assign it to the OpenAPI Operation Summary
                                operation.Summary = summaryNode.Value.Trim();
                            }
                        }
                    }

                    return Task.CompletedTask;
                });
            }

            opts.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                // 1. Initialize Info if it's completely missing
                if (document.Info == null)
                {
                    document.Info = new OpenApiInfo();
                }

                document.Info.Title = "Webhook API";
                document.Info.Version = "v1";

                // 2. Safely check Components before touching Schemas or SecuritySchemes
                if (document.Components == null)
                {
                    document.Components = new OpenApiComponents();
                }

                // 3. Loop through Paths safely
                if (document.Paths != null)
                {
                    foreach (var path in document.Paths)
                    {
                        // Ensure operations exist before touching them
                        if (path.Value?.Operations == null) continue;

                        // Your transformation logic here...
                    }
                }

                return Task.CompletedTask;
            });

            //opts.AddDocumentTransformer((document, context, cancellationToken) =>
            //{
            //    // Define the Bearer Security Scheme
            //    var securityScheme = new OpenApiSecurityScheme
            //    {
            //        Type = SecuritySchemeType.Http,
            //        Scheme = JwtBearerDefaults.AuthenticationScheme, // "bearer"
            //        BearerFormat = "JWT",
            //        In = ParameterLocation.Header,
            //        Description = "Enter your JWT token in the format: Bearer {token}"
            //    };

            //    // Add the scheme to the document's components
            //    document.Components ??= new OpenApiComponents();
            //    document.Components.SecuritySchemes.Add(JwtBearerDefaults.AuthenticationScheme, securityScheme);

            //    // Apply a global security requirement to all endpoints
            //    var requirement = new OpenApiSecurityRequirement
            //    {
            //        [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>()
            //    };

            //    foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations.Values))
            //    {
            //        operation.Security.Add(requirement);
            //    }

            //    return Task.CompletedTask;
            //});
        });

        //services.AddSwaggerGen(opts =>
        //{

        //    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        //    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

        //    opts.IncludeXmlComments(xmlPath);

        //    opts.AddSecurityDefinition("Bearer", securityScheme);

        //});

    }

    /// <summary>
    /// Registers MassTransit with the in-memory transport. No consumers are currently registered;
    /// in-process messaging uses <see cref="Channel{T}"/> instead.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureMassTransit(this IServiceCollection services)
    {
        services.AddMassTransit(opts =>
        {



            opts.UsingInMemory((context, config) =>
            {

                config.ConfigureEndpoints(context);
            });
        });
    }

    /// <summary>
    /// Registers <see cref="RepositoryContext"/> against PostgreSQL using the <c>DbConnection</c>
    /// connection string, with dynamic JSON support.
    /// </summary>
    /// <remarks>
    /// Sensitive data and parameter logging are enabled, so SQL parameter values (including
    /// hashes and encrypted secrets) are written to the logs.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ArgumentNullException">The <c>DbConnection</c> connection string is missing.</exception>
    internal static void ConfigureDatabaseConnection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<RepositoryContext>(opts =>
        {
            // UseNpgsql is used to configure the DbContext to use PostgreSQL as the database provider. It retrieves the connection string from the configuration using the key "DbConnection".
            // If the connection string is not provided, it throws an ArgumentNullException with a message indicating that the database configuration string is not provided.
            //It uses the native supporter for json conversion

            // NpgsqlDataSourceBuilder is used to create a data source for PostgreSQL. It enables dynamic JSON support and parameter logging for better debugging and monitoring of database operations.
            var dataSource = new NpgsqlDataSourceBuilder(configuration.GetConnectionString("DbConnection") ?? throw new ArgumentNullException("Database configuration string not provided"));
            dataSource.EnableDynamicJson();
            dataSource.EnableParameterLogging();

            opts.UseNpgsql(dataSource.Build(), v => v.SetPostgresVersion(18, 0))
                .EnableSensitiveDataLogging()
                .LogTo(Serilog.Log.Information,
                        new[] { DbLoggerCategory.Database.Command.Name },
                        minimumLevel: LogLevel.Information,
                        options: Microsoft.EntityFrameworkCore.Diagnostics.DbContextLoggerOptions.SingleLine
                 );
        });
    }

    /// <summary>
    /// Binds each configuration model in <c>WebHook.Core.Entities.ConfigurationModels</c> to its
    /// <c>appsettings.json</c> section for use with <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    internal static void AddConfigurationModels(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SignatureSecretConfiguration>(configuration.GetSection("SignatureSecretKey"));
        services.Configure<WebhookDeliveryWorkerConfiguration>(configuration.GetSection("WebhookDeliveryWorker"));
        services.Configure<RetryDeliveresAfterFailedConfiguration>(configuration.GetSection("RetryDeliveriesAfterFailed"));
        services.Configure<EventRaisedWorkerConfiguration>(configuration.GetSection("EventRaisedWorker"));
        services.Configure<EmailSenderEmailSmtpSettingsConfiguration>(configuration.GetSection("EmailSmtpSettings"));
        services.Configure<PendingRaisedEventsWorkerConfiguration>(configuration.GetSection("PendingRaisedEventsWorker"));
        services.Configure<DeadLetterManualRetryConfiguration>(configuration.GetSection("DeadLetterManualRetry"));
        services.Configure<EmailProcessorWorkerConfiguration>(configuration.GetSection("EmailProcessorWorker"));
        services.Configure<JwtSettingsConfiguration>(configuration.GetSection("JwtSettings"));
        services.Configure<TokenValidationConfiguration>(configuration.GetSection("TokenValidation"));
        services.Configure<OtpSettingsConfiguration>(configuration.GetSection("OtpSettings"));
    }

    /// <summary>
    /// Registers the application and helper services, delivery processors, filters, middleware and
    /// the shared <see cref="WorkerLivenessTracker"/> (45-second heartbeat window).
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IWebhookEventCatalogService, WebhookEventCatalogService>();
        services.AddScoped<IWebhookSubscriptionService, WebhookSubscriptionService>();
        services.AddScoped<IWebhookEventService, WebhookEventService>();
        services.AddScoped<IWebhookSubscriptionEventService, WebhookSubscriptionEventService>();
        services.AddScoped<IDeadLetterQueueService, DeadLetterQueueService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IAuthenticatedUserDetails, AuthenticatedUserDetails>();
        services.AddScoped<IWebhookServiceClientService, WebhookServiceClientService>();
        services.AddScoped<IWebhookServiceClientCatalogService, WebhookServiceClientCatalogService>();
        services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
        services.AddScoped<IWebhookDeliveryAttemptService, WebhookDeliveryAttemptService>();

        services.AddScoped<ISecretKeyGenerator, SecretKeyGeneratorService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<ISignatureService, SignatureService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IOtpGenerator, OtpGenerator>();
        services.AddScoped<IApplicationHasher, ApplicationHasher>();
        services.AddScoped<ICacheService, CacheService>();

        services.AddSingleton<IApplicationPublisher, ApplicationPublisher>();
        services.AddSingleton<EmailContentFormatterHelper>();

        services.AddScoped<WebhookDeliveryRetryAfterService>();
        services.AddScoped<WebhookDeliveryProcessorService>();
        services.AddScoped<RetryAfterPendingService>();
        services.AddScoped<StaleClaimedDeliveryReleaseService>();

        services.AddScoped<CustomAuthenticationFilter>();
        services.AddScoped<ClientValidationFilter>();

        services.AddScoped<IReportingAnalyticsService, ReportingAnalyticsService>();

        services.AddSingleton(new WorkerLivenessTracker(TimeSpan.FromSeconds(45)));

        services.AddScoped<RequestOtpEmailExtractionMiddleware>();
        services.AddScoped<ValidateOtpEmailExtractionMiddleware>();
    }

    /// <summary>
    /// Registers the singleton bounded channels for raised events (capacity 1,000) and outgoing
    /// emails (capacity 500). Both have a single reader and wait when full.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureApplicationChannels(this IServiceCollection services)
    {
        //Chnanel for raised events
        services.AddSingleton(_ =>
        {
            return Channel.CreateBounded<EventRaised>(new BoundedChannelOptions(1000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        });

        //Channel for sending email
        services.AddSingleton(_ =>
        {
            return Channel.CreateBounded<EmailSenderDto>(new BoundedChannelOptions(500)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        });
    }

    /// <summary>
    /// Registers the background workers: event fan-out, pending event recovery, first delivery,
    /// retry, stale claim release and email sending.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureApplicationWorkers(this IServiceCollection services)
    {
        services.AddHostedService<EventRaisedWorker>();
        services.AddHostedService<PendingRaisedEventsWorker>();
        services.AddHostedService<WebhookDeliveryProcessorWorker>();
        //services.AddHostedService<WebhookLongerPendingServiceBackground>();
        services.AddHostedService<RetryPendingDeliveriesWorker>();
        services.AddHostedService<StaleClaimedDeliverReleaseWorker>();
        services.AddHostedService<EmailProcessorWorker>();
    }

    /// <summary>
    /// Registers the named <c>WebhookDeliveryClient</c> HTTP client used to post deliveries, with a
    /// 30-second timeout and at most 10 connections per server.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureHttpClient(this IServiceCollection services)
    {
        //This adds teh webhook delivery named httpclient without the base address wich will be injected at teh point of calling the api.
        services.AddHttpClient("WebhookDeliveryClient", opts =>
        {
            opts.Timeout = TimeSpan.FromSeconds(30);

        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler()
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            MaxConnectionsPerServer = 10,
            PooledConnectionLifetime = TimeSpan.FromSeconds(120),
            EnableMultipleHttp2Connections = true
        });
    }

    /// <summary>
    /// Registers ASP.NET Core Identity for <see cref="User"/> and <see cref="Role"/>.
    /// </summary>
    /// <remarks>
    /// Passwords need a digit, lower- and upper-case letters and a symbol, and a minimum length from
    /// <c>UserSettingsConfiguration:MinimumPasswordLength</c>. After
    /// <c>UserSettingsConfiguration:MaximumAuthenticationAttempt</c> failed attempts a user is locked
    /// out indefinitely, until an admin or a password reset clears it.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ArgumentNullException">Either user setting is missing or zero.</exception>
    internal static void ConfigureIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        int minPasswordLength = configuration.GetValue<int>("UserSettingsConfiguration:MinimumPasswordLength");
        var maxFailedAuthenticationAttempt = configuration.GetValue<int>("UserSettingsConfiguration:MaximumAuthenticationAttempt");

        if (minPasswordLength == 0 || minPasswordLength == default(int))
        {
            throw new ArgumentNullException("Cannot proceeed as the maximum password length is not yet defined.");
        }

        if (maxFailedAuthenticationAttempt == 0 || maxFailedAuthenticationAttempt == default(int))
        {
            throw new ArgumentNullException("Cannot proceed as the maximum failed authentication attemot is not defined yet.");
        }

        services.AddIdentity<User, Role>(opts =>
        {
            //Password setttings configuration
            opts.Password.RequireDigit = true;
            opts.Password.RequireLowercase = true;
            opts.Password.RequireUppercase = true;
            opts.Password.RequireNonAlphanumeric = true;
            opts.Password.RequiredLength = minPasswordLength;

            //User configuration
            opts.User.RequireUniqueEmail = true;


            //Signin configuration for user
            opts.SignIn.RequireConfirmedEmail = false;
            opts.SignIn.RequireConfirmedAccount = false;
            opts.SignIn.RequireConfirmedPhoneNumber = false;

            //Lockout settings configuration
            opts.Lockout.MaxFailedAccessAttempts = maxFailedAuthenticationAttempt;
            opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromDays(36525); //Maximum possible timespan. This ensures users are locked ou indefinitely 
            opts.Lockout.AllowedForNewUsers = true; //This ensures that new users are created with lockout enabled.

        })
        .AddEntityFrameworkStores<RepositoryContext>()
        .AddDefaultTokenProviders();
    }

    /// <summary>
    /// Registers JWT bearer authentication. Tokens are validated against the configured issuer and
    /// audiences with no clock skew, using the key in the <c>webhook_secret_key</c> environment variable.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ArgumentNullException">The signing key or valid issuer is not configured.</exception>
    internal static void ConfigureApplicationJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtConfigSetings = configuration.GetSection("JwtSettings");
        var applicationSecretKey = Environment.GetEnvironmentVariable("webhook_secret_key") ?? throw new ArgumentNullException("Webhook Jwt Secret key is not yet defined. Key to define with: 'webhook_secret_key'. ");

        services.AddAuthentication(opts =>
        {
            opts.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opts.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(opts =>
        {
            TokenValidationParameters tokenValidationParameter = new TokenValidationParameters()
            {
                //Token validationn properties
                ClockSkew = TimeSpan.Zero,
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,

                //Token issuing values
                ValidAudiences = jwtConfigSetings["ValidAudiences"]?.Split(";", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                ValidIssuer = jwtConfigSetings["ValidIssuer"] ?? throw new ArgumentNullException("Valid Issuer is not yet defined for the application in the appsettings file."),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(applicationSecretKey))
            };

            opts.TokenValidationParameters = tokenValidationParameter;
        });
    }

    /// <summary>Registers authorization with the default options. Endpoints use role-based <c>[Authorize]</c> attributes.</summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureApplicationAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(opts =>
        {

        });
    }

    /// <summary>Registers <see cref="GlobalExceptionHandler"/> to turn unhandled exceptions into a standard error response.</summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureApplicationGlobalExceptionHandler(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
    }

    /// <summary>
    /// Registers ASP.NET Core Data Protection, used to protect OTP operation tokens. Keys use the
    /// default storage, so tokens issued by one instance may not be readable by another.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void AddDataProtectionService(this IServiceCollection services)
    {
        services.AddDataProtection(opts =>
        {

        });
    }

    /// <summary>Registers the in-memory cache with a size limit of 268,435,456 units.</summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureApplicationCaching(this IServiceCollection services)
    {
        services.AddMemoryCache(opts =>
        {
            opts.SizeLimit = 256 * 1024 * 1024;

        });
    }

    /// <summary>
    /// Registers the PostgreSQL health check and the custom checks in
    /// <c>WebHook.Infrastructure.CustomHealthChecks</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <exception cref="ArgumentNullException">The <c>DbConnection</c> connection string is missing.</exception>
    internal static void ConfigureHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(connectionString: configuration.GetConnectionString("DbConnection") ?? throw new ArgumentNullException("Db Connection string cannot be empty."), timeout: TimeSpan.FromSeconds(10),
                        failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, name: "POSTGRES DB HEALTH CHECK")
            .AddCheck<InMemoryCacheHealthCheck>(name: "IN MEMORY SYSTEM CACHE HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["cache"])
            .AddCheck<C_DriveHealthCheck>(name: "SERVER DISK SPACE HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["Disk Space"])
            .AddCheck<QueuedEmailHealthCheck>(name: "QUEUED EMAIL HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["channels", "email"])
            .AddCheck<RaisedEventChannelHealthCheck>(name: "QUEUED RAISED EVENTS HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["channels", "raised  events"])
            .AddCheck<DeadLetterQueuedHealthCheck>(name: "DEAD LETTER DELIVERIES HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["operation", "dead letter"])
            .AddCheck<PendingDeliveriesHealthCheck>(name: "PENDING DELIVERIES HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["operation", "pending deliveries"])
            .AddCheck<WorkerLivelinessHealthCheck>(name: "WORKER LIVELINESS HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["operation", "worker liveliness"])
            .AddCheck<StaleDeliveryHealthCheck>(name: "STALE DELIVERY HEALTH CHECK", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, tags: ["operation", "stale deliveries"]);

        //services.AddHealthChecksUI(opts =>
        //{
        //    opts.AddHealthCheckEndpoint(name: "Webhook System Health Check", uri: "/admin/_healths");
        //    opts.SetEvaluationTimeInSeconds(600);
        //}).AddInMemoryStorage();
    }

    /// <summary>
    /// Registers CORS policies: a default policy that allows any origin, method and header, and a
    /// <c>RestrictedPolicy</c> built from the <c>CorsPolicy</c> configuration section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    internal static void ConfigureApplicationCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var corsConfig = configuration.GetSection("CorsPolicy");
        services.AddCors(opts =>
        {
            opts.AddDefaultPolicy(config =>
            {
                config.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
            });

            opts.AddPolicy("RestrictedPolicy", config =>
            {
                config.WithOrigins(corsConfig["AllowedOrigins"]?.Split(";", StringSplitOptions.TrimEntries) ?? [])
                    .WithMethods(corsConfig["AllowedMethods"]?.Split(",", StringSplitOptions.TrimEntries) ?? [])
                    //.WithHeaders(corsConfig["AllowedHeaders"]?.Split(",", StringSplitOptions.TrimEntries) ?? [])
                    .AllowAnyHeader()
                    .WithExposedHeaders(corsConfig["AllowedHeaders"]?.Split(",", StringSplitOptions.TrimEntries) ?? []);
            });
        });
    }

    /// <summary>
    /// Registers the rate limiting policies, which reject excess requests with <c>429</c> and a
    /// <see cref="GenericResponse{T}"/> body.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><description><c>request-otp-limit</c> — 3 requests per 10 minutes per email address (or IP address).</description></item>
    ///   <item><description><c>validate-otp-limit</c> — 5 requests per 10 minutes per email address (or IP address).</description></item>
    ///   <item><description><c>per-user-rating</c> — 10 requests per minute per user ID (or IP address when anonymous).</description></item>
    /// </list>
    /// The email address partitions are populated by the OTP email extraction middleware.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureApplicationRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            opts.AddPolicy(policyName: "request-otp-limit", context =>
            {
                string ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string? emailFromRequuest = context.Items["otp-request-email"] as string; //This is set by a custom middleware
                var partitionKey = emailFromRequuest ?? ipAddress;

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey: partitionKey, _ => new FixedWindowRateLimiterOptions()
                {
                    Window = TimeSpan.FromMinutes(10),
                    PermitLimit = 3,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            opts.AddPolicy(policyName: "validate-otp-limit", context =>
            {
                string ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string? emailFromRequuest = context.Items["otp-validation-email"] as string; //This is set by a custom middleware
                var partitionKey = emailFromRequuest ?? ipAddress;

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey: partitionKey, _ => new FixedWindowRateLimiterOptions()
                {
                    Window = TimeSpan.FromMinutes(10),
                    PermitLimit = 5,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            opts.AddPolicy(policyName: "per-user-rating", context =>
            {
                string userId = context.User.FindFirstValue(claimType: ClaimTypes.NameIdentifier) ?? "";

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions()
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(60),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        SegmentsPerWindow = 1
                    });
                }

                string ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions()
                {
                    Window = TimeSpan.FromSeconds(60),
                    PermitLimit = 10,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });

            });

            opts.OnRejected = async (context, ct) =>
            {
                var defaultResponse = GenericResponse<object>.Failure(null, "Too many requests.", System.Net.HttpStatusCode.TooManyRequests);

                if (context.Lease.TryGetMetadata(metadataName: MetadataName.RetryAfter, out var retryAfterValue))
                {
                    var response = GenericResponse<object>.Failure(null, $"Too many requests. Kindly retry again after {retryAfterValue.TotalSeconds} seconds.", System.Net.HttpStatusCode.TooManyRequests);
                    await context.HttpContext.Response.WriteAsJsonAsync(response, ct);
                }
                else
                {
                    await context.HttpContext.Response.WriteAsJsonAsync(defaultResponse, ct);
                }
            };
        });
    }

    /// <summary>
    /// Configures forwarded headers so the client IP address and scheme are taken from
    /// <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> when running behind a proxy.
    /// </summary>
    /// <param name="services">The service collection.</param>
    internal static void ConfigureForwardHeaders(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(opts =>
        {
            opts.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        });
    }
}
