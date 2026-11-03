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
- [ ] تشغيل اختبارات `docs/SWAGGER-TESTS.md` كلها
- [ ] رفع المشروع على GitHub وتفعيل Branch protection

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
