# منصة حجز الكشوفات الطبية (Clinic Platform)

مشروع تخرج - تدريب ITI / DEPI. الفريق 3 أفراد (التقسيم التفصيلي في `docs/TEAM.md`).

> آخر تحديث: 3 نوفمبر 2026. المراحل 0 إلى 4 (الأساس والـ Auth والأطباء والجداول) اتنفذت في الكود. الاختبارات في `docs/SWAGGER-TESTS.md`.

---

## 1. نظرة عامة

منصة لحجز الكشوفات الطبية أونلاين. المريض يبحث عن دكتور (بالتخصص أو السعر أو التقييم)، ويحجز ويدفع، والدكتور يدير أجندته ويشوف التاريخ المرضي لمرضاه، والإدارة تتابع العيادات والأطباء والتقارير المالية.

**القاعدة الأساسية:** أي مستخدم ما يقدرش يوصل لبيانات مريض تاني.

### اللي اتشال من الفكرة الأصلية
| الميزة | السبب |
|---|---|
| الاستشارات بالفيديو (WebRTC وSDK وScreen Sharing) | قرار الفريق: غلط من الناحية الصحية |
| الروشتة الإلكترونية | قرار الفريق |
| ملفات العائلة (Family Profiles) | اترفضت |
| سجل مراجعة الوصول (Audit Log) | اترفض |

### اللي اتضاف
تقييمات الدكاترة، تذكيرات المواعيد، التاريخ المرضي المنظم، الرسائل الآمنة بين المريض والدكتور، كشف المتابعة، الكوبونات، دعم العربي والإنجليزي (RTL). وفكرة اختيارية: **اقتراح التخصص من الأعراض** بالـ AI (استدعاء LLM واحد بمخرجات JSON منظمة، من غير بيانات مريض، ومن غير Agent أو شات بوت) كمرحلة اختيارية في الآخر، لسه ما اتحسمتش.

### الأدوار (RBAC) ومين بيشوف مين

| الدور | يعمل إيه |
|---|---|
| Admin | يدير التخصصات والعيادات والأطباء (إنشاء واعتماد) والريسبشن والكوبونات، ويشوف الإحصائيات والتقارير المالية |
| Doctor | يدير أوقات عمله وإجازاته وأسعاره، ويشوف مرضاه، ويرد على رسائلهم |
| Patient | يبحث ويحجز ويدفع، ويدير تاريخه المرضي ومرفقاته، ويقيّم الدكتور، ويراسله بعد الكشف |
| Receptionist | يتابع مواعيد عيادته، ويأكد الدفع في العيادة، ويسجل وصول المريض |

**قواعد الوصول للبيانات (مسجلة):**
- **المريض:** بياناته هو بس (مواعيده، مدفوعاته، سجله الطبي، مرفقاته، رسائله).
- **الدكتور:** السجل الطبي والمرفقات لمريض عنده معاه موعد حالته `Confirmed` أو `CheckedIn` أو `Completed` بس (مش `PendingPayment` ولا `Cancelled` ولا `Expired`).
- **الريسبشن:** شغله جوه عيادته هو بس (يتحقق إن عيادة الدكتور = `ClinicId` بتاعه). **مايشوفش** سجل طبي ولا مرفقات ولا رسائل.
- **الأدمن:** الإدارة والتقارير. **مايشوفش** سجل طبي ولا مرفقات ولا رسائل.
- القاعدة تتكتب مرة واحدة في الـ Application layer (`IPatientAccessGuard`) وتتستخدم في كل endpoint حساس، مش تتكرر.

---

## 2. التقنيات

