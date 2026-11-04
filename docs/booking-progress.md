# متابعة شغل الشخص 2: محرك الحجز (مرحلة 5)

**الفرع:** `feature/booking`
**نسبة الإنجاز التقريبية:** حوالي 40%

---

## أولاً: اللي خلص

### 1. حساب الـ Slots (B5b جزء 1)
- `SlotCalculator.Calculate` في `Clinic.Application/Features/Slots/`.
- بيحسب المواعيد من وقت البداية لحد النهاية بمدة الكشف.
- بيستبعد المواعيد المحجوزة (`bookedStarts`) والمواعيد اللي بتتقاطع مع إجازة (`timeOffs`).
- الاختبارات: 4 اختبارات في `tests/Clinic.Application.Tests/Slots/SlotCalculatorTests.cs`، كلها ناجحة.

### 2. خدمة الـ Slots (B5b جزء 2)
- `SlotService.GetSlotsAsync` بياخد `IDoctorAvailabilityReader`، ويرجّع قايمة فاضية لو الدكتور مفيهوش دوام في اليوم ده.
- `IDoctorAvailabilityReader` و`DoctorAvailability` (DTO) في نفس الفولدر.
- الاختبارات: 2 اختبار في `SlotServiceTests.cs`، باستخدام Fake reader.

### 3. قراءة الداتابيز للـ Slots (B5b جزء 3)
- `DoctorAvailabilityReader` في `Clinic.Infrastructure/Booking/`.
- بيقرأ الدوام من `WorkingSchedules`، والحجوزات من `Appointments` (بيستبعد `Cancelled` و`Expired`)، والإجازات من `TimeOffs`.
- بيحوّل الأوقات بين UTC وتوقيت القاهرة من `ClinicTime`.
- `BookingModule.AddBookingModule()` بيسجّل الخدمات دي في الـ DI.
- الاختبارات: 2 اختبار في `tests/Clinic.Application.Tests/Booking/DoctorAvailabilityReaderTests.cs` على SQLite في الذاكرة.

### 4. Endpoint الـ Slots
- `GET /api/doctors/{doctorId}/slots?date=` في `Clinic.API/Controllers/SlotsController.cs`.
- اتجرّب في Swagger لدكتور 1 يوم 2026-11-03، ورجّع الـ 8 مواعيد المتوقعة (16:00 لـ 19:30).

### 5. الحجز بدون تعارض (B7 جزء 1)
- `AppointmentService.BookAsync` في `Clinic.Application/Features/Appointments/`.
- بيتأكد إن الميعاد فاضي، وإلا بيرمي `ConflictException` (409).
- بيحفظ `PriceSnapshot` من `ConsultationFee` وقت الحجز.
- الدفع أونلاين: الحالة `PendingPayment` و`ExpiresAt` بعد 15 دقيقة.
- الدفع في العيادة: الحالة `Confirmed` فوراً.
- `BookAppointmentRequest` في نفس الفولدر.
- الاختبارات: 2 اختبار في `AppointmentServiceTests.cs`.

### 6. التزامن على الداتابيز (B7 جزء 2)
- لو الحفظ فشل بـ `DbUpdateException` (الـ Unique Index رفض الحجز المكرر)، بيتحوّل لـ `ConflictException`.
- الاختبار `AppointmentConcurrencyTests.cs` بيشغّل حجزين متوازيين على SQL Server الحقيقي، ولازم واحد بس ينجح.
- الاختبار بيتعدّى لو متغير البيئة `CLINIC_TEST_CONNECTION` مش موجود.
- **تم:** الـ Unique Index على `(DoctorId, StartAt)` مع فلتر `[Status]<>4 AND [Status]<>6` اتعمل يدوياً على `ClinicDb` بعد ما اتكشف إنه كان index عادي.

### 7. الـ Commits اللي اتعملت على الفرع
- `fde0caa` DI module and availability reader
- `807e94e` integration tests for availability reader on SQLite
- `b4304e7` GET doctor slots endpoint
- `1410902` reference Infrastructure and add SQLite for reader tests
- commit الحجز مع منع التعارض
- commit تحويل الـ DbUpdateException لـ 409
- commit اختبار التزامن على SQL Server

الفرع متزامن مع `origin/feature/booking` على GitHub.

---

## ثانياً: اللي باقي

