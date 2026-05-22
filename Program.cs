using Amazon;
using Amazon.Extensions.Configuration.SystemsManager;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime.CredentialManagement;
using MessageProxyApi.Data;
using MessageProxyApi.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Web;
using System.Security.Claims;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);
var awsCredentialsFilePath = Path.Combine(Directory.GetCurrentDirectory(), "awscredentials.xml");

var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
try
{
    builder.Configuration.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    if (File.Exists(awsCredentialsFilePath))
    {
        SetAwsCredentials(logger, awsCredentialsFilePath);
    }

    var awsOptions = new AWSOptions
    {
        Region = RegionEndpoint.USWest1,
        Profile = "cahfs"
    };

    try
    {
        builder.Configuration
            .AddSystemsManager($"/{builder.Environment.EnvironmentName}", awsOptions)
            .AddSystemsManager("/Shared", awsOptions);
    }
    catch (Exception ex)
    {
        logger.Fatal(ex, "Failed to load AWS Systems Manager configuration.");
    }

    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
    builder.Host.UseNLog();

    // Register IHttpClientFactory so controllers can inject IHttpClientFactory
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddMemoryCache();
    builder.Services.AddHttpClient();

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.Cookie.Name = "MessageProxy.Authentication.UCD";
            options.LoginPath = new PathString("/Login");
            options.AccessDeniedPath = new PathString("/Denied");
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
        });

    var allowedLoginIds = builder.Configuration.GetSection("AuthorizedLoginIds").Get<string[]>() ?? Array.Empty<string>();

    builder.Services.AddSingleton<IAuthorizationHandler, AuthorizedLoginHandler>();
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AuthorizedLogin", policy =>
            policy.RequireClaim(ClaimTypes.AuthenticationMethod, "CAS")
                .AddRequirements(new AuthorizedLoginRequirement(allowedLoginIds)));
    });

    builder.Services.Configure<CasSettings>(builder.Configuration.GetSection("Cas"));

    builder.Services.AddDbContext<ProxyDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("StarLIMSDB")));

    builder.Services.AddControllersWithViews();

    builder.Services.AddRazorPages(options =>
    {
        options.Conventions.AllowAnonymousToPage("/Login");
        options.Conventions.AllowAnonymousToPage("/CasLogin");
        options.Conventions.AllowAnonymousToPage("/Denied");
    });

    var app = builder.Build();

    //app.Services.GetService<IMemoryCache>(), 
    HttpHelper.Configure(builder.Configuration, app.Environment, app.Services.GetService<IHttpContextAccessor>());

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }
    else
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    app.MapRazorPages();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    logger.Fatal(ex, "Application stopped because of exception");
    throw;
}
finally
{
    NLog.LogManager.Shutdown();
}

void SetAwsCredentials(Logger logger, string awsCredentialsFilePath)
{
    XElement xAwsCredentials = XElement.Load(awsCredentialsFilePath, LoadOptions.None);

    if (!string.IsNullOrWhiteSpace(xAwsCredentials?.Element("AccessKeyId")?.Value) &&
        !string.IsNullOrWhiteSpace(xAwsCredentials?.Element("SecretAccessKey")?.Value))
    {
        var options = new CredentialProfileOptions
        {
            AccessKey = xAwsCredentials?.Element("AccessKeyId")?.Value.Trim(),
            SecretKey = xAwsCredentials?.Element("SecretAccessKey")?.Value.Trim()
        };

        var profile = new CredentialProfile("cahfs", options);

        if (!string.IsNullOrWhiteSpace(xAwsCredentials?.Element("RegionEndpoint")?.Value))
        {
#pragma warning disable CS8604 // Possible null reference argument.
            profile.Region = typeof(RegionEndpoint)
                .GetField(xAwsCredentials?.Element("RegionEndpoint")?.Value)
                ?.GetValue(null) as RegionEndpoint;
#pragma warning restore CS8604 // Possible null reference argument.
        }
        else
        {
            profile.Region = RegionEndpoint.USWest1;
        }

        var netSDKFile = new NetSDKCredentialsFile();
        netSDKFile.RegisterProfile(profile);

        try
        {
            File.Delete(awsCredentialsFilePath);
        }
        catch
        {
            logger.Error($"COULD NOT DELETE THE AWS CREDENTIALS XML FILE (\"{awsCredentialsFilePath}\"). The file will need to be deleted manually.");
        }
    }
    else
    {
        throw new FormatException($"Could not parse AWS Credentials File: \"{awsCredentialsFilePath}\". AccessKeyId and/or SecretAccessKey are blank.");
    }
}