| الجزء | التقنية |
|---|---|
| Backend | ASP.NET Core Web API (.NET 10)، Clean Architecture |
| Database | SQL Server + Entity Framework Core |
| Auth | ASP.NET Core Identity (`AddIdentityCore`) + JWT (HS256) + أدوار. تأكيد الإيميل بـ OTP بالإيميل (لسه) |
| Realtime | SignalR (حالة الحجز، الرسائل) |
| الدفع | Paymob Sandbox + الدفع في العيادة |
| Jobs | Hosted Service أو Hangfire (التذكيرات، انتهاء الحجز المؤقت) |
| Caching | Memory Cache أو Redis |
| Logging | Serilog |
| التوثيق | **Swagger** على `/swagger` (مع زر Authorize للـ JWT) وملف `swagger.json` بيتصدّر للجنة عند الحاجة. (Postman اتشال، وScalar مش متركب) |
| Localization | Resource Files، عربي / إنجليزي مع RTL |
| Frontend | **لسه ما اتحددش** |

---

## 3. هيكل المشروع والاعتماديات

```
medical booking.slnx
├── Clinic.Domain          Entities، Enums (من غير أي packages)
├── Clinic.Application     Features/* (Services وDTOs)، Common/* (Interfaces، Exceptions، Security، Time)
├── Clinic.Infrastructure  AppDbContext، Configurations، Migrations، Identity (JWT, Seeder)، DI
└── Clinic.API             Controllers، Middleware، Services/CurrentUser، Program.cs
tests/
└── Clinic.Tests           Unit tests (xUnit)
```

**قاعدة الاعتماديات:** الطبقة الخارجية تعرف الداخلية، والعكس لا.

| المشروع | بيشوف |
|---|---|
| Domain | لا حاجة |
| Application | Domain |
| Infrastructure | Application و Domain |
| API | Application و Infrastructure |

### الحاجات المشتركة (القرارات الفنية)
| الموضوع | القرار |
|---|---|
| الوصول للداتابيز | `IAppDbContext` في Application (مفيش Repositories). `UserSummaries` بيدي الاسم والإيميل من غير ما Application يعرف `ApplicationUser` |
| الأنماط | Services عادية في `Features/<Feature>` (مفيش MediatR). كل feature فيه DTOs وService وinterface |
| التحقق من المدخلات | DataAnnotations على الـ DTOs (بيرجع 400 تلقائي)، وقواعد الأعمال بترمي `AppValidationException` |
| الأخطاء | `Common/Exceptions` + `GlobalExceptionHandler` بيحولها لـ ProblemDetails: 400 / 401 / 403 / 404 / 409 / 500 |
| الوقت | التخزين UTC. أوقات العمل (`TimeOnly`) بتوقيت القاهرة. التحويل من `ClinicTime` بس. الوقت الحالي من `TimeProvider` |
| الهوية | `ICurrentUser` (UserId, Role, PatientId, DoctorId, ClinicId). الـ ids الثلاثة claims في الـ JWT (`patient_id` / `doctor_id` / `clinic_id`) |
| الأدوار | `Roles.Admin / Doctor / Patient / Receptionist` (دور واحد لكل مستخدم) |
| الدفع وقت الحجز | Online: `PendingPayment` لمدة 15 دقيقة. AtClinic: `Confirmed` فوراً ومحدش يلغيه الـ Job |
| تحويلات حالة الموعد | عند الشخص 2 (`Confirm` و`CheckIn` و`Complete` و`NoShow`). الشخص 3 بينادي `Confirm` بعد الدفع |
| قائمة الانتظار | من غير حقل جديد: لما ميعاد يتفتح، يتدور على `DoctorId` + `DesiredDate` ويتبعت `Notification` نوع `Waitlist` |
| Endpoints إضافية | `/check-in` و`/complete` و`/no-show` عند الشخص 2. CRUD الكوبونات (الأدمن) عند الشخص 3 |
| منع الحجز المزدوج | Unique Filtered Index على `(DoctorId, StartAt)` يستثني `Cancelled` و`Expired` + `RowVersion` |
| السعر | بيتحفظ على `Appointment.PriceSnapshot` وقت الحجز |
| الشات | 14 يوم بعد الكشف المكتمل |

---

## 4. الحالة الحالية

