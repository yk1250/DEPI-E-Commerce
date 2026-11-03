# تقرير الشخص 1: اللي اتعمل واللي لسه ناقص

> الشخص 1 = Auth + الأطباء + الـ Swagger (المراحل 3 و4 في `docs/TEAM.md`).
> الحالة: الكود كله اتكتب، واتجربت الاختبارات الأساسية على Swagger، والمشروع على GitHub (`main`). الـ Unit tests اتكتبت.

---

## 1. الخلاصة

| | |
|---|---|
| الـ Endpoints المنفذة | **33** |
| ملفات C# (من غير الـ Migrations) | **80** (17 Entity و9 Enums) |
| اختبارات Swagger مكتوبة بالنتيجة المتوقعة | **115** (في `docs/SWAGGER-TESTS.md`)، الأساسية منها اتجربت |
| Unit tests (`tests/Clinic.Tests`) | **55** اختبار بتشتغل بـ `dotnet test` من غير داتابيز |
| Migrations جديدة | **0** (الـ Unique Index كان في `InitialCreate`) |
| NuGet packages جديدة | **0** (كل اللي اتكتب بيستخدم الـ packages اللي كانت موجودة) |

---

## 2. اللي اتعمل

### 2.1 الأساس (Application layer)
- **`IAppDbContext`:** الـ Application بيوصل للداتابيز من غير Repositories. فيه كمان `UserSummaries` (الاسم والإيميل وحالة الحساب، قراءة بس) عشان الـ Application ما يعرفش `ApplicationUser`.
- **`ICurrentUser`** و**`Roles`** و**`CustomClaims`**: هوية المستخدم والأدوار الأربعة وأسماء claims الـ JWT.
- **الأخطاء:** Exceptions (404 و401 و403 و409 و400) و`GlobalExceptionHandler` بيحولها لـ ProblemDetails بشكل موحد.
- **`ClinicTime`** و**`TimeProvider`**: الوقت UTC في الداتابيز وأوقات العمل بتوقيت القاهرة.

### 2.2 Auth والأمان
| الموضوع | التفاصيل |
|---|---|
| التسجيل | المريض بيسجل نفسه. الحساب وملف المريض بيتعملوا في **Transaction واحدة** |
| الباسورد | 8 حروف على الأقل، حرف كبير وصغير ورقم (Identity بيتأكد) |
| Login | بيرجّع JWT (HS256) صلاحيته 60 دقيقة. الرسالة واحدة (`Invalid email or password`) سواء الإيميل غلط أو الباسورد، فمفيش كشف لحسابات موجودة |
| قفل الحساب | بعد 5 محاولات فاشلة لمدة 5 دقايق |
| JWT claims | `sub` و`email` و`name` و`role`، ومعاهم `patient_id` أو `doctor_id` أو `clinic_id` حسب الدور، فمحدش محتاج query عشان يعرف هو مين |
| الأدوار | الأربعة بتتعمل أوتوماتيك عند تشغيل الـ API، وحسابات `@test.local` بتاخد باسورد `Test@12345` في **Development بس** |
| الصلاحيات | `[Authorize(Roles = ...)]`: من غير token = 401، دور غلط = 403 |
| الـ Key | في User Secrets (`Jwt:Key`)، مش في الكود ولا `appsettings.json`. لو ناقص أو أقل من 32 حرف، التشغيل بيقف برسالة واضحة |

### 2.3 الـ Endpoints (33)
| المجموعة | العدد | الصلاحية |
|---|---|---|
| `/api/auth` (register, login, me) | 3 | عام / أي مستخدم |
| `/api/specialties` (قراءة + إضافة + تعديل + حذف) | 5 | القراءة عامة، الكتابة Admin |
| `/api/clinics` (نفس الشكل) | 5 | القراءة عامة، الكتابة Admin |
| `/api/doctors` (بحث، ملف، جدول) | 3 | عام |
| `/api/doctors/me/*` (ملفه، جدوله، إجازاته) | 7 | Doctor |
| `/api/admin/doctors` (إنشاء، قايمة، عرض، تعديل، اعتماد، سحب اعتماد) | 6 | Admin |
| `/api/admin/receptionists` (إنشاء، قايمة، عرض، حذف) | 4 | Admin |

