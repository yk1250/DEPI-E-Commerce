# تقرير شغل الشخص 3: الدفع والكوبونات والسجل الطبي

**المشروع:** Clinic API (منصة حجز الكشوفات الطبية)
**الفرع:** `feature/payments-medical` (مبني فوق `feature/booking`)
**الحالة:** شغل الشخص 3 خلص من جهته، والباقي تسليم للدمج مع الشخص 1.
**آخر تشغيل كامل للاختبارات:** 354 اختبار، كلهم ناجحين (47 بتوع الشخص 2 و307 بتوع الشخص 3).

> الـ enums بترجع في الـ JSON كنصوص (`"Paid"` و`"AtClinic"` و`"Percent"`...). والوقت دايماً UTC وبينتهي بـ `Z`.

---

## 1. ملخص سريع

| المجال | الحالة |
|---|---|
| تشفير الحقول الحساسة (AES-256-GCM) | ✅ خلص ومتجرّب |
| السجل الطبي (مريض يكتب ويقرا، دكتور يقرا) | ✅ خلص ومتجرّب على Swagger |
| المرفقات (رفع وتحميل) | ✅ خلص ومتجرّب على Swagger |
| قاعدة "مين يشوف البيانات الطبية" (`IPatientAccessGuard`) | ✅ خلص ومتجرّب |
| الكوبونات (معاينة، خصم، استهلاك ذري) | ✅ خلص ومتجرّب |
| إدارة الكوبونات للأدمن (CRUD) | ✅ خلص ومتجرّب على Swagger |
| الدفع في العيادة | ✅ خلص ومتجرّب على Swagger |
| الدفع الأونلاين: بدء الدفع (Paymob) | ✅ خلص ومتجرّب على Paymob sandbox |
| الـ webhook والتحقق من التوقيع (HMAC) | ✅ خلص ومتجرّب بتوقيع حقيقي (انظر 3.3) |
| الدفع الفاشل (Failed) على Paymob نفسه | 🟡 متغطّي باختبارات الوحدة بس |
| إشعار Paymob الفعلي (server-to-server) على عنوان عام | ⬜ يتجرّب عند النشر (انظر 6) |
| تسجيل `AddMedicalModule` و`AddPaymentsModule` في `DependencyInjection.cs` | ⬜ الشخص 1 عند الدمج |
| الاسترجاع (refund) | ⬜ مش جوه النطاق (انظر 7) |

---

## 2. اللي اتعمل بالتفصيل

### 2.1 التشفير
- `IFieldEncryptor` (في `Application`) و`AesGcmFieldEncryptor` (في `Infrastructure/Security/Encryption/`): AES-256-GCM مع nonce عشوائي لكل تشفير، فنفس النص بيتشفر بشكل مختلف كل مرة. الصيغة المخزنة: Base64 من `nonce(12) + tag(16) + cipherText`. أي تعديل في النص المشفر بيفشل فك التشفير.
- `null` والنص الفاضي بيعدّوا من غير تشفير.
- المفتاح من `Encryption:Key` (Base64 لـ 32 بايت). لو ناقص أو غلط، التطبيق بيقف عند التشغيل برسالة واضحة.
- **لو المفتاح ضاع أو اتغيّر، كل البيانات المشفرة بيه مش هتتفك.**

### 2.2 السجل الطبي
الحقول الثلاثة (الأمراض المزمنة، الحساسية، الأدوية الحالية) بتتشفر قبل الحفظ وبتتفك عند القراءة، وحد أقصى 2000 حرف للحقل.
- `GET /api/medical-history/me` (مريض): سجله هو، وفاضي لو ما كتبش حاجة.
- `PUT /api/medical-history/me` (مريض): بيستبدل السجل كله، والحقل الفاضي أو المسافات بتتحفظ `null`. الـ `PatientId` بيتاخد من الـ token مش من الـ request.
- `GET /api/patients/{id}/medical-history` (دكتور): للمريض اللي عنده معاه موعد بس (انظر 2.4).
- كل الردود فيها `Cache-Control: no-store`.

