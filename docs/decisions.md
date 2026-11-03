# قرارات المشروع

## مسجلة

### البيانات
- **PriceSnapshot:** السعر يتحفظ على `Appointment` وقت الحجز، ومبيتغيرش حتى لو الدكتور غيّر سعره.
- **مدة الشات:** 14 يوم بعد الكشف المكتمل، و`Conversation.ExpiresAt` يتحسب من الـ Application layer.
- **Restrict على كل علاقات الدومين:** عشان SQL Server ما يرفضش مسارات cascade متعددة.
- **منع الحجز المزدوج:** Unique Filtered Index على `(DoctorId, StartAt)` يستثني `Cancelled` و`Expired`، مع `RowVersion`.
- **قائمة الانتظار:** من غير حقل جديد. لما ميعاد يتفتح، يتدور على `DoctorId` + `DesiredDate` ويتبعت `Notification` نوع `Waitlist`.
- **الـ Seed:** `MedicalHistories` و`Messages` فيهم نص عادي. لما الشخص 3 يضيف التشفير يمسحهم من الـ Seed ويعيد إنشاءهم من الكود.

### المعمارية
- **الاعتماديات:** `Infrastructure` بيعتمد على `Application` و`Domain`.
- **الوصول للداتابيز:** من Application عن طريق `IAppDbContext` (مفيش Repositories). `UserSummaries` للقراءة بس (الاسم والإيميل).
- **الأنماط:** Services عادية في `Features/<Feature>`، من غير MediatR.
- **التحقق:** DataAnnotations على الـ DTOs، وقواعد الأعمال بـ `AppValidationException`.
- **الأخطاء:** Exceptions في `Common/Exceptions` + `GlobalExceptionHandler` (ProblemDetails): 400 و401 و403 و404 و409 و500.
- **الوقت:** التخزين UTC، وأوقات العمل بتوقيت القاهرة، والتحويل من `ClinicTime` بس، والوقت الحالي من `TimeProvider`.
- **الوثائق:** Swagger (`/swagger`) هو الواجهة، مش Scalar.

### الأمان والصلاحيات
- **OTP:** بالإيميل.
- **ICurrentUser:** `UserId` و`Role` و`PatientId` و`DoctorId` و`ClinicId`، والـ ids الثلاثة claims في الـ JWT (`CustomClaims`).
- **JWT:** HS256، الـ Key في User Secrets (`Jwt:Key`، 32 حرف على الأقل)، وصلاحيته 60 دقيقة من `appsettings.json`.
- **الأدوار:** دور واحد لكل مستخدم، والأسماء في `Roles`.
- **التسجيل:** المريض بيسجل نفسه. الدكتور والريسبشن بيتعملوا من الأدمن، والدكتور بيتعمل غير معتمد لحد ما الأدمن يعتمده.
- **قفل الحساب:** بعد 5 محاولات فاشلة لمدة 5 دقايق.
- **صلاحية السجل الطبي:** الدكتور لمريض عنده معاه موعد (`Confirmed` أو `CheckedIn` أو `Completed`) بس. الأدمن والريسبشن لا يشوفوا سجل طبي ولا مرفقات ولا رسائل. الريسبشن شغله جوه عيادته بس.
- **ظهور الدكتور في البحث:** معتمد + حسابه شغال + عيادته شغالة.
- **الإجازة:** مينفعش تتقاطع مع إجازة تانية أو مع مواعيد (`PendingPayment` أو `Confirmed` أو `CheckedIn`).

### الاختبارات والتوثيق
- **Postman:** اتشال. Swagger كفاية، و`swagger.json` بيتصدّر للجنة عند الحاجة.
- **Unit tests:** على المنطق اللي من غير داتابيز (`tests/Clinic.Tests`، xUnit). اختبارات التزامن والـ Integration على SQL Server حقيقي، مش InMemory.
- **اختبارات Swagger:** الأساسية اتعملت، والـ 115 كاملين وقت الدمج.

### توزيع المسؤوليات
- **تحويلات حالة الموعد:** مسؤولية الشخص 2 (`Confirm` و`CheckIn` و`Complete` و`NoShow`). الشخص 3 بينادي `Confirm` بعد الدفع.
- **طريقة الدفع وقت الحجز:** Online ينتهي `PendingPayment` 15 دقيقة. AtClinic يبقى `Confirmed` فوراً، وصف الدفع بيتعمل `Paid` لما الريسبشن يأكد.
- **Endpoints:** `/check-in` و`/complete` و`/no-show` عند الشخص 2. CRUD الكوبونات (الأدمن) عند الشخص 3.
- **Routes الدكاترة:** `/api/doctors/{id}/slots` و`/api/doctors/me/appointments` في controller تاني بتاع الشخص 2، من غير تعديل `DoctorsController`.

## مفتوحة
- مكان ملاحظات الكشف (المقترح: حقل `VisitNotes` على `Appointment`، ومشفر). لازم يتحسم قبل الـ Migration القادمة.
- Frontend: MVC ولا React ولا Angular.
- AI لاقتراح التخصص من الأعراض: نضيفه ولا لأ.
