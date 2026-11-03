# تقسيم المشروع على 3 أشخاص

> الأسماء: ضيفها في الجدول اللي تحت.

| # | الشخص | الاسم | المراحل |
|---|---|---|---|
| 1 | Auth + الأطباء + السواجر | [الاسم] | 3، 4 |
| 2 | محرك الحجز | [الاسم] | 5 |
| 3 | الدفع + السجل الطبي | [الاسم] | 6، 7 |

**ترتيب الشغل:** الشخص 1 هو اللي بيدمج أول، والشخص 2 يبدأ بالتصميم والاختبارات من أول يوم، ويكمل بالـ stubs لحد ما الأطباء تتدمج. الشخص 3 يبدأ بالتشفير والتخزين والـ Paymob sandbox.

---

## قبل ما أي حد يبدأ (اتفاق مشترك، ساعة واحدة)

1. **تقفل القرارات المفتوحة:**
   - OTP: **إيميل** (اتحسم).
   - قائمة الانتظار وملاحظات الكشف: راجع `docs/decisions.md`.
2. **الريبو على GitHub:** ارفع المشروع على `main`، واعمل `develop`، وضيف الـ 3 أشخاص كـ Collaborators، وفعّل Branch protection على `main` و`develop`.
3. **شغّل `seed-all-tables.sql`** على `ClinicDb` عشان الكل يشتغل على نفس البيانات.
4. **الشخص 1 يدفع `ICurrentUser` على `develop`** في نفس اليوم، لأن الاتنين التانيين بيستخدموه.

---

## الشخص 1: Auth + الأطباء + السواجر (مراحل 3 و4)

### المسؤوليات
- تسجيل Identity وJWT والـ roles والـ policies.
- حسابات المرضى (register)، وحسابات الدكاترة والريسبشن (من الأدمن).
- CRUD للتخصصات والعيادات (Admin).
- ملف الدكتور: السعر، نسبة المتابعة، مدتها، والاعتماد (`IsApproved`).
- أوقات العمل والإجازات.
- البحث والفلترة (التخصص، السعر، التقييم).
- **السواجر (Swagger/Scalar):** الشخص 1 مسؤول عن الواجهة وتوثيق كل الـ endpoints، وبيتأكد إن الـ auth بيظهر في الواجهة (زرار Authorize).

### الجداول
| الجدول | الاستخدام |
|---|---|
| `AspNetUsers` | حسابات كل المستخدمين (ApplicationUser) |
| `AspNetRoles`، `AspNetUserRoles` | الأدوار وربط المستخدم بيها |
| `AspNetUserClaims`، `AspNetRoleClaims`، `AspNetUserLogins`، `AspNetUserTokens` | جداول Identity الداخلية (مش بتتكتب فيها يدوي) |
| `Patients` | بيتعمل مع register المريض |
| `Receptionists` | بيتعمل من الأدمن |
| `Specialties` | التخصصات |
| `Clinics` | العيادات |
| `Doctors` | ملف الدكتور |
| `WorkingSchedules` | أوقات العمل الأسبوعية |
| `TimeOffs` | الإجازات |

### الملفات والفولدرات
- `Clinic.Infrastructure/Identity/` (تنفيذ Identity وJWT وتوليد الـ tokens)
- `Clinic.Application/Features/Auth/`، `Features/Doctors/`، `Features/Specialties/`، `Features/Clinics/`، `Features/Schedules/`
- `Clinic.API/Controllers/AuthController.cs`، `DoctorsController.cs`، `ClinicsController.cs`
- `Clinic.Infrastructure/AuthModule.cs` (أو `IdentityModule.cs`)
- `Clinic.Application/Common/Interfaces/ICurrentUser.cs` (العقد، بيتكتب أول يوم)

### Endpoints
| Method | Route | الصلاحية |
|---|---|---|
| POST | `/api/auth/register` | عام (مريض بس) |
| POST | `/api/auth/login` | عام، بيرجع JWT |
| POST | `/api/admin/doctors` | Admin |
| PATCH | `/api/admin/doctors/{id}/approve` | Admin |
| POST | `/api/admin/receptionists` | Admin |
| GET/POST/PUT | `/api/specialties` | Admin للكتابة، عام للقراءة |
| GET/POST/PUT | `/api/clinics` | Admin للكتابة، عام للقراءة |
| GET | `/api/doctors` (بحث وفلترة) | عام |
| GET | `/api/doctors/{id}` | عام |
| PUT | `/api/doctors/me/schedule` | Doctor |
| POST | `/api/doctors/me/timeoffs` | Doctor |

