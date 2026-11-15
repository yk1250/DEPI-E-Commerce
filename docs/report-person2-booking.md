# تقرير شغل الشخص 2: محرك الحجز

**المشروع:** Clinic API (منصة حجز الكشوفات الطبية)
**الفرع:** `feature/booking`
**الحالة:** شغل الشخص 2 خلص من جهته (حوالي 95%). الباقي تسليم للدمج مع الشخص 1.
**آخر تشغيل كامل للاختبارات:** 47 اختبار، كلهم ناجحين (اختبار التزامن بيتعدّى لو متغير `CLINIC_TEST_CONNECTION` مش موجود).

> هذا التقرير بيحل محل الملفات الثلاثة السابقة (`booking-progress.md` و`booking-progress-v3.md` و`booking-progress-latest.md`)، لأنها متأخرة عن الحالة الحالية.

---

## 1. ملخص سريع

| المجال | الحالة |
|---|---|
| حساب المواعيد المتاحة (Slots) | ✅ خلص ومتجرّب |
| الحجز مع كل التحققات والتزامن | ✅ خلص ومتجرّب |
| إلغاء المريض (مع قاعدة الساعتين) | ✅ خلص ومتجرّب |
| إلغاء الدكتور | ✅ خلص ومتجرّب |
| تحويلات الحالة (check-in / complete / no-show) | ✅ خلص ومتجرّب |
| الحجوزات المنتهية تلقائياً (Expiry Job) | ✅ خلص ومتجرّب |
| قراءة المواعيد (مريض ودكتور) | ✅ خلص ومتجرّب |
| كشف المتابعة (FollowUp) | ✅ خلص ومتجرّب |
| قائمة الانتظار (تسجيل + إشعار عند الإلغاء) | ✅ خلص (الإيميل نفسه مؤجل للشخص 1) |
| تسجيل الـ DI للوحدة | ✅ خلص (`AddBookingModule`) |
| الإيميل الحقيقي (SMTP) | ⬜ الشخص 1 |
| تسجيل `AddBookingModule` في `DependencyInjection.cs` | ⬜ الشخص 1 عند الدمج |
| `medical booking.slnx` | ⬜ متعدل ومش متعمل له commit، يتكلم فيه مع الشخص 1 |

---

## 2. اللي اتعمل بالتفصيل

### 2.1 حساب المواعيد المتاحة (Slots)
- `SlotCalculator.Calculate` في `Clinic.Application/Features/Slots/`: بيحسب المواعيد من بداية الفترة لحد نهايتها بمدة الكشف، ويستبعد المحجوز والإجازات المتقاطعة.
- `SlotService.GetSlotsAsync`: بيرجّع قايمة فاضية لو الدكتور مفيهوش دوام في اليوم ده.
- `DoctorAvailabilityReader` في `Clinic.Infrastructure/Booking/`: بيقرأ الدوام من `WorkingSchedules`، والحجوزات من `Appointments` (من غير `Cancelled` و`Expired`)، والإجازات من `TimeOffs`، ويحوّل الأوقات بين UTC وتوقيت القاهرة عن طريق `ClinicTime`.
- **Endpoint:** `GET /api/doctors/{doctorId}/slots?date=`.

### 2.2 الحجز (`AppointmentService.BookAsync`)
التحققات بالترتيب:
1. الميعاد لازم يكون في المستقبل (وإلا 400). ده الإصلاح اللي اتعمل بعد ما اتكشف في تجربة.
2. الدكتور موجود (404).
3. الدكتور معتمد من الإدارة `IsApproved` (400).
4. حساب الدكتور شغال (400).
5. الميعاد جوه أي فترة دوام في اليوم، حتى لو الدكتور عنده فترتين (400، ورسالة فيها الوقت المطلوب والفترات).
6. الميعاد على شبكة الكشف بتاعة الفترة (400).
7. مفيش حجز نشط على نفس الميعاد (409).

الحفظ:
- الدفع أونلاين: الحالة `PendingPayment` و`ExpiresAt` بعد 15 دقيقة.
- الدفع في العيادة: الحالة `Confirmed` فوراً.
- `PriceSnapshot` بيتحفظ من `ConsultationFee` وقت الحجز.
- الحجز من نوع `FollowUp` بيتعامل في 2.7.

**التزامن:** Unique Filtered Index على `(DoctorId, StartAt)` مع فلتر `[Status]<>4 AND [Status]<>6`. لو الداتابيز رفضت الحفظ (`DbUpdateException`) بيتحوّل لـ 409.

