using System.Text;
using System.Text.Json.Serialization;
using Clinic.API.Middleware;
using Clinic.API.Services;
using Clinic.Application;
using Clinic.Application.Common.Interfaces;
using Clinic.Infrastructure;
using Clinic.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Clinic API");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/clinic-.log", rollingInterval: RollingInterval.Day));

    // الداتابيز + Identity + JWT (بيتحقق من Jwt:Key وبيوقف التشغيل برسالة واضحة لو ناقص)
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApplication();

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();

    // ---- Authentication: JWT Bearer ----
    var jwtKey = builder.Configuration["Jwt:Key"]!;
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // بنسيب أسماء الـ claims زي ما هي في الـ token (sub / role / patient_id ...)
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ClinicApi",
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ClinicClient",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services
        .AddControllers()
        .AddJsonOptions(options =>
        {
            // Enums تظهر وتتقبل كنص (Male / Sunday / ...)
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Clinic API",
            Version = "v1",
            Description = "منصة حجز الكشوفات الطبية. اعمل Login من POST /api/auth/login، وانسخ الـ accessToken، واضغط Authorize."
        });

        options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "الصق الـ accessToken بس (من غير كلمة Bearer)."
        });

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("bearer", document)] = []
        });
    });

    var app = builder.Build();

    // الأدوار الأربعة دايماً، وباسوردات حسابات @test.local في Development بس
    await DbSeeder.SeedAsync(app.Services, seedTestAccounts: app.Environment.IsDevelopment());

    app.UseExceptionHandler();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.EnablePersistAuthorization());
    }

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Clinic API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// للـ Integration tests (WebApplicationFactory<Program>)
public partial class Program { }