### خطة الشغل بالترتيب
1. فرع `feature/auth-doctors` من `develop`.
2. `ICurrentUser` (العقد) وادفعه على `develop` أول يوم.
3. تسجيل Identity في `AddInfrastructure` (`AddIdentity<ApplicationUser, IdentityRole<Guid>>` وRequireUniqueEmail وPassword policy).
4. JWT: الإعدادات في User Secrets، و`AddJwtBearer` في `Program.cs`، و`UseAuthentication()` قبل `UseAuthorization()`.
5. Seed للأدوار الأربعة عند أول تشغيل.
6. `register` و`login`، وJWT فيه `sub` وRole claims.
7. Endpoints الأدمن (الدكاترة والريسبشن).
8. CRUD التخصصات والعيادات.
9. ملف الدكتور والاعتماد.
10. أوقات العمل والإجازات (مع اختبارات: مفيش وقتين بيتقاطعوا).
11. البحث والفلترة.
12. الواجهة (Swagger) واختبارات Swagger الكاملة.
13. PR على `develop`.

### معيار الخلاص
- register وlogin شغالين، والـ token بيشتغل في Swagger.
- endpoint محمي بدون token بيرجع 401، ولـ Patient بيرجع 403.
- الدكتور مش بيتعرض في البحث غير لو `IsApproved = 1`.

### اللي عليه ينتظر
لا شيء، بس هو **أول حد يدمج**، والباقيين بيعتمدوا عليه.

---

## الشخص 2: محرك الحجز (مرحلة 5)

### المسؤوليات
- توليد الـ Slots من أوقات العمل، مع استبعاد الإجازات والمواعيد المحجوزة.
- إنشاء الموعد بـ Transaction و`RowVersion` عشان ما يتحجزش مرتين.
- الإلغاء وفتح الميعاد.
- قائمة الانتظار: لما ميعاد يتفتح، يتبعت إشعار للمنتظرين.
- حساب `PriceSnapshot` (السعر المتجمد وقت الحجز) والتحقق من كشف المتابعة.
- Hosted Service: لو الحجز `PendingPayment` عدّى 15 دقيقة، يتحول لـ `Expired` والميعاد يتفتح.
- **اختبارات التزامن**: أكتر من مريض بيحجزوا نفس الميعاد في نفس اللحظة.

### الجداول
| الجدول | الاستخدام |
|---|---|
| `Appointments` | المواعيد (الجدول الأهم في المشروع) |
| `WaitlistEntries` | قائمة الانتظار |

**بيقرأ** من: `Doctors` و`WorkingSchedules` و`TimeOffs` (الشخص 1)، و`Patients` (الشخص 1).
**بيكتب في**: `Appointments` و`WaitlistEntries` بس.

### الملفات والفولدرات
- `Clinic.Application/Features/Appointments/` (Commands وQueries وValidators)
- `Clinic.Application/Features/Waitlist/`
- `Clinic.Application/Features/Slots/` (حساب الـ Slots، مع unit tests)
- `Clinic.Infrastructure/Jobs/ExpirePendingAppointmentsJob.cs` (Hosted Service)
- `Clinic.API/Controllers/AppointmentsController.cs`
- `Clinic.Infrastructure/BookingModule.cs`
- `tests/` (اختبارات التزامن)

### Endpoints
| Method | Route | الصلاحية |
|---|---|---|
| GET | `/api/doctors/{id}/slots?date=` | عام |
| POST | `/api/appointments` (حجز، بيرجع PendingPayment) | Patient |
| GET | `/api/appointments/me` | Patient |
| GET | `/api/doctors/me/appointments?date=` | Doctor |
| POST | `/api/appointments/{id}/cancel` | Patient أو Doctor |
| POST | `/api/waitlist` | Patient |

### خطة الشغل بالترتيب
1. فرع `feature/booking` من `develop`.
2. **التصميم أول يوم:** `Appointment` و`WaitlistEntry` (الـ Entities موجودة)، ومنطق الـ Slots كـ Domain service منفصل بيتكتب بـ unit tests ومن غير أي DB.
3. Interface لقراءة أوقات الدكتور، واكتب له stub عشان تشتغل لحد ما الشخص 1 يدمج.
4. الحجز بـ Transaction: قراءة الـ slot، والتحقق إن مفيش موعد بيتقاطع، والإنشاء، وحفظ `PriceSnapshot`.
5. اختبارات التزامن (حجز متوازي، ولازم واحد بس ينجح).
6. الإلغاء وفتح الميعاد وإشعار قائمة الانتظار.
7. الـ Hosted Service لإلغاء الحجوزات المنتهية.
8. الـ Endpoints والـ Swagger.
9. PR على `develop`.