### 2.3 إلغاء المريض (`CancelByPatientAsync`)
- ملكية الموعد (403)، والحالة `PendingPayment` أو `Confirmed` بس (409)، وقبل الموعد بساعتين على الأقل (409).
- بعده: `Cancelled` و`CancelledAt` و`CancelReason`، والميعاد بيترجع متاح للحجز تاني.
- بعد الإلغاء بيتبلّغ أول مريض في قايمة الانتظار (انظر 2.8).

### 2.4 إلغاء الدكتور (`CancelByDoctorAsync`)
- الدكتور صاحب الموعد بس (403)، والحالة `PendingPayment` أو `Confirmed` بس (409).
- **مفيش قاعدة ساعتين للدكتور.**
- بيفتح الميعاد، وبيبلّغ أول مريض في قايمة الانتظار.
- **مؤجل:** إشعار المريض نفسه بإلغاء الدكتور، لحد ما يتعمل نظام الإشعارات العام.

### 2.5 تحويلات الحالة
| العملية | من الحالة | لمين | صلاحية |
|---|---|---|---|
| check-in | `Confirmed` | `CheckedIn` | الدكتور صاحب الموعد، أو الريسبشن في نفس العيادة |
| complete | `CheckedIn` | `Completed` | الدكتور صاحب الموعد |
| no-show | `Confirmed` (وبعد بداية الموعد) | `NoShow` | الدكتور صاحب الموعد |

- الصلاحيات بتتفحص في `AppointmentsController` عن طريق `GetClinicScopeAsync` ومقارنتها بـ `ICurrentUser`.
- الأدمن مش مسموح له بالتحويلات دي (مطابق لـ `decisions.md`).

### 2.6 الحجوزات المنتهية تلقائياً (Expiry)
- `ExpirePendingAsync` بيحوّل `PendingPayment` اللي انتهت مهلته لـ `Expired`، ومش بيلمس `Confirmed` أبداً.
- `ExpirePendingJob` (Infrastructure): كل دورة في scope جديد، وبيسجّل الخطأ لو حصل بدل ما يتجاهله (`ILogger`).
- `ExpirePendingHostedService` (API): بيشغّل الـ Job كل دقيقة، ومسجّل في `Program.cs`.
- **متأكد بالداتابيز:** حجز أونلاين انتهت مهلته اتحول لـ `Expired` تلقائياً.

### 2.7 كشف المتابعة (FollowUp)
القاعدة اللي اتفقنا عليها:
- لو الحجز `FollowUp` ولقينا كشف `Completed` سابق لنفس المريض والدكتور، والميعاد الجديد داخل `FollowUpWindowDays` يوم من **نهاية** الكشف السابق: السعر = `ConsultationFee × FollowUpFeePercent / 100`، ويتعبّى `PreviousAppointmentId`.
- غير كده (مفيش كشف سابق مكتمل، أو برا المدة): بيتحوّل لكشف عادي `New` بالسعر الكامل، وبدون خطأ.
- لو النسبة 0 يبقى المتابعة مجانية.

**متجرّب:** كشف أول مكتمل (حجز 7)، ومتابعة بعده بـ 7 أيام بسعر 150 (50%)، و`PreviousAppointmentId` صح.

### 2.8 قائمة الانتظار
القرارات المتفق عليها:
- المريض يسجّل في القايمة بـ `DoctorId` + `DesiredDate`. مش مرتبطة بميعاد معين.
- لما ميعاد يتفتح (إلغاء من المريض أو الدكتور): **أول مريض بس** في القايمة يتبلّغ.
- الإشعار بيتبعت **فوراً** (مش Job دوري).
- المريض اللي اتبلّغ ما بيتحجزش تلقائياً، لازم يحجز بنفسه.

اللي اتعمل:
- `WaitlistService.JoinAsync`: يسجّل المريض، ولو مسجّل قبل كده على نفس الدكتور والتاريخ يرجّع 409.
- `WaitlistService.NotifyFirstForOpenSlotAsync`: يجيب أول مريض ما اتبلّغش، ويسجّل `Notification` (نوع `Waitlist`، قناة `Email`)، ويبعت الإيميل عن طريق `IEmailSender`، ويحدّث `NotifiedAt`.
- `AppointmentService` بيستدعي الإشعار بعد أي إلغاء (المريض أو الدكتور).
- `POST /api/waitlist` (المريض بس)، والـ body: `{ "doctorId": 1, "desiredDate": "2026-11-17" }`.