### 2.4 قواعد الأعمال اللي اتنفذت
- **ظهور الدكتور في البحث:** معتمد + حسابه شغال + عيادته شغالة. غير المعتمد بيرجع 404 (مش 403) عشان ما نكشفش إنه موجود.
- **الدكتور الجديد:** بيتعمل من الأدمن **غير معتمد**، ولازم الأدمن يعتمده.
- **التخصصات والعيادات:** الحذف بيرجع 409 لو في دكاترة أو ريسبشن عليها. العيادة بتتوقف بـ `isActive = false` بدل الحذف.
- **البحث:** التخصص، العيادة، السعر (من/إلى)، التقييم، جزء من الاسم، ترتيب (تقييم، سعر، خبرة، اسم)، صفحات.
- **جدول العمل:** بيتستبدل كله مرة واحدة. الفترات الشغالة في نفس اليوم ماينفعش تتقاطع، والنهاية بعد البداية، والفترة مش أطول من نافذة العمل.
- **الإجازات:** مرفوضة (409) لو بتتقاطع مع إجازة تانية أو مع مواعيد `PendingPayment` أو `Confirmed` أو `CheckedIn`. بتتخزن UTC والـ API بيقبل `DateTimeOffset`.
- **الدكتور لنفسه:** بيعدل السعر ونسبة المتابعة ومدتها والنبذة والخبرة. التخصص والعيادة والاعتماد عند الأدمن بس.
- **إجازة دكتور تاني:** بترجع 404 مش 403.

### 2.5 Swagger والاختبارات
زر **Authorize** شغال (JWT)، والـ token بيفضل بعد الـ Refresh، والـ enums ظاهرة كنص، وكل endpoint عليه الـ Responses المتوقعة.

**Unit tests (55):** الجدول (تقاطع الفترات والحدود)، التوقيت (الشتا والصيف وفرق اليوم)، الـ JWT (الـ claims والتوقيع والانتهاء)، `GlobalExceptionHandler` (كل حالة ورقمها، ومنع تسريب تفاصيل الـ 500)، `CurrentUser`، وقواعد التحقق اللي بتحصل قبل الداتابيز.

### 2.6 قرارات أخدتها بنفسي
| القرار | السبب |
|---|---|
| `CurrentUser` في `Clinic.API` مش `Infrastructure` | محتاج `HttpContext` |
| Services عادية من غير MediatR | أقل كود وأوضح لـ 3 أشخاص، وMediatR بقى بترخيص تجاري |
| JWT مكتوب يدوي (HS256) من غير package | مفيش package جديد ولا مشاكل إصدارات |
| الدكاترة مفيهمش حذف، فيه `unapprove` | الدكتور ليه مواعيد وتقييمات مربوطة بيه |
| الـ Seeder بيحط باسورد الاختبار في Development بس | عشان تقدر تجرب بحسابات الـ Seed من غير ما تعمل Register |

### 2.7 التعديلات على ملفات موجودة (10 ملفات)
`AppExceptions.cs` و`IAppDbContext.cs` و`AppDbContext.cs` و`GlobalExceptionHandler.cs` و`appsettings.json` (إضافة قسم Jwt بس) و`Infrastructure/DependencyInjection.cs` و`Program.cs` و3 ملفات docs. باقي الملفات (الـ Entities والـ Configurations والـ Migration) **ما اتلمسوش**.

---

## 3. اللي لسه ناقص

### 3.1 ناقص شغل شخصين كاملين + تكملة للشخص 1

| الشخص | الشغل | الحجم النسبي |
|---|---|---|
| **2** | محرك الحجز | **كبير** (أصعب جزء في المشروع) |
| **3** | الدفع + السجل الطبي | **كبير** |
| **1** | تكملة بعد الدمج | **متوسط** |
| لسه من غير مسؤول | الفرونت، الإشعارات والرسائل | **كبير** |

### 3.2 الشخص 2: محرك الحجز (مرحلة 5)
- [ ] توليد الـ Slots من أوقات العمل مع استبعاد الإجازات والمواعيد (`GET /api/doctors/{id}/slots`)
- [ ] الحجز بـ Transaction و`RowVersion`. Online ينتهي `PendingPayment` 15 دقيقة، وAtClinic يبقى `Confirmed` فوراً
- [ ] حساب `PriceSnapshot` وكشف المتابعة (`FollowUpFeePercent` و`FollowUpWindowDays`)
- [ ] الإلغاء وفتح الميعاد، وقائمة الانتظار (إشعار لـ `WaitlistEntries` بنفس الدكتور والتاريخ)
- [ ] تحويلات الحالة: `/check-in` و`/complete` و`/no-show`
- [ ] Hosted Service لإلغاء `PendingPayment` بعد 15 دقيقة
- [ ] `GET /api/appointments/me` و`GET /api/doctors/me/appointments`
- [ ] **اختبارات التزامن** على SQL Server حقيقي (واحد بس بينجح، والتاني 409)