### 1. Endpoints الحجز (أول حاجة نعملها)
- `POST /api/appointments`: بيستدعي `AppointmentService.BookAsync`، ويرجّع الموعد. محتاج Controller و`[Authorize(Roles = Patient)]`.
- `GET /api/appointments/me`: مواعيد المريض الحالي.
- `GET /api/doctors/me/appointments?date=`: مواعيد الدكتور الحالي. الـ route ده في controller الشخص 2 وليس في `DoctorsController`.

### 2. الإلغاء وفتح الميعاد (B8)
- `POST /api/appointments/{id}/cancel` للمريض أو الدكتور.
- عند الإلغاء: الحالة `Cancelled` و`CancelledAt` و`CancelReason`، والميعاد يتفتح.
- اختبارات: الإلغاء يفتح الميعاد، والمريض ما يقدرش يلغي موعد مش بتاعه.

### 3. قائمة الانتظار (B8)
- `POST /api/waitlist` للمريض.
- لما ميعاد يتفتح: يدور على `DoctorId` + `DesiredDate` ويبعت `Notification` من نوع `Waitlist`.
- **قرار مفتوح:** لازم نقفله في `docs/decisions.md` قبل الكود (الإشعار بيتعمل إزاي، ومين يبعته).

### 4. تحويلات حالة الموعد (B8)
- `POST /api/appointments/{id}/check-in` (Receptionist أو Doctor)
- `POST /api/appointments/{id}/complete` (Doctor)
- `POST /api/appointments/{id}/no-show` (Doctor)
- كل تحويل بيتحقق من الحالة الحالية (مثلاً `complete` مينفعش إلا من `CheckedIn`).

### 5. Hosted Service لإلغاء الحجوزات المنتهية (B11)
- `ExpirePendingAppointmentsJob` في `Clinic.Infrastructure/Jobs/`.
- بيحوّل `PendingPayment` اللي عدّى عليه `ExpiresAt` لـ `Expired`، والميعاد يتفتح.
- اختبار: حجز منتهي بيتحول، وحجز `Confirmed` ما يتلمسش.

### 6. كشف المتابعة
- حساب السعر بـ `FollowUpFeePercent` و`FollowUpWindowDays` من بيانات الدكتور.
- التحقق إن الكشف السابق مكتمل وفي نفس الفترة.

### 7. تسجيل الـ DI (مع الشخص 1 عند الدمج)
- إضافة `services.AddBookingModule()` في `DependencyInjection.cs`، بالإضافة لتسجيل `AppointmentService`.
- التسجيل المؤقت في `Program.cs` اتشال، فلازم يتعمل رسمياً عند الدمج.

### 8. ملفات مشتركة لازم نكلم الشخص 1 فيها
- `medical booking.slnx`: متعدل بإضافة مشروع الاختبارات، ولسه مش داخل أي commit.

### 9. تحسينات مؤجلة (مش بتمنع الدمج)
- استبدال `DateTime.UtcNow` بـ `TimeProvider` في `AppointmentService`، حسب `decisions.md`.
- حقل `Type` (`New` / `FollowUp`) لسه مش بيأثر على المنطق.
- الاختبار `RowVersion` على SQLite بيستخدم `TestDbContext` محلي، وده خاص بالاختبارات بس.
- الـ index على `ClinicDb` اتعمل يدوياً، والأفضل يتأكد إن الـ Migration الرسمية تطابقه عند الدمج.
- تعليق عربي في `AppointmentConfiguration.cs` مخزّن بترميز غلط، ومحتاج يتصلح في commit منفصل.

---

## ثالثاً: متطلبات لتشغيل الاختبارات

- `dotnet test` لكل الاختبارات (حالياً 10 في `Clinic.Application.Tests` غير اختبار التزامن، و+1 اختبار التزامن).
- اختبار التزامن محتاج متغير البيئة (ساعة ما نشتغل في Terminal جديد):

```powershell
$env:CLINIC_TEST_CONNECTION = "Server=localhost\SQLEXPRESS;Database=ClinicDb;Trusted_Connection=True;TrustServerCertificate=True"
```

---

## رابعاً: ترتيب الخطوات الجاية

1. `POST /api/appointments` وتجربته في Swagger.
2. قفل قرار قائمة الانتظار في `decisions.md`.
3. الإلغاء وفتح الميعاد (B8).
4. قائمة الانتظار والإشعارات.
5. تحويلات الحالة (check-in / complete / no-show).
6. Hosted Service (B11).
7. كشف المتابعة.
8. تنظيف: الـ TimeProvider، وترميز التعليق، وتجهيز الدمج مع الشخص 1.