**متجرّب من Swagger:** التسجيل 201، والتكرار 409، والإلغاء بيبلّغ `patient1` (أول واحد في القايمة) وبيسجّل `Notification` في الداتابيز، و`NotifiedAt` بيتحدّد، والـ log بيطبع "Email not sent".

### 2.9 قراءة المواعيد
- `GET /api/appointments/me`: مواعيد المريض الحالي بس، مرتبة من الأقرب للأبعد.
- `GET /api/doctors/me/appointments?date=`: مواعيد الدكتور الحالي في اليوم ده بتوقيت القاهرة (الـ route في `DoctorAppointmentsController`، مش في `DoctorsController` بتاع الشخص 1).

### 2.10 الـ Endpoints اللي شغالة (ملخص)
| Method | Route | الصلاحية | الحالة |
|---|---|---|---|
| GET | `/api/doctors/{doctorId}/slots?date=` | عام | ✅ |
| POST | `/api/appointments` | Patient | ✅ |
| GET | `/api/appointments/me` | Patient | ✅ |
| POST | `/api/appointments/{id}/cancel` | Patient | ✅ |
| POST | `/api/appointments/{id}/check-in` | Doctor (owner) / Receptionist (same clinic) | ✅ |
| POST | `/api/appointments/{id}/complete` | Doctor (owner) | ✅ |
| POST | `/api/appointments/{id}/no-show` | Doctor (owner) | ✅ |
| GET | `/api/doctors/me/appointments?date=` | Doctor | ✅ |
| POST | `/api/doctors/me/appointments/{id}/cancel` | Doctor (owner) | ✅ |
| POST | `/api/waitlist` | Patient | ✅ |

---

## 3. نتائج الاختبارات

**الإجمالي:** 47 اختبار ناجح، صفر فشل.

| المجال | ملف الاختبار | الغرض |
|---|---|---|
| حساب الـ Slots | `SlotCalculatorTests.cs` | الحساب مع الحجوزات والإجازات والشبكة |
| خدمة الـ Slots | `SlotServiceTests.cs` | الخدمة مع Fake reader |
| قراءة الداتابيز | `DoctorAvailabilityReaderTests.cs` | تحويل الوقت UTC/القاهرة، والإلغاء مش بيتحسب محجوز |
| الحجز والإلغاء | `AppointmentServiceTests.cs` | كل التحققات، التعارض، الإلغاء، قاعدة الساعتين |
| الحجز في الماضي | `AppointmentPastBookingTests.cs` | ميعاد بدأ أو فات بيترفض |
| التزامن | `AppointmentConcurrencyTests.cs` | حجزين متوازيين على SQL Server، واحد بس ينجح (مشروط بمتغير البيئة) |
| تحويلات الحالة | `AppointmentStatusTests.cs` | check-in و complete و no-show وقواعدها |
| الـ Expiry | `AppointmentExpiryTests.cs` و `ExpirePendingJobTests.cs` و `ExpirePendingJobLoggingTests.cs` | الحجوزات المنتهية، والتسجيل عند الخطأ |
| القراءة | `AppointmentQueriesTests.cs` | مواعيد المريض والدكتور، وتوقيت يوم الدكتور بالقاهرة |
| إلغاء الدكتور | `AppointmentDoctorCancelTests.cs` | ملكية الموعد، والحالة، ومفيش قاعدة ساعتين |
| المتابعة | `AppointmentFollowUpTests.cs` | الخصم داخل المدة، والتحويل لـ New برا المدة أو من غير كشف سابق، والنسبة 0 |
| قائمة الانتظار | `WaitlistTests.cs` | التسجيل، ومنع التكرار، وإشعار أول مريض بس، والقايمة الفاضية |
| ربط الإلغاء بقائمة الانتظار | `WaitlistLinkTests.cs` | إلغاء المريض والدكتور بيبلّغ، والقايمة الفاضية ما بتبعتش حاجة |

**ملاحظة:** الاختبارات بتستخدم SQLite في الذاكرة، ما عدا اختبار التزامن اللي بيحتاج SQL Server (`ClinicDb`).

**تشغيل الاختبارات:**
```powershell
dotnet test
```

اختبار التزامن على SQL Server:
```powershell
$env:CLINIC_TEST_CONNECTION = "Server=localhost\SQLEXPRESS;Database=ClinicDb;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test --filter "FullyQualifiedName~AppointmentConcurrencyTests" -v normal
```
لو مدة التشغيل أقل من 100 ms يبقى الاختبار اتعدّى من غير ما يشتغل.

---

## 4. الـ Migration والداتابيز