### اتنفذ (المراحل 0 إلى 4)
- [x] الـ Solution بأربع طبقات والـ references صح، وSerilog، وUser Secrets.
- [x] 17 Entity وكل الـ Enums والـ Configurations، والـ Migration `InitialCreate` + Unique Index منع الحجز المزدوج.
- [x] Seed للبيانات التجريبية (`seed/seed-all-tables.sql`).
- [x] Application foundation: `IAppDbContext`، `ICurrentUser`، `Roles`، `CustomClaims`، Exceptions، `ClinicTime`، `PagedResult`.
- [x] `GlobalExceptionHandler` (ProblemDetails).
- [x] **Auth:** تسجيل مريض، Login بـ JWT، `GET /api/auth/me`، قفل الحساب بعد 5 محاولات فاشلة، الأدوار الأربعة بتتعمل أوتوماتيك، وحسابات `@test.local` بتاخد باسورد `Test@12345` في Development.
- [x] **التخصصات والعيادات:** CRUD كامل (القراءة عامة، الكتابة للأدمن، الحذف 409 لو مستخدم).
- [x] **الأطباء:** إنشاء الحساب من الأدمن، الاعتماد وسحبه، التعديل، قايمة الأدمن، الدكتور يعدل سعره ومدة المتابعة بنفسه.
- [x] **جدول العمل والإجازات:** استبدال الجدول الأسبوعي، منع تقاطع الفترات، إجازات بمنع التقاطع مع إجازة تانية أو مواعيد محجوزة.
- [x] **البحث والفلترة:** تخصص، عيادة، سعر، تقييم، اسم، ترتيب، صفحات.
- [x] **الريسبشن:** إنشاء وعرض وحذف من الأدمن.
- [x] Swagger مع زر Authorize.
- [x] **Unit tests** (`tests/Clinic.Tests`، xUnit، 55 اختبار): الجدول، التوقيت، الـ JWT، الأخطاء، `CurrentUser`، قواعد التحقق. بتشتغل بـ `dotnet test` من غير داتابيز.
- [x] اختبارات Swagger الأساسية (Auth، البحث، الجدول، الإجازات) اتجربت ونجحت. الـ 115 كاملين وقت الدمج.
- [x] المشروع على GitHub (`main`).

### لسه
| # | البند | المرحلة | المسؤول |
|---|---|---|---|
| B2 | OTP بالإيميل لتأكيد الحساب | 3 | شخص 1 |
| B5b | توليد الـ Slots + `GET /api/doctors/{id}/slots` | 5 | شخص 2 |
| B7 | الحجز بدون تعارض (Transaction + RowVersion + اختبارات تزامن) | 5 | شخص 2 |
| B8 | الإلغاء وفتح الميعاد وقائمة الانتظار + `/check-in` و`/complete` و`/no-show` | 5 | شخص 2 |
| B9 | Paymob Sandbox + Webhook مع HMAC | 6 | شخص 3 |
| B10 | الدفع في العيادة + تأكيد الموعد بعد الدفع | 6 | شخص 3 |
| B11 | Hosted Service: إلغاء `PendingPayment` بعد 15 دقيقة | 5 أو 6 | شخص 2 |
| B12 | التاريخ المرضي بتشفير الحقول الحساسة | 7 | شخص 3 |
| B13 | المرفقات (PDF وصور) بأمان | 7 | شخص 3 |
| B14 | التقييمات بعد الكشف المكتمل | 8 | — |
| B15 | الكوبونات (CRUD للأدمن + `validate`) وكشف المتابعة | 8 | شخص 3 |
| B16 | الإشعارات والتذكيرات | 9 | — |
| B17 | SignalR والرسائل الآمنة | 9 | — |
| B18 | Localization عربي / إنجليزي + RTL | 10 | — |
| B19 | Caching | 10 | — |
| B20 | الإحصائيات والتقارير المالية (الأدمن) | 11 | — |
| B22 | اختبارات Swagger الكاملة (115) وقت الدمج + مراجعة أمنية | 12 | — |
| — | `IPatientAccessGuard` (قاعدة وصول الدكتور للمريض) | 5 أو 7 | شخص 2 أو 3 |
| — | (اختياري) اقتراح التخصص من الأعراض بـ AI | آخر المشروع | لسه ما اتحسمش |

