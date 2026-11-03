# المهام المتبقية

## قرارات مفتوحة
- [ ] ملاحظات الكشف (`VisitNotes`)
- [ ] تقنية الـ Frontend
- [ ] AI لاقتراح التخصص (اختياري)

## المراحل 0 إلى 4 (خلصت)
- [x] الـ packages والـ Serilog والـ User Secrets
- [x] الـ Entities والـ Configurations والـ Migrations والـ Seed
- [x] Application foundation (`IAppDbContext` و`ICurrentUser` و`Roles` و`ClinicTime` والـ Exceptions)
- [x] `GlobalExceptionHandler`
- [x] Auth: Register / Login / Me، JWT، الأدوار، قفل الحساب
- [x] CRUD التخصصات والعيادات
- [x] الأطباء: إنشاء، اعتماد، تعديل، بحث وفلترة
- [x] جدول العمل والإجازات (منع التقاطع)
- [x] الريسبشن (أدمن)
- [x] Swagger مع Authorize
- [x] اختبارات Swagger الأساسية (Auth والبحث والجدول والإجازات)
- [ ] اختبارات `docs/SWAGGER-TESTS.md` الكاملة (115) وقت الدمج
- [x] Unit tests (`tests/Clinic.Tests`، 55 اختبار)
- [x] المشروع على GitHub (`main`)
- [ ] فرع `develop` + Branch protection (اللّيدر)

## المرحلة الجاية
- [ ] OTP بالإيميل (شخص 1)
- [ ] الشخص 2: الـ Slots، الحجز بدون تعارض، الإلغاء، قائمة الانتظار، تحويلات الحالة، Hosted Service
- [ ] الشخص 3: الدفع، السجل الطبي والمرفقات، الكوبونات
- [ ] `IPatientAccessGuard`

## حاجات لازم تتذكر في الكود
- [ ] Paymob Webhook: التحقق من HMAC قبل أي حاجة
- [ ] `Coupon.UsedCount`: زيادة آمنة من التزامن
- [ ] `Conversation.ExpiresAt` = وقت الكشف المكتمل + 14 يوم
- [ ] تشفير الحقول الحساسة في `MedicalHistory` و`Message.Body`، وتنضيف الـ Seed منهم
- [ ] ما تحطش أي secret في `appsettings.json`
- [ ] مسح `docs/pro.txt` (نسخة قديمة من `PROJECT.md`)
