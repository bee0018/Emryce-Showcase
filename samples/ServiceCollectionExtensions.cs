using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace PortfolioApp.Api.Extensions;

/// <summary>
/// Demonstrates modular service configuration, custom Kestrel bindings, 
/// and cross-platform security hardening for a .NET Web API.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The development CORS policy.
    /// </summary>
    private static readonly string DevCorsPolicy = "DevCors";

    /// <summary>
    /// Sets up the web application builder.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="appConfig">The API configuration.</param>
    /// <returns>The configured web application builder.</returns>
    public static WebApplicationBuilder SetupBuilder(this WebApplicationBuilder builder, AppConfigurations appConfig)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Custom certificate binding pattern for secure local loopback communication
            int httpPort = appConfig.BindingPort;
            int httpsPort = appConfig.SecureBindingPort;
            options.ListenLocalhost(httpPort);
            options.ListenLocalhost(httpsPort, listenOptions =>
            {
                listenOptions.UseHttps();
            });
        });
        
        builder.WebHost.UseWebRoot("wwwroot");
        builder.Host.UseSerilog();
        if (OperatingSystem.IsWindows())
        {
            builder.Logging.AddEventLog();
        }

        return builder;
    }

    /// <summary>
    /// Adds the application services to the service collection.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="appConfig">The API configuration.</param>
    /// <returns>The application service collection with the added resources.</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, AppConfigurations appConfig)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("StandardPolicy", fixedOptions =>
            {
                fixedOptions.PermitLimit = 200;
                fixedOptions.Window = TimeSpan.FromMinutes(1);
                fixedOptions.QueueLimit = 0;
            });
        });

        services.AddControllers();
        services.AddSignalR();

        // Platform-aware Data Protection persistence (e.g., Linux common data folder vs Windows default)
        if (OperatingSystem.IsLinux())
        {
            string keyStoragePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PortfolioApp", "keys");
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyStoragePath))
                .SetApplicationName("PortfolioApp");
        }
        else
        {
            services.AddDataProtection();
        }


        // Register generic or sample core services
        return services
            .AddSingleton<IDemoService, DemoService>()
            .AddSingleton<IBackgroundWorker, BackgroundWorkerService>()
            .AddEndpointsApiExplorer()
            .AddOpenApiDocument(config =>
            {
                config.Title = "Portfolio API Showcase";
                config.Version = "v1";
            })
            .AddDbContext<ApplicationDbContext>(options => options.UseSqlite(appConfig.ConnectionString))
            .AddHostedService<SystemInitializationService>();
    }

    /// <summary>
    /// Applies the application services to the application builder.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The configured application builder.</returns>
    public static async Task<WebApplication> UseApplicationServices(this WebApplication app)
    {
        // Auto-apply database migrations on startup pattern
        using (IServiceScope scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        app.UseRateLimiter()
            .UseDefaultFiles()
            .UseStaticFiles()
            .UseRouting();
        app.UseAuthentication()
            .UseAuthorization()
            .UseOpenApi();
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage()
               .UseSwaggerUi();
        }

        app.MapControllers();
        app.MapFallbackToFile("index.html");
        return app;
    }
}