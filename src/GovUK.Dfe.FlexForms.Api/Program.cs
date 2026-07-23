using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using GovUK.Dfe.FlexForms.Api.ExceptionHandlers;
using GovUK.Dfe.FlexForms.Api.Filters;
using GovUK.Dfe.FlexForms.Api.Middleware;
using GovUK.Dfe.FlexForms.Api.Security;
using GovUK.Dfe.FlexForms.Api.Swagger;
using GovUK.Dfe.CoreLibs.Http.Extensions;
using GovUK.Dfe.CoreLibs.Http.Interfaces;
using GovUK.Dfe.CoreLibs.Http.Middlewares.CorrelationId;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using GovUK.Dfe.CoreLibs.Messaging.MassTransit.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.FeatureManagement;
using NetEscapades.AspNetCore.SecurityHeaders;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using GovUK.Dfe.FlexForms.Api.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Tenancy;
using GovUK.Dfe.FlexForms.Domain.Caching;
using GovUK.Dfe.FlexForms.Infrastructure.Caching;
using GovUK.Dfe.FlexForms.Infrastructure.Database;
using GovUK.Dfe.FlexForms.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using TelemetryConfiguration = Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration;

namespace GovUK.Dfe.FlexForms.Api
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Initial Serilog config with console only
            // App Insights sink is added AFTER builder.Build() when TelemetryConfiguration is available
            builder.Host.UseSerilog((context, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .WriteTo.Console();
            });

            builder.Services.AddControllers(opts =>
                {
                    opts.Filters.Add<ResultToExceptionFilter>();
                })
                .AddJsonOptions(c =>
                {
                    c.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                    c.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    c.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                })
                .ConfigureApiBehaviorOptions(options =>
                {
                    // Disable automatic model validation to let MediatR ValidationBehaviour handle it
                    options.SuppressModelStateInvalidFilter = true;
                });

            builder.Services.AddApiVersioning(config =>
            {
                config.DefaultApiVersion = new ApiVersion(1, 0);
                config.AssumeDefaultVersionWhenUnspecified = true;
                config.ReportApiVersions = true;

            }).AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

            builder.Services.AddSwaggerGen(c =>
            {
                string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
                c.EnableAnnotations();
            });

            
            // Decorate IDistributedCache with tenant-aware wrapper
            // This ensures all cache operations are automatically scoped to the current tenant
            builder.Services.AddScoped<ITenantAwareDistributedCache>(sp =>
            {
                var innerCache = sp.GetRequiredService<IDistributedCache>();
                var tenantAccessor = sp.GetRequiredService<ITenantContextAccessor>();
                var logger = sp.GetRequiredService<ILogger<TenantAwareDistributedCache>>();
                return new TenantAwareDistributedCache(innerCache, tenantAccessor, logger);
            });

            builder.Services.ConfigureOptions<SwaggerOptions>();
            builder.Services.AddFeatureManagement();
            builder.Services.AddHttpContextAccessor();

            // Always register TenantConfigDbContext and encryptor (needed for seeding even in AppSettings mode)
            builder.Services.AddDbContext<TenantConfigDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("TenantConfigDatabase")));

            // Shared Data Protection key ring for secret TenantSettings (local keys in Local/Test;
            // Azure Blob + Key Vault when DataProtection:UseAzure is true outside those environments).
            builder.Services.AddTenantSettingsDataProtection(builder.Configuration, builder.Environment);
            var encryptor = BuildTenantSettingsEncryptor(builder.Configuration, builder.Environment);
            builder.Services.AddSingleton<ITenantSettingsEncryptor>(encryptor);
            builder.Services.AddScoped<ITenantConfigSeeder, GovUK.Dfe.FlexForms.Infrastructure.Services.TenantConfigSeederService>();
            builder.Services.AddScoped<ITenantSettingsWriter, GovUK.Dfe.FlexForms.Infrastructure.Services.TenantSettingsWriterService>();
            builder.Services.AddScoped<ITenantPrincipalResolver, GovUK.Dfe.FlexForms.Infrastructure.Services.TenantPrincipalResolver>();
            builder.Services.AddScoped<ITenantSettingsReader, GovUK.Dfe.FlexForms.Infrastructure.Services.TenantSettingsReader>();
            builder.Services.AddScoped<ITenantHostnameResolver, GovUK.Dfe.FlexForms.Infrastructure.Services.TenantHostnameResolver>();
            builder.Services.AddSingleton<IHostConfigurationReader, GovUK.Dfe.FlexForms.Infrastructure.Services.HostConfigurationReader>();

            // SaaS hot-reload pub/sub: the configuration provider broadcasts refresh events; the
            // auth-provider registry subscribes and rebuilds its indexes in-place.
            // Instantiated directly here so the singleton instance can be wired into the
            // DatabaseTenantConfigurationProvider (which is built before app.Build()).
            var tenantConfigChangedNotifier = new GovUK.Dfe.FlexForms.Infrastructure.Services.TenantConfigurationChangedNotifier(
                CreateBootstrapLoggerFactory()
                    .CreateLogger<GovUK.Dfe.FlexForms.Infrastructure.Services.TenantConfigurationChangedNotifier>());
            builder.Services.AddSingleton<ITenantConfigurationChangedNotifier>(tenantConfigChangedNotifier);

            // Tenant configuration provider: Database or AppSettings based on config toggle
            // Path 3: TenantConfig DB is the runtime source of truth; AppSettings remains for tests/codegen.
            var tenantConfigSource = builder.Configuration["TenantConfigSource"] ?? "Database";
            ITenantConfigurationProvider tenantConfigurationProvider;

            if (string.Equals(tenantConfigSource, "Database", StringComparison.OrdinalIgnoreCase))
            {
                var tempScopeFactory = BuildServiceScopeFactory(builder.Services, builder.Configuration);

                var dbProvider = new DatabaseTenantConfigurationProvider(
                    tempScopeFactory,
                    CreateBootstrapLoggerFactory()
                        .CreateLogger<DatabaseTenantConfigurationProvider>(),
                    encryptor: encryptor,
                    targetApplication: "Api",
                    changeNotifier: tenantConfigChangedNotifier);

                dbProvider.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();

                tenantConfigurationProvider = dbProvider;
                builder.Services.AddSingleton<ITenantConfigurationProvider>(dbProvider);
                builder.Services.AddSingleton<IHostedService>(dbProvider);
            }
            else
            {
                var optionsProvider = new OptionsTenantConfigurationProvider(builder.Configuration);
                tenantConfigurationProvider = optionsProvider;
                builder.Services.AddSingleton<ITenantConfigurationProvider>(optionsProvider);
            }

            var allTenants = tenantConfigurationProvider.GetAllTenants();

            if (!allTenants.Any())
            {
                throw new InvalidOperationException(
                    "At least one tenant must be configured.");
            }
            builder.Services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
            builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();

            builder.Services.AddCustomExceptionHandler<ValidationExceptionHandler>();
            builder.Services.AddCustomExceptionHandler<JsonExceptionHandler>();
            builder.Services.AddCustomExceptionHandler<ApplicationExceptionHandler>();

            // Collect all frontend origins from all tenants for the default CORS policy
            // TenantCorsPolicyProvider will handle per-tenant CORS dynamically
            var allFrontendOrigins = allTenants
                .SelectMany(t => t.FrontendOrigins)
                .Distinct()
                .ToArray();
            
            builder.Services.AddCors(o => o.AddPolicy("Frontend", p =>
                p.WithOrigins(allFrontendOrigins.Length > 0 ? allFrontendOrigins : new[] { "https://localhost:7020" })
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()));

            builder.Services.AddSingleton<ICorsPolicyProvider, TenantCorsPolicyProvider>();

            // Configure SignalR using the shared host configuration (single Azure SignalR Service for all tenants)
            ConfigureSignalR(builder.Services, builder.Configuration, builder.Environment);

            builder.Services.AddApplicationDependencyGroup(builder.Configuration, tenantConfigurationProvider);
            builder.Services.AddInfrastructureDependencyGroup(builder.Configuration, tenantConfigurationProvider);

            // SaaS auth: hot-reloadable registry of per-tenant auth providers. Singleton; subscribes
            // to ITenantConfigurationChangedNotifier and rebuilds its indexes when the tenant
            // configuration is refreshed (no restart required).
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<ITenantAuthProviderRegistry, GovUK.Dfe.FlexForms.Infrastructure.Services.DatabaseTenantAuthProviderRegistry>();

            builder.Services.AddCustomAuthorization(builder.Configuration, tenantConfigurationProvider);

            builder.Services.AddOptions<SwaggerUIOptions>()
                .Configure<IHttpContextAccessor>((swaggerUiOptions, httpContextAccessor) =>
                {
                    var originalIndexStreamFactory = swaggerUiOptions.IndexStream;
                    swaggerUiOptions.IndexStream = () =>
                    {
                        using var originalStream = originalIndexStreamFactory();
                        using var originalStreamReader = new StreamReader(originalStream);
                        var originalIndexHtmlContents = originalStreamReader.ReadToEnd();
                        var requestSpecificNonce = httpContextAccessor?.HttpContext?.GetNonce();
                        var nonceEnabledIndexHtmlContents = originalIndexHtmlContents
                            .Replace("<script", $"<script nonce=\"{requestSpecificNonce}\" ",
                                StringComparison.OrdinalIgnoreCase)
                            .Replace("<style", $"<style nonce=\"{requestSpecificNonce}\" ",
                                StringComparison.OrdinalIgnoreCase);
                        return new MemoryStream(Encoding.UTF8.GetBytes(nonceEnabledIndexHtmlContents));
                    };
                });

            // Application Insights uses global configuration (not per-tenant)
            var appInsightsCnnStr = builder.Configuration["GlobalConfiguration:ApplicationInsights:ConnectionString"];
            
            if (!string.IsNullOrWhiteSpace(appInsightsCnnStr))
            {
                builder.Services.AddApplicationInsightsTelemetry(opt =>
                {
                    opt.ConnectionString = appInsightsCnnStr;
                });
            }
            
            // Disable the App Insights ILogger provider completely
            // All logs/exceptions should go through Serilog with our custom converter (includes ErrorId)
            builder.Logging.AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>(
                (category, level) => false);

            builder.Services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });

            builder.Services.AddOpenApiDocument(configure => { configure.Title = "Api"; });


            var app = builder.Build();

            // Reconfigure Serilog to add Application Insights sink now that TelemetryConfiguration is available
            var telemetryConfig = app.Services.GetService<TelemetryConfiguration>();
            if (telemetryConfig != null)
            {
                Log.Logger = new Serilog.LoggerConfiguration()
                    .ReadFrom.Configuration(builder.Configuration)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.ApplicationInsights(
                        telemetryConfig,
                        new GovUK.Dfe.FlexForms.Api.Telemetry.ExceptionTrackingTelemetryConverter())
                    .CreateLogger();
            }

            var forwardOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.All,
                RequireHeaderSymmetry = false
            };
            forwardOptions.KnownNetworks.Clear();
            forwardOptions.KnownProxies.Clear();
            app.UseForwardedHeaders(forwardOptions);

            app.UseMiddleware<TenantResolutionMiddleware>();

            // CORS must be early in the pipeline to ensure headers are added to all responses (including errors)
            app.UseCors("Frontend");

            // Swagger UI + aspnetcore hot-reload + VS Browser Link serve HTML/JS that does not always
            // line up with CSP nonces generated for API responses. Applying the strict CSP below to
            // those paths leaves the browser blocking inline bootstrapping scripts and Swagger appears
            // stuck on "Loading..." even though /swagger/index.html returned 200.
            app.UseWhen(
                static ctx => !IsSwaggerOrDevToolingPath(ctx.Request.Path),
                static branch => branch.UseSecurityHeaders(options =>
                {
                    options.AddFrameOptionsDeny()
                        .AddXssProtectionDisabled()
                        .AddContentTypeOptionsNoSniff()
                        .RemoveServerHeader()
                        .AddContentSecurityPolicy(builder =>
                        {
                            builder.AddDefaultSrc().Self();
                            builder.AddStyleSrc().Self().WithNonce();
                            builder.AddScriptSrc().Self().WithNonce();
                        })
                        .AddPermissionsPolicy(builder =>
                        {
                            builder.AddAccelerometer().None();
                            builder.AddAutoplay().None();
                            builder.AddCamera().None();
                            builder.AddEncryptedMedia().None();
                            builder.AddFullscreen().None();
                            builder.AddGeolocation().None();
                            builder.AddGyroscope().None();
                            builder.AddMagnetometer().None();
                            builder.AddMicrophone().None();
                            builder.AddMidi().None();
                            builder.AddPayment().None();
                            builder.AddPictureInPicture().None();
                            builder.AddSyncXHR().None();
                            builder.AddUsb().None();
                        });
                }));

            app.UseHsts();
            app.UseHttpsRedirection();

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
                foreach (var desc in provider.ApiVersionDescriptions)
                {
                    c.SwaggerEndpoint($"/swagger/{desc.GroupName}/swagger.json", desc.GroupName.ToUpperInvariant());
                }

                c.SupportedSubmitMethods(SubmitMethod.Get, SubmitMethod.Post, SubmitMethod.Put, SubmitMethod.Delete);
            });

            app.UseMiddleware<CorrelationIdMiddleware>();
            app.UseGlobalExceptionHandler(options =>
            {
                options.IncludeDetails = builder.Environment.IsDevelopment();
                options.LogExceptions = true;
                options.DefaultErrorMessage = "Something went wrong";
                options.SharedPostProcessingAction = (exception, response) =>
                {
                    if (exception is GovUK.Dfe.FlexForms.Application.Common.Exceptions.ValidationException validationException)
                    {
                        response.Details = string.Join("; ",
                            validationException.Errors
                                .SelectMany(kvp => kvp.Value.Select(error => $"{kvp.Key}: {error}")));
                    }
                    else if (exception is JsonException jsonException)
                    {
                        response.Details = jsonException.Message;
                    }
                };
            });


            app.UseMiddleware<UrlDecoderMiddleware>();

            app.UseRouting();

            app.UseCors("Frontend");

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                endpoints.MapHub<Hubs.NotificationHub>("/hubs/notifications")
                    .RequireAuthorization("Cookies.CanReadNotifications")
                    .RequireCors("Frontend");
            });

            ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Logger is working...");

            try
            {
                await app.RunAsync();
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Paths that must not receive the API's strict CSP-with-nonce headers. Swagger UI and several
        /// IDE/browser dev tools inject or bootstrap scripts in ways that break when nonce generation
        /// and HTML rewriting are even slightly out of sync.
        /// </summary>
        private static bool IsSwaggerOrDevToolingPath(PathString path)
        {
            return path.StartsWithSegments("/swagger")
                || path.StartsWithSegments("/_framework")
                || path.StartsWithSegments("/_vs");
        }

        /// <summary>
        /// Configures SignalR for the SaaS model: ONE shared Azure SignalR Service for ALL tenants.
        /// Tenant isolation is logical, enforced by group-naming convention - NOT by separate
        /// Azure SignalR resources. The connection string is sourced from root host configuration
        /// (NOT from per-tenant config).
        /// <para>
        /// Group naming contract: <c>tenant:{tenantId}:user:{userEmail}</c>
        /// (or <c>tenant:{tenantId}:...</c> for any future broadcast scopes).
        /// </para>
        /// <para>
        /// NEW HUBS must:
        /// </para>
        /// <list type="number">
        /// <item><description>Resolve <c>ITenantContextAccessor.CurrentTenant</c> at connect time.</description></item>
        /// <item><description>Prefix every group name with <c>tenant:{currentTenantId}:</c>.</description></item>
        /// <item><description>Always publish via <c>IHubContext</c> to a <c>tenant:{currentTenantId}:...</c> group.</description></item>
        /// </list>
        /// </summary>
        private static void ConfigureSignalR(
            IServiceCollection services,
            IConfiguration config,
            IWebHostEnvironment environment)
        {
            // Connection string sources (root host config, in priority order):
            //   1. Azure:SignalR:ConnectionString  (Microsoft.Azure.SignalR SDK convention)
            //   2. ConnectionStrings:AzureSignalR  (.NET-standard fallback)
            // Either populated wins. If neither is set, the API falls back to in-process SignalR
            // which is fine for dev/local; production must provision a shared Azure SignalR Service.
            var azureSignalRConn =
                config["Azure:SignalR:ConnectionString"]
                ?? config.GetConnectionString("AzureSignalR");

            var signalRBuilder = services.AddSignalR();

            if (!string.IsNullOrWhiteSpace(azureSignalRConn))
            {
                signalRBuilder.AddAzureSignalR(azureSignalRConn);
            }
            // else: in-process SignalR (development/local). No additional registration required.

            services.AddScoped<GovUK.Dfe.FlexForms.Domain.Services.INotificationHubContext, GovUK.Dfe.FlexForms.Api.Services.NotificationHubContext>();
        }

        /// <summary>
        /// Builds a temporary IServiceScopeFactory so the DatabaseTenantConfigurationProvider
        /// can load tenants before the full application DI container is built.
        /// </summary>
        private static IServiceScopeFactory BuildServiceScopeFactory(
            IServiceCollection services,
            IConfiguration configuration)
        {
            var tempServices = new ServiceCollection();
            tempServices.AddDbContext<TenantConfigDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("TenantConfigDatabase")));
            tempServices.AddLogging(ConfigureConsoleLogging);
            return tempServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        /// <summary>
        /// Console logging for bootstrap/temporary service providers (tenant config load before
        /// the main Serilog pipeline is fully wired). Suppresses noisy EF SQL command output.
        /// </summary>
        private static void ConfigureConsoleLogging(ILoggingBuilder logging)
        {
            logging.AddConsole();
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Warning);
        }

        private static ILoggerFactory CreateBootstrapLoggerFactory()
            => LoggerFactory.Create(ConfigureConsoleLogging);

        /// <summary>
        /// Builds a DataProtection-backed ITenantSettingsEncryptor for encrypting/decrypting
        /// secret tenant settings before the main host DI container is built.
        /// Uses the same Local vs Azure key-ring rules as <see cref="TenantSettingsDataProtectionExtensions"/>.
        /// </summary>
        private static ITenantSettingsEncryptor BuildTenantSettingsEncryptor(
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            var tempServices = new ServiceCollection();
            tempServices.AddTenantSettingsDataProtection(configuration, environment);
            tempServices.AddLogging(ConfigureConsoleLogging);
            var tempProvider = tempServices.BuildServiceProvider();
            return new DataProtectionTenantSettingsEncryptor(
                tempProvider.GetRequiredService<IDataProtectionProvider>());
        }
    }
}