### Frontend (بعد ما الـ Backend يتقفل)
قرار التقنية مفتوح (MVC ولا React ولا Angular). الشاشات: Patient Portal، Doctor Dashboard، Admin Dashboard، Receptionist، Dark/Light + Responsive + RTL.

---

## 5. الـ Endpoints المنفذة

| الصلاحية | Method | المسار | الوصف |
|---|---|---|---|
| عام | POST | `/api/auth/register` | تسجيل مريض |
| عام | POST | `/api/auth/login` | JWT |
| أي مستخدم | GET | `/api/auth/me` | بيانات المستخدم من الـ token |
| عام | GET | `/api/specialties` و`/{id}` | التخصصات |
| Admin | POST / PUT / DELETE | `/api/specialties` و`/{id}` | إدارة التخصصات |
| عام | GET | `/api/clinics` و`/{id}` | العيادات الشغالة |
| Admin | POST / PUT / DELETE | `/api/clinics` و`/{id}` | إدارة العيادات |
| عام | GET | `/api/doctors` | بحث وفلترة (المعتمدين بس) |
| عام | GET | `/api/doctors/{id}` | ملف دكتور معتمد |
| عام | GET | `/api/doctors/{id}/schedule` | جدول عمله |
| Doctor | GET / PUT | `/api/doctors/me` | ملفه وتعديل السعر والمتابعة |
| Doctor | GET / PUT | `/api/doctors/me/schedule` | جدوله الأسبوعي |
| Doctor | GET / POST | `/api/doctors/me/timeoffs` | إجازاته |
| Doctor | DELETE | `/api/doctors/me/timeoffs/{id}` | حذف إجازة |
| Admin | POST / GET | `/api/admin/doctors` | إنشاء وقايمة الأطباء |
| Admin | GET / PUT | `/api/admin/doctors/{id}` | عرض وتغيير التخصص أو العيادة |
| Admin | PATCH | `/api/admin/doctors/{id}/approve` و`/unapprove` | اعتماد وسحب الاعتماد |
| Admin | POST / GET | `/api/admin/receptionists` | إنشاء وقايمة الريسبشن |
| Admin | GET / DELETE | `/api/admin/receptionists/{id}` | عرض وحذف |

---

## 6. تشغيل المشروع من الصفر

```powershell
# 1) الأسرار (مرة واحدة)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection string>" --project Clinic.API
dotnet user-secrets set "Jwt:Key" "<32 حرف عشوائي على الأقل>" --project Clinic.API

# 2) الداتابيز
dotnet ef database update --project Clinic.Infrastructure --startup-project Clinic.API
#    ثم في SSMS على ClinicDb: seed/seed-all-tables.sql

# 3) التشغيل
dotnet build "medical booking.slnx"
dotnet run --project Clinic.API --launch-profile https
#    Swagger: https://localhost:7011/swagger

# 4) Unit tests (من غير داتابيز)
dotnet test
```

حسابات الاختبار (الباسورد `Test@12345`، بتتظبط أوتوماتيك في Development):

| الإيميل | الدور | ملحوظة |
|---|---|---|
| admin@test.local | Admin | |
| doctor1@test.local | Doctor | Dr Ahmed Test، قلب، 300 جنيه، معتمد |
| doctor2@test.local | Doctor | Dr Sara Test، جلدية، 250 جنيه، معتمدة |
| doctor3@test.local | Doctor | Dr Mona Test، أطفال، **غير معتمدة** |
| patient1@test.local إلى patient3 | Patient | |
| reception1@test.local | Receptionist | عيادة 1 |

Migration جديدة: من المسؤول عن الدمج بس، بعد الدمج الكامل.

---

## 7. توزيع الأدوار (3 أفراد)

التفاصيل الكاملة والـ checklist في `docs/TEAM.md`. الخلاصة:

| # | المسؤولية | المراحل |
|---|---|---|
| 1 | Auth + الأطباء والجداول + Swagger (**اتنفذت**) | 3 و4 |
| 2 | محرك الحجز (Slots، الحجز، الإلغاء، قائمة الانتظار، تحويلات الحالة، Hosted Service) | 5 |
| 3 | الدفع + السجل الطبي والمرفقات + الكوبونات | 6 و7 و8 |

