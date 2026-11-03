# الـ Packages والمتطلبات

> **ملحوظة للـ Leader:** مفيش packages بتتبعت كملفات. كل الـ packages مكتوبة في ملفات `.csproj` جوه الريبو، وبتتنزل أوتوماتيك مع `dotnet restore` (أو أول Build). الجدول ده للمراجعة بس.

## 1. NuGet Packages (من ملفات `.csproj` الحالية)

### Clinic.Domain
مفيش packages (نظيف تماماً).

### Clinic.Application
| Package | الإصدار | ليه |
|---|---|---|
| Microsoft.EntityFrameworkCore | 10.0.13 | `IAppDbContext` و`DbSet` وأوامر الاستعلام |

### Clinic.Infrastructure
| Package | الإصدار | ليه |
|---|---|---|
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.13 | حسابات المستخدمين والأدوار (ASP.NET Identity) |
| Microsoft.EntityFrameworkCore.SqlServer | 10.0.13 | الاتصال بـ SQL Server |
| System.Security.Cryptography.Pkcs | 10.0.13 | للتشفير (هيتستخدم في السجل الطبي لاحقاً) |

### Clinic.API
| Package | الإصدار | ليه |
|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.13 | التحقق من الـ JWT |
| Microsoft.EntityFrameworkCore.Design | 10.0.13 | أوامر الـ Migrations |
| Microsoft.EntityFrameworkCore.Tools | 10.0.13 | أوامر `dotnet ef` من Package Manager |
| Serilog.AspNetCore | 10.0.0 | الـ Logging |
| Serilog.Sinks.Console | 6.1.1 | Logging على الـ Console |
| Serilog.Sinks.File | 7.0.0 | Logging في ملف (`logs/clinic-.log`) |
| Swashbuckle.AspNetCore | 10.3.1 | Swagger UI وزر Authorize |
| Microsoft.AspNetCore.OpenApi | 10.0.13 | **غير مستخدم حالياً** (ممكن يتشال) |

**آخر تعديل (Auth + الأطباء + الجداول) ما أضافش أي package جديد للمشروع نفسه.**

### tests/Clinic.Tests (جديد)
مشروع الاختبارات بيتعمل بـ `dotnet new xunit`، فبيجيب packages القالب (`xunit` و`Microsoft.NET.Test.Sdk` و`xunit.runner.visualstudio`) بإصداراتها المناسبة. ومعاه ProjectReference لـ Application وInfrastructure وAPI.

### Packages هتتضاف لاحقاً (حسب المراحل)
| المرحلة | Package محتمل |
|---|---|
| 5 | Hangfire (لو اتقرر بدل Hosted Service) |
| 6 | مفيش (Paymob عن طريق `HttpClient`) |
| 9 | SignalR جاهز مع ASP.NET Core |
| 12 | `Microsoft.AspNetCore.Mvc.Testing` لاختبارات الـ Integration (xUnit موجود) |

## 2. الأدوات المطلوبة على كل جهاز

| الأداة | ملحوظة |
|---|---|
| .NET SDK 10 | `dotnet --version` المفروض يبدأ بـ 10 |
| SQL Server (أي instance) + SSMS | الداتابيز `ClinicDb` |
| Git | `git --version` |
| Visual Studio 2026 أو VS Code | أي واحد |
| `dotnet-ef` | `dotnet tool install --global dotnet-ef --version 10.0.13` |

## 3. تجهيز الجهاز بعد `git clone`

```powershell
# 1) الأسرار (مرة واحدة على كل جهاز)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=ClinicDb;Trusted_Connection=True;TrustServerCertificate=True;" --project Clinic.API
dotnet user-secrets set "Jwt:Key" "<32 حرف عشوائي على الأقل>" --project Clinic.API

# 2) الداتابيز
dotnet ef database update --project Clinic.Infrastructure --startup-project Clinic.API
#    ثم في SSMS على ClinicDb: seed/seed-all-tables.sql

# 3) البيلد والتشغيل
dotnet restore
dotnet build "medical booking.slnx"
dotnet run --project Clinic.API --launch-profile https
#    Swagger: https://localhost:7011/swagger
```

## 4. لو عايز قايمة حقيقية من جهازك

```powershell
dotnet list "medical booking.slnx" package
dotnet list "medical booking.slnx" package --include-transitive   # بالـ packages اللي بتنزل معاهم
dotnet tool list --global                                          # الأدوات (dotnet-ef)
dotnet --list-sdks
```