- `InitialCreate` (في `Clinic.Infrastructure/Persistence/Migrations`) فيها كل الجداول اللي محتاجينها، بما فيها `WaitlistEntries` و`Notifications`، والـ Unique Filtered Index على `(DoctorId, StartAt)` بالفلتر الصح.
- اتعملت `ClinicDb` من الـ Migration من أول، والـ Index اتأكد بالاستعلام.
- الـ seed (`seed\seed-all-tables.sql`) اتشغّل يدوياً على `ClinicDb`، والحسابات `@test.local` بتتعمل من الـ Seeder عند تشغيل الـ API في Development.
- **مش محتاجين Migration جديدة لشغل الشخص 2.**
- **لو الشخص 1 هيضيف جدول OTP، هيعمل Migration جديدة خاصة بيه.**

---

## 5. ملاحظات وتحسينات (مش بتمنع الدمج)

1. **`TestDbContext` محلي في الاختبارات:** بيوقف توليد `RowVersion` لأن SQLite مش بيدعمه زي SQL Server. الكود الحقيقي ما اتغيرش، ومتكررة في كل ملف اختبار (تحسين ممكن: نعمله ملف مشترك).
2. **`NotifiedAt` بيستخدم `DateTime.UtcNow`** مش `TimeProvider`. تحسين بسيط.
3. **`Type` بيتحفظ في الحجز، والـ DTO الحالي ما بيرجعوش** (`type` و`previousAppointmentId`). لو عايزين نرجعهم، نضيفهم للـ `AppointmentDto`.
4. **التعليق العربي في `AppointmentConfiguration.cs`:** ترميز غلط، شكلياً فقط، ومش بيأثر على التشغيل.
5. **بيانات تجريبية في `ClinicDb`:** فيها حجوزات كتير من التجارب، تتنظف قبل أي demo.
6. **الـ Start Time بيرجع `Z`** بعد الإصلاح (في الـ Controllers عن طريق `SpecifyKind`). لو الـ DTO اتغيّر لاحقاً لازم نحافظ عليه.

---

## 6. قرارات مفتوحة (مش بتمنع الدمج)

- **وقت الـ check-in المبكر:** حالياً أي وقت. لو عايزين نحدد (مثلاً 30 دقيقة قبل الموعد)، ده قرار يتسجل في `decisions.md`.
- **مكان ملاحظات الكشف `VisitNotes`:** مفتوح، ومتعلق بالـ Migration.
- **إشعار المريض بإلغاء الدكتور:** مؤجل لحد نظام الإشعارات العام.
- **أول متابعة مجانية؟ ومحسوبة من تاريخ إيه؟** الاتفاق الحالي: المدة من نهاية الكشف السابق، والنسبة من الدكتور. لو الاتفاق يتغير، ده تعديل صغير.
- **Frontend (MVC / React / Angular)، وAI لاقتراح التخصص:** قرارات عامة للمشروع، مش لشغل الحجز.

---

## 7. تسليم الشخص 1: الـ OTP والإيميل

### 7.1 الفكرة
الـ Application بيعرف الإيميل من خلال **interface** واحد بس، `IEmailSender`، وده موجود بالفعل:

**`Clinic.Application/Common/Interfaces/IEmailSender.cs`**
```csharp
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
```

الـ Infrastructure هو اللي بينفّذه. دلوقتي فيه تنفيذ مؤقت اسمه `LoggingEmailSender` بيسجّل الإيميل في الـ log بس، وبيتسجل بـ `TryAdd` في `BookingModule`، يعني **أي تنفيذ حقيقي بيتسجل قبله هيتاخد الأولوية.**

**الاستخدامين:**
- **الـ OTP** (بتاعك): تأكيد الإيميل عند التسجيل.
- **قائمة الانتظار** (بتاعنا): إشعار "ميعاد اتفتح" للمريض.

الاتنين بيستخدموا نفس الـ `IEmailSender`، فمحتاجين تنفيذ واحد بس.

### 7.2 المطلوب منك (الشخص 1)

**الخطوة 1: تنفيذ SMTP حقيقي**
اعمل ملف في `Clinic.Infrastructure/Email/SmtpEmailSender.cs` بينفّذ `IEmailSender`، باستخدام MailKit أو `System.Net.Mail`. الإعدادات (Host، Port، User، Password) تتحط في User Secrets مش في الكود، زي `Jwt:Key`.

**الخطوة 2: تسجيله في `DependencyInjection.cs`**
```csharp
services.AddScoped<IEmailSender, SmtpEmailSender>();
```
وخليه قبل أو بعد `services.AddBookingModule();`، الاتنين صح، لأن `TryAdd` بيحترم التسجيل اللي موجود.