**شخص 2 و3 يبدأوا من `develop` بعد ما فرع الشخص 1 يتدمج.** اللي محتاجينه جاهز: `ICurrentUser` و`Roles` و`IAppDbContext` وJWT شغال بـ claims الـ ids.

---

## 8. Git وقواعد الشغل

- الفروع: `main` (مستقر) و`develop` (التكامل) و`feature/<المسؤولية>` لكل فرد. ممنوع push مباشر على `main` و`develop`، وكل حاجة بـ PR بمراجعة واحدة.
- الـ Commits: `feat(auth): ...` و`fix(booking): ...` و`chore(...)` و`docs: ...`.
- `.gitattributes` بيوحد نهايات الأسطر (commit لوحده الأول).
- **ممنوع تعدل:** `Clinic.Domain/Entities/*` و`Enums/*` (مقفولة)، و`Program.cs` و`DependencyInjection.cs` و`AppDbContext.cs` و`*.csproj` (للمسؤول عن الدمج). بدلها: كل مسؤولية تعمل Module خاص بيها فيه `AddXxx(this IServiceCollection)`.
- **Migrations:** ممنوع على فرعك. بتتعمل مرة واحدة بعد الدمج.
- لو محتاج بيانات من مسؤولية تانية: اكتب interface في فولدرك واشتغل بـ stub.
- الأسرار (connection string، `Jwt:Key`، مفاتيح Paymob، مفتاح التشفير) في User Secrets بس.
- ترتيب الدمج: Auth والأطباء الأول، وبعدهم الحجز، والدفع، والسجل الطبي.

### تعريف "خلص" (Definition of Done)
- [ ] بيتبني من غير warnings جديدة.
- [ ] Unit tests للقواعد، وIntegration tests للـ endpoints الحساسة (اختبارات التزامن على SQL Server حقيقي).
- [ ] الـ endpoints ظاهرة في Swagger بالـ Responses الصح.
- [ ] الصلاحيات متطبقة (Role أو Policy) ومجربة بـ 401 و403.
- [ ] مفيش أسرار في الكود أو `appsettings.json`.
- [ ] `docs/todo.md` اتحدث، والـ PR اتراجع واتعمل merge.

---

## 9. القرارات المفتوحة

1. **ملاحظات الكشف (`VisitNotes`):** حقل على `Appointment` ومشفر؟ (يحتاج Migration، فيتحسم قبل الـ Migration القادمة).
2. **Frontend:** MVC ولا React ولا Angular؟
3. **AI لاقتراح التخصص:** نضيفه ولا لأ؟
4. **OTP:** التنفيذ نفسه (مزود الإيميل).

---

## 10. ملاحظات مهمة

- **الأمان:** الحقول المشفرة (`MedicalHistory` و`Message.Body`) بتتشفر وتتفك في Infrastructure بس. الـ Seed الحالي فيه نص عادي في الجدولين دول، فالشخص 3 يمسحهم من الـ Seed ويعيد إنشاءهم من الكود بعد التشفير.
- **Paymob Webhook:** أي طلب من غير HMAC صحيح يترفض قبل أي حاجة.
- **Conversation:** `ExpiresAt` = وقت الكشف المكتمل + 14 يوم.
- **الـ JWT:** الـ claims `patient_id` و`doctor_id` و`clinic_id` بتتحدد وقت الـ Login. لو الأدمن غيّر عيادة دكتور، الـ token القديم يفضل بالقيمة القديمة لحد ما ينتهي أو يسجل دخول تاني.
- **الإجازات:** بتتخزن UTC. الـ API بيقبل `DateTimeOffset` (مثال `2026-12-01T00:00:00+02:00`).
- **الـ Seed:** موعد #1 (الثلاثاء 09:00 لدكتور 1) برا جدول عمله. بيانات تجريبية بس، مش بتأثر.