### معيار الخلاص
- حجز متوازي على نفس الميعاد: واحد بس بينجح، والتاني بيرجع 409.
- الميعاد اللي فيه إجازة ما يتحجزش.
- الحجز اللي عدّى 15 دقيقة من غير دفع بيبقى `Expired` تلقائياً.
- `PriceSnapshot` ما يتغيرش لو الدكتور غيّر سعره بعد الحجز.

### اللي عليه ينتظر
- **الشخص 1:** الأطباء وأوقات العمل، عشان الـ Slots تتحسب من بيانات حقيقية. في الأول يشتغل على stub.
- **القرار المفتوح:** قائمة الانتظار (لازم يتقفل قبل ما يبدأ فيها).

---

## الشخص 3: الدفع + السجل الطبي (مراحل 6 و7)

### المسؤوليات
**الدفع:**
- Paymob Sandbox: الحساب والمفاتيح (في User Secrets).
- إنشاء طلب الدفع وتوجيه المريض لصفحة Paymob.
- Webhook مع التحقق من **HMAC قبل أي حاجة**. أي طلب بتوقيع غلط يترفض.
- الدفع في العيادة: الـ Receptionist بيغير حالة الدفع لـ Paid.
- الكوبونات: التحقق من الصلاحية والحد الأقصى، وزيادة `UsedCount` بأمان من التزامن.
- `Amount = max(0, OriginalAmount - DiscountAmount)`.

**السجل الطبي:**
- `MedicalHistory`: الأمراض المزمنة والحساسية والأدوية، **مشفرة**.
- رفع المرفقات (PDF وصور): التحقق من النوع والحجم، وحفظ بمسار آمن.
- **قاعدة الأمان:** المريض يشوف بياناته بس، والدكتور يشوف المرضى اللي عنده مواعيد معاهم بس.

### الجداول
| الجدول | الاستخدام |
|---|---|
| `Payments` | المدفوعات (واحد لكل موعد) |
| `Coupons` | الكوبونات |
| `MedicalHistories` | السجل الطبي (مشفر) |
| `Attachments` | المرفقات |

**بيقرأ** من: `Appointments` (الشخص 2)، و`Patients` و`Doctors` (الشخص 1).
**بيكتب في**: `Payments` و`Coupons` و`MedicalHistories` و`Attachments` بس.

### الملفات والفولدرات
- `Clinic.Infrastructure/Payments/Paymob/` (Client وWebhook Verifier)
- `Clinic.Application/Features/Payments/`
- `Clinic.Application/Features/Coupons/`
- `Clinic.Application/Features/MedicalHistory/`
- `Clinic.Application/Features/Attachments/`
- `Clinic.Infrastructure/Files/` (التخزين والتحقق من الملفات)
- `Clinic.Infrastructure/Security/Encryption/` (تشفير الحقول)
- `Clinic.API/Controllers/PaymentsController.cs`، `PaymobWebhookController.cs`، `MedicalHistoryController.cs`، `AttachmentsController.cs`
- `Clinic.Infrastructure/PaymentsModule.cs`، `MedicalModule.cs`

### Endpoints
| Method | Route | الصلاحية |
|---|---|---|
| POST | `/api/payments/online` (بيرجع رابط Paymob) | Patient |
| POST | `/api/payments/webhook/paymob` | عام، بس HMAC |
| POST | `/api/payments/clinic/{appointmentId}/confirm` | Receptionist |
| POST | `/api/coupons/validate` | Patient |
| GET/PUT | `/api/medical-history/me` | Patient |
| GET | `/api/patients/{id}/medical-history` | Doctor (لمرضاه بس) |
| POST | `/api/appointments/{id}/attachments` | Patient |
| GET | `/api/attachments/{id}` | المريض صاحبه، أو الدكتور اللي معاه موعد |

### خطة الشغل بالترتيب
1. فرع `feature/payments-medical` من `develop`.
2. **أول يوم:** حساب Paymob sandbox، ومفاتيحه في User Secrets.
3. **تشفير الحقول:** service بيشفر ويفك (AES)، والمفتاح في User Secrets. اختبار: الحقل بيتخزن مشفر، وبيترجع نص عادي.
4. تخزين المرفقات مع التحقق من النوع (PDF وصور بس) والحجم.
5. قاعدة "المريض يشوف بياناته بس": تتكتب في Application، ومعاها اختبارات لكل حالة.
6. Webhook مع HMAC: أول حاجة بيتعملها الـ endpoint هي التحقق، وبعدين يعدل الحالة.
7. الدفع في العيادة.
8. الكوبونات بزيادة `UsedCount` بأمان (`RowVersion` أو update شرطي).
9. الـ Endpoints والـ Swagger.
10. PR على `develop`.