### 2.3 المرفقات
- **الأنواع:** PDF وJPG وPNG بس، حد أقصى 10 ميجا، و10 مرفقات للموعد.
- **التحقق على 4 مستويات:** الحجم، والامتداد، ونوع المحتوى المُعلن، وأول بايتات الملف الحقيقية (فملف exe متسمّي `.pdf` بيترفض). واسم الملف بيتنضّف من أي مسار.
- **التخزين:** `IFileStorage` و`LocalFileStorage` بيحفظ باسم Guid عشوائي في `سنة/شهر/` (الاسم اللي كتبه المستخدم ما بيدخلش في المسار)، وبيرفض أي مسار برا مكان التخزين. المكان الافتراضي `%LocalAppData%\ClinicApi\attachments` (برا المشروع)، ويتغيّر بـ `Attachments:RootPath`.
- `POST /api/appointments/{appointmentId}/attachments` (مريض، multipart): لموعد بتاعه بس (موعد غيره 404)، ومش لموعد ملغي أو منتهي أو غاب عنه (409). لو حفظ الداتابيز فشل، الملف بيتمسح.
- `GET /api/attachments/{id}` (مريض أو دكتور): المريض صاحبه أو دكتور معاه موعد. بيرجّع الملف كتحميل (`Content-Disposition: attachment`) مع `X-Content-Type-Options: nosniff`.

### 2.4 قاعدة "مين يشوف البيانات الطبية" (`IPatientAccessGuard`)
| الدور | بيشوف |
|---|---|
| مريض | بياناته هو بس |
| دكتور | مريض عنده معاه موعد حالته `Confirmed` أو `CheckedIn` أو `Completed` |
| أدمن / ريسبشن / أي دور تاني | ممنوع دايماً |
| من غير تسجيل دخول | 401 |

الموعد `PendingPayment` أو `Cancelled` أو `NoShow` أو `Expired` ما بيدّيش الدكتور صلاحية.

### 2.5 الكوبونات
- **المعاينة:** `POST /api/coupons/validate` (مريض) بياخد `code` و`appointmentId`، ويرجّع الخصم والمبلغ النهائي من غير ما يستهلك الكوبون. كل أسباب الرفض (مش موجود، متعطل، منتهي، خلص) بترجع نفس رسالة 400 عشان محدّش يخمّن أكواد.
- **الحساب:** النسبة بتتقصّ على 100%، والخصم الثابت مش بيعدّي السعر، والتقريب لأقرب قرشين، و`Amount = max(0, OriginalAmount - DiscountAmount)`.
- **الاستهلاك الذري:** `ICouponRedeemer` (في `Infrastructure`) بينفّذ `UPDATE Coupons SET UsedCount = UsedCount + 1 WHERE Id = @id AND IsActive = 1 AND ExpiresAt > @now AND UsedCount < MaxUses` في أمر واحد، فلو اتنين حاولوا ياخدوا آخر استخدام واحد بس بينجح. من غير migration.
- **متى بيتستهلك:** وقت تأكيد الدفع (في العيادة أو بعد نجاح الدفع الأونلاين) مش وقت المعاينة.
- **CRUD الأدمن** (`api/admin/coupons`): قايمة، وكوبون واحد، وإنشاء، وتعديل، وحذف.
  - الكود 3 إلى 30 حرف (إنجليزي وأرقام و`-` و`_`)، بيتحوّل لحروف كبيرة، والمكرر 409، وبعد الإنشاء ما بيتغيرش.
  - النسبة مش فوق 100، وتاريخ الانتهاء في المستقبل عند الإنشاء (وفي التعديل مسموح بتاريخ قديم، وده بيخلّي الكوبون ينتهي فوراً).
  - `MaxUses` في التعديل لازم يكون أكبر من أو يساوي `UsedCount`، و`UsedCount` مش بيتعدّل من الـ API.
  - **الحذف:** الكوبون اللي اتستخدم أو مرتبط بدفع ما بيتحذفش (409) وبيتعطّل بـ `PUT` (`isActive: false`).

### 2.6 الدفع في العيادة
`POST /api/payments/clinic/{appointmentId}/confirm` (ريسبشن)، والـ body اختياري فيه `couponCode`:
- الريسبشن شغله في **عيادته بس** (عيادة تانية 403)، والموعد `Confirmed` أو `CheckedIn` (غير كده 409).
- بيعمل صف `Payment` بـ `Method = AtClinic` و`Status = Paid`، والمبلغ الأصلي من `PriceSnapshot`.
- استهلاك الكوبون وحفظ الدفع في **transaction واحدة**.
- **الدفع المزدوج:** أي صف دفع موجود للموعد بيرفض بـ 409، وحتى لو ريسبشنين أكدوا في نفس اللحظة الـ Unique Index على `AppointmentId` بيخلي واحد بس ينجح.