**الخطوة 3: الـ OTP**
الـ flow المقترح:
1. التسجيل (`POST /api/auth/register`) بيعمل المستخدم بـ `EmailConfirmed = false`، ويولّد كود 6 أرقام.
2. الكود بيتحفظ **مشفّر (hash)** مع تاريخ انتهاء (مثلاً 10 دقايق) وعداد للمحاولات (مثلاً 5).
3. الكود بيتبعت عن طريق `IEmailSender`.
4. `POST /api/auth/verify-otp` بيتحقق من الكود، ويخلّي `EmailConfirmed = true`.
5. `POST /api/auth/resend-otp` مع cooldown (مثلاً دقيقة) عشان ما يتسبمش.
6. تسجيل الدخول (`login`) يرفض الحساب اللي `EmailConfirmed = false`.

**ملاحظات أمنية مهمة:**
- **الكود ما يتسجلش في الـ log أبداً.** `LoggingEmailSender` الحالي بيسجّل الـ To والـ Subject بس، وده مقصود.
- الكود يتحفظ مشفّر، مش plain text.
- الكود يتمسح بعد الاستخدام أو عند انتهاء المدة.

**الخطوة 4: الـ Migration**
لو الـ OTP محتاج جدول (زي `EmailOtps`)، اعمل Migration جديدة. ومتعملش أي تعديل على `InitialCreate` الحالي.

**الخطوة 5: الاختبارات**
زي ما عملنا في قائمة الانتظار: Fake sender بيسجّل الإيميلات في قايمة، والاختبار بيتأكد إن الكود اتبعت للإيميل الصح.

### 7.3 الدمج (ترتيب الخطوات)
1. تسحب الفرع `feature/booking` وتعمل branch من عندك.
2. تنفّذ `SmtpEmailSender` وتسجّله في `DependencyInjection.cs`.
3. تضيف `services.AddBookingModule();` في `DependencyInjection.cs` (لو مش موجود).
4. تعمل الـ OTP (Flow + Migration + الاختبارات).
5. تتكلم مع الشخص 2 عن `medical booking.slnx` قبل الـ Pull Request، لأنه متعدل بإضافة مشروع الاختبارات.
6. تشغّل `dotnet test` والمتوقع كل الاختبارات تعدي.
7. تعمل Pull Request على `develop`.

### 7.4 تجربة الإيميل بعد ما تربطه
- سجّل مستخدم جديد، والإيميل المتوقع يوصل فعلاً.
- إلغاء حجز بعد ما مريض يكون في قائمة الانتظار، والإيميل يوصل للمريض الأول.
- لو الإيميل ما وصلش، الـ log لازم يوضح السبب (خطأ SMTP، مش صمت).

---

## 8. اللي لسه ومحتاج قرار قبل الدمج

| البند | المسؤول | الحالة |
|---|---|---|
| تنفيذ SMTP حقيقي (`IEmailSender`) | الشخص 1 | ⬜ |
| تسجيل `AddBookingModule()` في `DependencyInjection.cs` | الشخص 1 | ⬜ |
| OTP (تسجيل + تحقق + إعادة إرسال) | الشخص 1 | ⬜ |
| `medical booking.slnx` (إضافة مشروع الاختبارات) | الاتنين | ⬜ يتكلموا فيه |
| Pull Request على `develop` | الاتنين | ⬜ |
| تنظيف `ClinicDb` من البيانات التجريبية | الشخص 2 | ⬜ اختياري |
| تنظيف التعليق العربي في `AppointmentConfiguration.cs` | الشخص 2 | ⬜ اختياري |

---

## 9. الـ Commits المهمة على الفرع
- حساب الـ Slots، والـ reader، والـ DI module.
- `GET doctor slots endpoint`.
- الحجز مع التعارض والتزامن، والتحقق من الدوام والشبكة والاعتماد والحساب.
- الإلغاء من المريض، وقاعدة الساعتين، و`TimeProvider`.
- `POST /api/appointments` و`cancel` endpoint.
- الـ Expiry والـ Hosted Service، وتسجيل الأخطاء.
- تحويلات الحالة والـ endpoints مع الصلاحيات.
- إصلاحات: الحجز في الماضي، و`startAt` بـ `Z`.
- القراءة (مريض ودكتور).
- الإلغاء من الدكتور.
- كشف المتابعة.
- قائمة الانتظار (الـ Service، والـ Endpoint، والربط بالإلغاء).

**آخر commit:** `feat(booking): waitlist endpoint and logging email sender until SMTP is wired` (لو اتعمل).
