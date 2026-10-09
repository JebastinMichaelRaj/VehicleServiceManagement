using System.Globalization;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuestPDF.Infrastructure;
using Serilog;
using VehicleServiceManagement.Data;
using VehicleServiceManagement.Data.Identity;
using VehicleServiceManagement.Data.Repositories;
using VehicleServiceManagement.Domain.Identity;
using VehicleServiceManagement.Mapping;
using VehicleServiceManagement.Services.Billing;
using VehicleServiceManagement.Services.Booking;
using VehicleServiceManagement.Services.Customers;
using VehicleServiceManagement.Services.Dashboard;
using VehicleServiceManagement.Services.Inventory;
using VehicleServiceManagement.Services.Invoicing;
using VehicleServiceManagement.Services.Maintenance;
using VehicleServiceManagement.Services.Notifications;
using VehicleServiceManagement.Validation;

// QuestPDF Community licence: free for organisations under its revenue threshold.
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/vsm-.log", rollingInterval: RollingInterval.Day));

// ---- Data ----
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is not configured.")));

// ---- Identity (cookie auth, roles, default login/register UI) ----
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultUI();

// ---- Application services ----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IBookingPolicy, BookingPolicy>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IOverdueReminderService, OverdueReminderService>();
builder.Services.AddSingleton<IInvoicePdfGenerator, InvoicePdfGenerator>();

builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.Configure<SchedulingOptions>(builder.Configuration.GetSection(SchedulingOptions.SectionName));
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.SectionName));

// ---- Notifications: log by default; SMS with resilience (retry, circuit breaker, timeout) when configured ----
if (string.Equals(builder.Configuration["Notifications:Channel"], "Sms", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<INotificationSender, SmsNotificationSender>(client =>
    {
        client.BaseAddress = new Uri(builder.Configuration["Sms:BaseUrl"]
            ?? throw new InvalidOperationException("Sms:BaseUrl is required when Notifications:Channel is Sms."));
    }).AddStandardResilienceHandler();
}
else
{
    builder.Services.AddSingleton<INotificationSender, LogNotificationSender>();
}

builder.Services.AddHostedService<OverdueReminderWorker>();

// ---- Mapping & validation ----
builder.Services.AddAutoMapper(typeof(BookingProfile).Assembly);
builder.Services.AddValidatorsFromAssemblyContaining<CreateBookingRequestValidator>();

// ---- Web & API ----
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddControllers();

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Vehicle Service Management API", Version = "v1" });
    options.DocInclusionPredicate((_, _) => true);
});

// ---- Observability & health ----
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

if (builder.Configuration.GetValue<bool>("Telemetry:Enabled"))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("VehicleServiceManagement"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddConsoleExporter());
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync("An unexpected error occurred. Please try again.");
    }));
    app.UseHsts();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "VSM API v1"));
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Culture-aware formatting: en-IN by default (₹, dd/MM/yyyy, lakh grouping); en-US also supported.
var supportedCultures = new[] { new CultureInfo("en-IN"), new CultureInfo("en-US") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en-IN"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapHealthChecks("/health");

// Apply migrations and seed roles/accounts on startup (convenient for development; use a deployment step in production).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

app.Run();