### 2.7 الدفع الأونلاين: بدء الدفع
`POST /api/payments/online` (مريض)، والـ body فيه `appointmentId` و`couponCode` اختياري:
- الموعد بتاع المريض (غير كده 404) وحالته `PendingPayment` ومهلته لسه فاضل منها دقيقة على الأقل (غير كده 409).
- بيحسب الخصم، ولو المبلغ النهائي صفر بيرفض (400)، لأن Paymob مش بيقبل مبلغ صفر.
- `PaymobClient` بينفّذ 3 طلبات لـ Paymob بالترتيب: توكن المصادقة، وتسجيل الأوردر (`merchant_order_id` = `A{رقم الموعد}-{جزء عشوائي}`)، ومفتاح الدفع (مدته مش بتعدّي الوقت المتبقي للموعد وحد أقصى ساعة). وبيرجّع رابط الـ iframe.
- بيتحفظ صف `Payment` بحالة `Pending` و`PaymobOrderId` (أو بيتعاد استخدامه لو المحاولة السابقة `Pending` أو `Failed`).
- لو مفاتيح Paymob ناقصة، التطبيق بيشتغل عادي والدفع الأونلاين بس هو اللي بيرجّع خطأ واضح (`UnconfiguredPaymobClient`).

### 2.8 الـ webhook
`POST /api/payments/webhook/paymob` (من غير تسجيل دخول، الأمان بالتوقيع):
1. **التوقيع أول حاجة:** HMAC-SHA512 على 20 حقل بترتيب Paymob (`amount_cents` ... `success`)، بمقارنة ثابتة الزمن مع `?hmac=`. توقيع غلط أو ناقص: 401 ومفيش أي تغيير في الداتابيز.
2. **الربط بالدفع بـ `order.id` الموقّع بس.** حقل `merchant_order_id` **مش ضمن الحقول الموقّعة**، فأي حد معاه إشعار حقيقي لدفعته هو ممكن يغيّره عشان يأكد دفع حد تاني، فمش بنستخدمه خالص.
3. **بيتحقق** من رقم التكامل والعملة والمبلغ (`amount_cents`)، وإلا بيتجاهل الإشعار (200) ويسجّل خطأ.
4. **الدفع الناجح:** تغيير الحالة لـ `Paid` بأمر UPDATE واحد بشرط (`IPaymentSettler`)، فإشعارين متكررين ما ينجحوش الاتنين. بعده استهلاك الكوبون وتأكيد الموعد، الكل في transaction واحدة.
5. **الفاشل** (`success=false` أو `error_occured=true`): `Failed`، وممكن بعده ينجح لو العميل جرّب كارت تاني. و`pending` وإشعارات الـ refund والـ void بتتتجاهل.
6. **الرد:** 401 للتوقيع الغلط، و200 لأي إشعار سليم (حتى لو مفيش حاجة تتعمل)، و500 لأي خطأ مؤقت عشان Paymob يعيد الإرسال.
7. `IAppointmentConfirmer`: `PendingPayment` يبقى `Confirmed` ويتمسح `ExpiresAt`، والمؤكد بالفعل ما يحصلش حاجة، و`Expired` أو `Cancelled` أو `NoShow` يرمي `ConflictException`. **لو الشخص 2 رفع `ConfirmAsync` في `AppointmentService`، نبدّل التسجيل في `PaymentsModule` بسطر واحد من غير ما نمس الـ webhook.**

### 2.9 الـ Endpoints (ملخص)
| الطريقة | المسار | الدور |
|---|---|---|
| GET / PUT | `/api/medical-history/me` | مريض |
| GET | `/api/patients/{id}/medical-history` | دكتور |
| POST | `/api/appointments/{id}/attachments` | مريض |
| GET | `/api/attachments/{id}` | مريض، دكتور |
| POST | `/api/coupons/validate` | مريض |
| GET / POST | `/api/admin/coupons` | أدمن |
| GET / PUT / DELETE | `/api/admin/coupons/{id}` | أدمن |
| POST | `/api/payments/clinic/{appointmentId}/confirm` | ريسبشن |
| POST | `/api/payments/online` | مريض |
| POST | `/api/payments/webhook/paymob` | من غير تسجيل دخول (HMAC) |

---

## 3. نتائج الاختبارات

