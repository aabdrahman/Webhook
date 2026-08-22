using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using System.Xml.XPath;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.EventPublishers;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Api.ServiceExtensions;

internal static class ApplicationServiceExtensions
{
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
        services.AddScoped<IAuthenticatedUserDetails,  AuthenticatedUserDetails>();

        services.AddScoped<ISecretKeyGenerator, SecretKeyGeneratorService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<ISignatureService, SignatureService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IOtpGenerator, OtpGenerator>();
        services.AddScoped<IApplicationHasher, ApplicationHasher>();

        services.AddSingleton<IApplicationPublisher, ApplicationPublisher>();
        services.AddSingleton<EmailContentFormatterHelper>();

        services.AddScoped<WebhookDeliveryRetryAfterService>();
        services.AddScoped<WebhookDeliveryProcessorService>();
        services.AddScoped<RetryAfterPendingService>();
        services.AddScoped<StaleClaimedDeliveryReleaseService>();
    }

    internal static void ConfigureApplicationChannels(this IServiceCollection services)
    {
        //Chnanel for raised events
        services.AddSingleton(_ =>
        {
            return Channel.CreateUnbounded<EventRaised>(new UnboundedChannelOptions()
            {
                SingleReader = true,
                SingleWriter = false

            });
        });

        //Channel for sending email
        services.AddSingleton(_ =>
        {
            return Channel.CreateUnbounded<EmailSenderDto>(new UnboundedChannelOptions()
            {
                SingleReader = true,
                SingleWriter = false
            });
        });
    }

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

    internal static void ConfigureApplicationAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(opts =>
        {

        });
    }

    internal static void ConfigureApplicationGlobalExceptionHandler(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
    }

    internal static void AddDataProtectionService(this IServiceCollection services)
    {
        services.AddDataProtection(opts =>
        {

        });
    }

    internal static void ConfigureApplicationCaching(this IServiceCollection services)
    {
        services.AddMemoryCache(opts =>
        {
            opts.SizeLimit = 2 * 1024 * 1024;
            
        });
    }
}