### 3.3 الشخص 3: الدفع + السجل الطبي (مراحل 6 و7)
- [ ] Paymob Sandbox + Webhook مع التحقق من HMAC قبل أي حاجة
- [ ] الدفع في العيادة (الريسبشن، جوه عيادته بس)، وتأكيد الموعد بعد الدفع (بينادي `Confirm` عند الشخص 2)
- [ ] الكوبونات: `validate` وCRUD للأدمن، و`UsedCount` بأمان من التزامن
- [ ] `MedicalHistory` بتشفير الحقول (AES) والمفتاح في User Secrets
- [ ] المرفقات (PDF وصور) بالتحقق من النوع والحجم
- [ ] قاعدة الوصول: الدكتور لمريض عنده معاه موعد (`Confirmed` أو `CheckedIn` أو `Completed`)، والأدمن والريسبشن مايشوفوش سجل طبي
- [ ] تنضيف الـ Seed من النص العادي في `MedicalHistories` و`Messages` بعد التشفير
- [ ] التقييمات (مرحلة 8، أقرب له)

### 3.4 الشخص 1: اللي فاضل عليه
- [x] اختبارات Swagger الأساسية
- [x] رفع المشروع على GitHub (`main`)
- [ ] تركيب مشروع الـ Unit tests وتشغيل `dotnet test`
- [ ] فرع `develop` وتفعيل Branch protection (مع اللّيدر)
- [ ] تشغيل باقي اختبارات Swagger (الـ 115) وقت الدمج، وتصليح أي حاجة تطلع
- [ ] دمج فروع الشخص 2 و3، وبعدها **Migration واحدة** فقط
- [ ] OTP بالإيميل (محتاج مزود إيميل) - آخر أولوية
- ~~Postman~~ اتشال (Swagger كفاية، و`swagger.json` بيتصدّر عند الحاجة)
- [ ] Localization (عربي / إنجليزي، RTL) والـ Caching (مرحلة 10)
- [ ] الإحصائيات والتقارير المالية للأدمن (مرحلة 11)

### 3.5 من غير مسؤول لحد دلوقتي (يتوزع بعد الدمج)
- [ ] **التقييمات** (`Reviews`): بعد كشف `Completed` بس، وتحدّث `AverageRating` و`ReviewsCount`
- [ ] **الإشعارات والتذكيرات** (بيوم وبساعة) + **SignalR** والرسائل الآمنة (14 يوم بعد الكشف)
- [ ] **الفرونت اند:** التقنية لسه ما اتحددتش (MVC ولا React ولا Angular)، والشاشات: Patient Portal، Doctor Dashboard، Admin Dashboard، Receptionist
- [ ] **المرحلة 12:** Standard API Response، والاختبارات، والمراجعة الأمنية، وتجهيز العرض
- [ ] (اختياري) اقتراح التخصص من الأعراض بالـ AI

### 3.6 قرارات لازم تتقفل
1. ملاحظات الكشف (`VisitNotes`): لو اتضافت لازم قبل الـ Migration الجاية.
2. تقنية الفرونت.
3. الـ AI: نضيفه ولا لأ.

---

## 4. أهم تحذيرات للفريق

- **ممنوع تعمل Migration على فرعك.** بتتعمل مرة واحدة بعد الدمج.
- **ممنوع تعدل** `Program.cs` و`DependencyInjection.cs` و`AppDbContext.cs` و`*.csproj` و`Entities/*` و`Enums/*` غير الشخص 1. كل شخص يعمل Module خاص بيه.
- **الأسرار** (connection string وJwt:Key ومفاتيح Paymob ومفتاح التشفير) في User Secrets بس.
- الـ `appsettings.json` فيه connection string باسم instance جهاز الشخص 1 (`localhost\sql2025`). كل واحد يعمل Override بـ `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..." --project Clinic.API`.
- الـ JWT بيحمل `doctor_id` و`clinic_id` وقت الـ Login. لو الأدمن غيّر عيادة دكتور، الـ token القديم يفضل بالقيمة القديمة لحد ما ينتهي.