### 3.1 اختبارات الوحدة (354 كلهم ناجحين)
| الملف | العدد |
|---|---|
| تشفير + قواعد المرفقات + تخزينها | 37 |
| `IPatientAccessGuard` | 18 |
| السجل الطبي | 17 |
| خدمة المرفقات | 29 |
| حسابات الكوبونات + خدمة الكوبونات | 43 |
| إدارة الكوبونات (أدمن) | 36 |
| الدفع في العيادة | 25 |
| `PaymobClient` (بـ HTTP مزيّف) | 13 |
| بدء الدفع الأونلاين | 27 |
| توقيع HMAC (منهم اختبار بقيمة محسوبة بتنفيذ مستقل) | 19 |
| تغيير حالة الدفع الذري + تأكيد الموعد | 16 |
| الـ webhook | 27 |
| باقي الاختبارات (الشخص 2) | 47 |

الاختبارات على SQLite في الذاكرة. **اختبار تزامن حقيقي على SQL Server (استهلاك الكوبون وتأكيد الدفع من طلبين في نفس اللحظة) مش متكتب**: الضمان من أوامر SQL الشرطية والـ Unique Index، والاختبارات بتثبت الشرط بشكل متتابع.

### 3.2 التجارب على Swagger وSQL Server الحقيقي
السجل الطبي والمرفقات (كل الصلاحيات)، والكوبونات (معاينة وCRUD)، والدفع في العيادة بكوبون (الكوبون زاد مرة واحدة، والتأكيد التاني اترفض 409)، ورفض المريض والريسبشن والأدمن ومن غير token.

### 3.3 تجربة Paymob sandbox
- حجز موعد أونلاين، وبدء الدفع بكوبون (`WELCOME10`، المبلغ 270)، ودفع حقيقي بكارت الاختبار على صفحة Paymob.
- **التوقيع:** توقيع Paymob الحقيقي من رابط الرجوع اتطابق (`MATCH`) مع تنفيذنا (ترتيب الحقول وتنسيق القيم)، واتأكد منه بتنفيذ مستقل (Python) كمان.
- **الـ webhook:** الإشعار المبني من رابط الرجوع (نفس التوقيع الحقيقي) رجّع 200، وإعادة إرساله 200، وتعديل المبلغ 401، ومن غير توقيع 401، وتوقيع غلط 401.
- **الداتابيز بعدها:** الدفع `Paid` ومعاه رقم العملية، والموعد `Confirmed` و`ExpiresAt` فاضي، و`UsedCount` للكوبون زاد **واحد بس** رغم الإرسال مرتين.
- **ملاحظة صريحة:** الإشعار اتبعت من جهازنا (محاكاة من رابط الرجوع)، مش من سيرفر Paymob، لأن Paymob مش بيوصل لـ `localhost`. ده بيثبت التوقيع ومنطق المعالجة، لكن ضبط عنوان الـ callback واستقبال الإشعار الفعلي يتجرّبوا عند النشر (انظر 6).

---

## 4. الداتابيز
- **مفيش Migration جديدة.** الجداول (`Payments` و`Coupons` و`MedicalHistories` و`Attachments`) موجودة في `InitialCreate`.
- **ملف الـ Seed:** اتشالت منه سجلات `MedicalHistories` لأنها كانت نص عادي (مش مشفر). السجل بيتعمل من الـ API (`PUT /api/medical-history/me`). **لو عندك داتابيز اتعملت بالـ seed القديم**، امسح الصفوف دي قبل ما تقرا السجل الطبي، وإلا فك التشفير هيفشل:
  ```sql
  DELETE FROM dbo.MedicalHistories;
  ```
- صف المرفق اللي في الـ seed مساره مش موجود على القرص، فتحميله بيرجّع 404 (مقصود).

---

## 5. الإعدادات والأسرار (User Secrets، **مش في الريبو**)
| الإعداد | مطلوب؟ | ملاحظة |
|---|---|---|
| `Encryption:Key` | **نعم** (التطبيق مش بيشتغل من غيره) | Base64 لـ 32 بايت، ولو ضاع البيانات المشفرة مش هتتفك |
| `Paymob:ApiKey` | للدفع الأونلاين | |
| `Paymob:HmacSecret` | للدفع الأونلاين، وللتحقق من الـ webhook | من غيره كل الإشعارات بتترفض (401) |
| `Paymob:IntegrationId` | للدفع الأونلاين | رقم صحيح |
| `Paymob:IframeId` | للدفع الأونلاين | رقم صحيح |
| `Paymob:BaseUrl` / `Paymob:Currency` | اختياري | الافتراضي `https://accept.paymob.com` و`EGP` |
| `Attachments:RootPath` | اختياري | الافتراضي `%LocalAppData%\ClinicApi\attachments` |