### معيار الخلاص
- Webhook بتوقيع غلط يرجع 401، ومفيش تغيير في الداتابيز.
- Webhook صح بيغير الحالة لـ Paid مرة واحدة بس، حتى لو اتبعت مرتين.
- دكتور مش معاه موعد مع مريض، ما يقدرش يشوف سجله (403).
- الكوبون اللي `MaxUses` خلص، ما يتقبلش.

### اللي عليه ينتظر
- **الشخص 1:** Auth عشان الـ endpoints تتحمي. في الأول يشتغل بـ stub للـ roles.
- **الشخص 2:** الحجز عشان الدفع يتربط بيه. في الأول يشتغل على stub للـ Appointment.

---

## بعد الدمج الكامل (الفترة الجاية، 8 إلى 12)

دي مش مقسومة دلوقتي، توزعها بعد ما الـ 3 يخلصوا:
- **التقييمات والكوبونات** (`Reviews`، مرحلة 8): أقرب للشخص 3.
- **كشف المتابعة** (مرحلة 8): أقرب للشخص 2.
- **الرسائل والإشعارات** (`Conversations`، `Messages`، `Notifications`، مرحلة 9): الشخص اللي يخلص الأول.
- **Localization والـ Caching** (مرحلة 10): الشخص 1.
- **Admin والتقارير** (مرحلة 11): الشخص 1.
- **Global Exception Handling و Standard API Response، والاختبارات والتلميع** (مرحلة 12): الكل.

---

## Git وقواعد الشغل

### الفروع
```
main                      النسخة المستقرة
develop                   التكامل
feature/auth-doctors      الشخص 1
feature/booking           الشخص 2
feature/payments-medical  الشخص 3
```

### دورة الشغل
```
git checkout develop
git pull
git checkout -b feature/<الفرع>
# اشتغل في فولدراتك بس
git add .
git commit -m "feat(<المجال>): ..."
git push -u origin feature/<الفرع>
# افتح Pull Request على develop، وواحد من التلاتة يراجع
```

### ممنوعات
- **ممنوع تعمل Migration على فرعك.** الـ Migration بتتعمل مرة واحدة بعد الدمج الكامل، من **الشخص 1** (المسؤول عن الدمج)، وتتطبق على داتابيز التكامل.
- **ممنوع تعدّل الملفات المشتركة** إلا الشخص 1: `Program.cs`، `DependencyInjection.cs`، `AppDbContext.cs`، `Entities/*`، `Enums/*`، والـ `csproj`.
- ممنوع تكتب على `main` مباشرة.

### بدل تعديل الملفات المشتركة
كل شخص يعمل Module خاص بيه (`AuthModule`، `BookingModule`، `PaymentsModule`/`MedicalModule`)، والشخص 1 بيضيف سطر لكل module في `DependencyInjection.cs` عند الدمج.

### الـ Migrations
- التعديلات على الـ Entities تتعمل **قبل** التقسيم، أو تتدمج الأول وتتعمل Migration واحدة.
- لو شخص محتاج يعدّل Entity، يكلم الباقيين الأول.

### لو محتاج بيانات من غيرك
1. اكتب **interface** في فولدرك.
2. اعمل **stub** عشان تشتغل محلياً.
3. لما الدمج يحصل، الشخص 1 يبدّل الـ stub بالتنفيذ الحقيقي.

---

## ترتيب الدمج

1. **الشخص 1** يدمج الأول (Auth + ICurrentUser).
2. **الشخص 1** يدمج الأطباء والجداول.
3. **الشخص 2** يدمج الحجز (بعد الأطباء).
4. **الشخص 3** يدمج الدفع والسجل الطبي (بعد الحجز).
5. بعد كل دمج: `dotnet build` والاختبارات تشتغل، وتتحدث `docs/todo.md`.
6. بعد الدمج الكامل: **Migration واحدة** من الشخص 1، وتتطبق على داتابيز التكامل.

---

## قبل ما تبدأوا (checklist)


- [x] OTP: إيميل
- [ ] الريبو على GitHub، والـ 3 أشخاص Collaborators.
- [ ] Branch protection على `main` و`develop`.
- [ ] الشخص 1 دفع `ICurrentUser` على `develop`.
- [ ] Paymob sandbox حسابه اتعمل (الشخص 3).

---