توليد مفتاح التشفير (PowerShell):
```powershell
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Convert]::ToBase64String($bytes)
```

---

## 6. تسليم الشخص 1: الدمج (ترتيب الخطوات)

1. **ترتيب الدمج:** `feature/booking` الأول، وبعده `feature/payments-medical`. فرع الدفع مبني فوق الحجز، فلو اتفتح PR على `develop` قبل دمج الحجز هيشمل commits الشخص 2 كمان.
2. في `Clinic.Infrastructure/DependencyInjection.cs` تحت `services.AddBookingModule();` ضيف:
   ```csharp
   services.AddMedicalModule(configuration);
   services.AddPaymentsModule(configuration);
   ```
   ومفيش تعديل في `Program.cs` (الـ Controllers بتتسجّل أوتوماتيك).
3. ظبّط الأسرار في الجدول فوق (`Encryption:Key` أولاً). **الأسرار بتتسلّم بطريقة آمنة، مش في الريبو ولا في الشات.**
4. شغّل `dotnet test` (المفروض 354 ناجح) و`dotnet build`.
5. **عند النشر:** في لوحة Paymob، ظبّط "Transaction processed callback" على `https://<الدومين>/api/payments/webhook/paymob`. الدومين لازم يكون عام ومعاه HTTPS (الـ API فيها `UseHttpsRedirection`). وبعد الضبط، جرّب دفعة بكارت الاختبار وتأكد إن الدفع بقى `Paid` لوحده.
6. **أمان:** مفاتيح Paymob اللي اتستخدمت في التطوير اتكتبت في محادثة، فاعمل لها إعادة إنشاء من لوحة Paymob (API Key وHMAC secret) قبل أي استخدام حقيقي.

---

## 7. حدود معروفة وقرارات مفتوحة (مش بتمنع الدمج)

| # | الموضوع | الوضع الحالي |
|---|---|---|
| 1 | الفلوس وصلت والموعد اتلغى أو انتهى | الدفع بيتسجّل `Paid` والموعد بيفضل زي ما هو، وبيتسجّل خطأ في اللوج إن الدفعة **محتاجة استرجاع يدوي**. مفيش حالة تانية في الداتابيز نسجّل بيها ده. |
| 2 | الاسترجاع (refund) والإلغاء (void) من Paymob | إشعاراتهم بتتتجاهل، ومفيش منطق استرجاع. |
| 3 | كوبون بيغطي السعر كله | بيترفض في الدفع الأونلاين (Paymob مش بيقبل صفر). |
| 4 | ضغطتين سريعتين على "ادفع" لنفس الموعد | الأوردر الأقدم بيتنسى، ولو العميل دفع عليه الإشعار بيتجاهل ويتسجّل في اللوج. |
| 5 | Paymob نفسه وقع | الرد 500 مش 502 (تغيير معالجة الأخطاء ملف مشترك). |
| 6 | الكوبون خلص بين بدء الدفع ونجاحه | العميل دفع بالخصم فعلاً، فبنحترمه ونسجّل تحذير (الكوبون ما بيعدّيش `MaxUses`). |
| 7 | حد استخدام الكوبون للمريض الواحد | مفيش (مش في المطلوب). |
| 8 | الدكتور مفيش عنده endpoint يعرف منه مرفقات المريض | الـ `id` بيرجع للمريض وقت الرفع بس. ممكن نضيف `GET /api/appointments/{id}/attachments` بقرار الفريق. |
| 9 | بيانات الفوترة المبعوتة لـ Paymob | قيم عامة (`Patient` و`patient{رقم}@clinic.local` و`NA`)، لأن الـ Application ما بيشوفش بيانات الـ Identity. |
| 10 | إشعار المريض بنجاح أو فشل الدفع | مؤجل لنظام الإشعارات العام. |

---

## 8. الـ Commits على الفرع
- `feat(medical): AES-GCM field encryption`
- `feat(medical): attachment validation and local storage`
- `feat(medical): patient access guard`
- `feat(medical): medical history service and endpoints`
- `feat(medical): attachment upload and download`
- `feat(payments): coupon validation and atomic redeem`
- `feat(payments): confirm clinic payment with optional coupon`
- `feat(payments): admin coupon CRUD`
- `feat(payments): paymob client and online payment initiation`
- `feat(payments): paymob webhook with hmac verification`
- `docs(payments): person 3 final report and handover notes`
