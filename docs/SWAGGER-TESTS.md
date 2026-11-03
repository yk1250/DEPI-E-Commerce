# اختبارات Swagger (المراحل 0 إلى 4)

الهدف: تتأكد إن كل endpoint بيرجع الـ Status والبيانات المتوقعة، وإن الصلاحيات (401 و403) شغالة.

## قبل ما تبدأ

1. **الأسرار:** `Jwt:Key` (32 حرف على الأقل) والـ connection string في User Secrets (انظر `PROJECT.md` قسم 6).
2. **الداتابيز:** الـ Migrations متطبقة، و`seed/seed-all-tables.sql` اتشغل (مرة واحدة).
3. **التشغيل:** `dotnet run --project Clinic.API --launch-profile https` ثم افتح `https://localhost:7011/swagger`.
   - عند أول تشغيل بيتعمل الأربع أدوار، وبيتحط الباسورد `Test@12345` لحسابات `@test.local`.
4. **الـ Authorize:** اعمل Login من `POST /api/auth/login`، وانسخ قيمة `accessToken` (من غير علامات التنصيص)، واضغط زر **Authorize** فوق، والصق الـ token **من غير كلمة Bearer**. لتغيير المستخدم: Authorize ثم Logout ثم الصق token جديد. (الـ token بيفضل محفوظ لو عملت Refresh للصفحة.)
5. **الأرقام (ids):** الأرقام تحت (التخصص 1 = Cardiology، والدكتور 1 = Dr Ahmed...) صح لو الداتابيز اتعملت من الصفر. لو رقم مختلف عندك، خد الرقم الصح من الـ GET وعدّله.

### الحالة الأصلية للبيانات (اللي الاختبارات مبنية عليها)

| | |
|---|---|
| التخصصات | 1 Cardiology، 2 Dermatology، 3 Pediatrics، 4 Orthopedics، 5 Dentistry، 6 Internal Medicine |
| العيادات | 1 Test Clinic 1، 2 Test Clinic 2 (مدينة Faiyum) |
| دكتور 1 | Dr Ahmed Test، قلب، عيادة 1، 300 جنيه، متابعة 50% خلال 14 يوم، خبرة 12، تقييم 4.00، **معتمد** |
| دكتور 2 | Dr Sara Test، جلدية، عيادة 1، 250 جنيه، متابعة 0%، خبرة 8، تقييم 5.00، **معتمدة** |
| دكتور 3 | Dr Mona Test، أطفال، عيادة 2، 200 جنيه، **غير معتمدة** |
| جدول دكتور 1 | الأحد 09:00 إلى 13:00 (فترة 30)، والثلاثاء 16:00 إلى 20:00 (فترة 30) |
| إجازة دكتور 1 | من 2026-11-10T00:00Z إلى 2026-11-12T23:59Z ("إجازة سفر") |
| مواعيد دكتور 1 | 2026-11-01 من 09:30 لـ 10:00 (`Confirmed`) |

> الاختبارات بتعدل البيانات (مثلاً الجدول). فيه خطوات "رجّع" بعد الاختبارات اللي بتغير حاجة. للبداية من الأول: امسح الداتابيز واعمل Migration وSeed تاني.

---

## 0. الاختبارات الأساسية (اتجربت ونجحت)

دي المسارات اللي كل حاجة بتبني عليها. لو حد عدّل Auth أو البحث أو الجدول، اعمل الأرقام دي الأول:

| الأرقام | بتثبت إيه |
|---|---|
| 1.6 و1.9 و1.11 | Login بمريض ودكتور وأدمن، والـ claims (`patientId` و`doctorId` و`clinicId`) |
| 1.12 و1.13 | `/me` من غير token ثم بـ token |
| 2.5 | إضافة تخصص بـ token مريض = 403 |
| 4.1 | إنشاء دكتور من الأدمن (Transaction الحساب والملف) |
| 4.9 و7.1 | اعتماد الدكتور ثم ظهوره في البحث (الـ join) |
| 7.11 | الترتيب بالسعر |
| 6.10 | استبدال الجدول الأسبوعي |
| 6.19 | إضافة إجازة وتحويلها لـ UTC |
| 5.1 | إنشاء ريسبشن |

الباقي (حالات الأخطاء والحدود) بيتعمل وقت الدمج.

---

## 1. Auth

| # | الطلب | المتوقع |
|---|---|---|
| 1.1 | `POST /api/auth/register` (من غير token) بالـ body تحت | **201**، والـ body: `userId` (GUID)، `patientId` (رقم، 4 أو أكتر)، `email` = `new.patient@test.local`، `fullName` = `New Patient` |
| 1.2 | نفس الطلب تاني | **409**. `title`: `Conflict`، `detail`: `An account with this email already exists.` |
| 1.3 | register بباسورد `alllowercase1` | **400**. `title`: `Validation Failed`، و`errors` فيها `PasswordRequiresUpper` |
| 1.4 | register بـ body فاضي `{}` | **400** (ValidationProblemDetails). `errors` فيها Email وPassword وFullName وDateOfBirth وGender |
| 1.5 | register بـ `"dateOfBirth": "2999-01-01"` | **400**. `errors.dateOfBirth` = `Date of birth must be between 1900-01-01 and today.` |
| 1.6 | `POST /api/auth/login` بـ `patient1@test.local` والباسورد `Test@12345` | **200**. فيه `accessToken` (نص طويل فيه نقطتين)، `tokenType` = `Bearer`، `expiresAtUtc` بعد حوالي 60 دقيقة من دلوقتي، و`user.role` = `Patient`، `user.patientId` = 1، و`doctorId` و`clinicId` = null |
| 1.7 | login بباسورد غلط | **401**. `detail`: `Invalid email or password.` |
| 1.8 | login بإيميل مش موجود | **401**، نفس الرسالة (من غير ما نكشف إن الإيميل مش موجود) |
| 1.9 | login بـ `doctor1@test.local` | **200**. `user.role` = `Doctor`، `doctorId` = 1، `clinicId` = 1 |
| 1.10 | login بـ `reception1@test.local` | **200**. `role` = `Receptionist`، `clinicId` = 1 |
| 1.11 | login بـ `admin@test.local` | **200**. `role` = `Admin`، والـ ids الثلاثة null |
| 1.12 | `GET /api/auth/me` من غير token | **401** (الـ body فاضي، وفي الـ header `WWW-Authenticate: Bearer`) |
| 1.13 | `GET /api/auth/me` بـ token المريض | **200**. `email` = `patient1@test.local`، `role` = `Patient`، `patientId` = 1 |
| 1.14 | `GET /api/auth/me` بـ token اتغير فيه حرف | **401** |
| 1.15 | قفل الحساب: login بـ `patient3@test.local` وباسورد غلط 5 مرات ورا بعض، وبعدين السادسة بالباسورد الصح | آخر طلب **401**، و`detail` فيها `temporarily locked`. الحساب بيتفتح بعد 5 دقايق |

الـ body بتاع 1.1:
```json
{
  "email": "new.patient@test.local",
  "password": "Patient@123",
  "fullName": "New Patient",
  "phoneNumber": "01012345678",
  "dateOfBirth": "1995-04-20",
  "gender": "Male",
  "preferredLanguage": "Ar"
}
```
الـ body بتاع 1.3: نفس 1.1 مع `"email": "weak.pass@test.local"` و`"password": "alllowercase1"`.
الـ body بتاع Login:
```json
{ "email": "patient1@test.local", "password": "Test@12345" }
```

---

## 2. التخصصات

| # | الطلب (والـ token) | المتوقع |
|---|---|---|
| 2.1 | `GET /api/specialties` (من غير token) | **200**. 6 عناصر مرتبة بالاسم الإنجليزي: Cardiology، Dentistry، Dermatology، Internal Medicine، Orthopedics، Pediatrics |
| 2.2 | `GET /api/specialties/1` | **200**: `nameEn` = `Cardiology`، `nameAr` = `قلب` |
| 2.3 | `GET /api/specialties/999` | **404**. `detail`: `Specialty (999) was not found.` |
| 2.4 | `POST /api/specialties` من غير token | **401** |
| 2.5 | `POST /api/specialties` بـ token مريض | **403** |
| 2.6 | `POST` بـ token أدمن، الـ body: `{"nameAr":"أعصاب","nameEn":"Neurology"}` | **201**. فيه Header `Location: /api/specialties/{id}` والـ body فيه `id` جديد (7 أو أكتر). **سجّل الـ id** |
| 2.7 | نفس الـ POST تاني | **409**. `detail`: `A specialty with the same Arabic or English name already exists.` |
| 2.8 | `POST` بـ `{"nameAr":"","nameEn":"x"}` | **400**. errors على `NameAr` و`NameEn` |
| 2.9 | `PUT /api/specialties/{id}` (أدمن) بـ `{"nameAr":"مخ وأعصاب","nameEn":"Neurology"}` | **200**. `nameAr` = `مخ وأعصاب` |
| 2.10 | `PUT /api/specialties/999` | **404** |
| 2.11 | `DELETE /api/specialties/1` (أدمن) | **409**. `detail`: `This specialty is used by one or more doctors and cannot be deleted.` |
| 2.12 | `DELETE /api/specialties/{id الجديد}` (أدمن) | **204** (من غير body). وبعدها `GET` بنفس الـ id = **404** |

---

## 3. العيادات

| # | الطلب | المتوقع |
|---|---|---|
| 3.1 | `GET /api/clinics` (من غير token) | **200**. عيادتين: Test Clinic 1 وTest Clinic 2 (مدينة Faiyum) |
| 3.2 | `GET /api/clinics/1` | **200** |
| 3.3 | `POST /api/clinics` (أدمن) بالـ body تحت | **201**. `isActive` = true. **سجّل الـ id** |
| 3.4 | نفس الـ POST تاني | **409**. `A clinic with the same name already exists in this city.` |
| 3.5 | `POST` بـ phone `abc` | **400** |
| 3.6 | `POST` من غير token / بـ token مريض | **401** / **403** |
| 3.7 | `PUT /api/clinics/{id}` (أدمن) بنفس الـ body مع `"isActive": false` | **200**. `isActive` = false |
| 3.8 | `GET /api/clinics` (من غير token) بعد 3.7 | **200**. العيادة الجديدة **مش موجودة** |
| 3.9 | `GET /api/clinics?includeInactive=true` من غير token | **200**. لسه مش موجودة (الخيار للأدمن بس) |
| 3.10 | `GET /api/clinics?includeInactive=true` بـ token أدمن | **200**. موجودة |
| 3.11 | `GET /api/clinics/{id}` من غير token / بـ token أدمن | **404** / **200** |
| 3.12 | `DELETE /api/clinics/1` (أدمن) | **409**. `This clinic has doctors or receptionists and cannot be deleted. Set IsActive to false instead.` |
| 3.13 | `DELETE /api/clinics/{id الجديد}` (أدمن) | **204** |

الـ body بتاع 3.3:
```json
{ "name": "Nile Clinic", "address": "12 Nile St", "city": "Cairo", "phone": "0223456789", "isActive": true }
```

---

## 4. إدارة الأطباء (أدمن)

سجّل دخول بـ `admin@test.local`.

| # | الطلب | المتوقع |
|---|---|---|
| 4.1 | `POST /api/admin/doctors` بالـ body تحت | **201**. `isApproved` = **false**، `specialtyNameEn` = `Cardiology`، `clinicName` = `Test Clinic 1`، `email` = `dr.new@test.local`، `isAccountActive` = true. **سجّل الـ id** |
| 4.2 | نفس الطلب تاني | **409** (الإيميل مسجل) |
| 4.3 | الطلب بـ `"specialtyId": 999` | **400**. `errors.specialtyId` = `Specialty was not found.` |
| 4.4 | الطلب بـ `"clinicId": 999` | **400**. `errors.clinicId` = `Clinic was not found.` |
| 4.5 | الطلب بـ `"followUpFeePercent": 150` | **400** |
| 4.6 | `POST /api/admin/doctors` بـ token مريض / دكتور / من غير token | **403** / **403** / **401** |
| 4.7 | `GET /api/doctors` (من غير token) | الدكتور الجديد **مش موجود** في النتيجة |
| 4.8 | `GET /api/admin/doctors?isApproved=false` | **200**. `totalCount` = 2: Dr Mona Test + الدكتور الجديد |
| 4.9 | `PATCH /api/admin/doctors/{id}/approve` | **200**. `isApproved` = true |
| 4.10 | `GET /api/doctors` (من غير token) | الدكتور الجديد **ظهر** |
| 4.11 | `PUT /api/admin/doctors/{id}` بـ `{"specialtyId":2,"clinicId":2}` | **200**. `specialtyNameEn` = `Dermatology`، `clinicName` = `Test Clinic 2` |
| 4.12 | `PUT` بـ `{"specialtyId":999,"clinicId":1}` | **400** |
| 4.13 | `PATCH /api/admin/doctors/{id}/unapprove` | **200**. `isApproved` = false، وبيختفي من `GET /api/doctors` |
| 4.14 | `GET /api/admin/doctors/999` | **404** |
| 4.15 | `GET /api/admin/doctors` | **200**. `totalCount` = 4، وفيه `email` و`isApproved` في كل عنصر |

الـ body بتاع 4.1:
```json
{
  "email": "dr.new@test.local",
  "password": "Doctor@123",
  "fullName": "Dr New Doctor",
  "phoneNumber": "01099999999",
  "specialtyId": 1,
  "clinicId": 1,
  "bio": "استشاري قلب",
  "yearsOfExperience": 6,
  "consultationFee": 400,
  "followUpFeePercent": 50,
  "followUpWindowDays": 10
}
```

---

## 5. الريسبشن (أدمن)

| # | الطلب | المتوقع |
|---|---|---|
| 5.1 | `POST /api/admin/receptionists` بالـ body تحت | **201**. `clinicName` = `Test Clinic 1`، `isAccountActive` = true. **سجّل الـ id** |
| 5.2 | `GET /api/admin/receptionists` | **200**. عنصرين (reception1 والجديد) |
| 5.3 | `GET /api/admin/receptionists?clinicId=2` | **200**. قايمة فاضية `[]` |
| 5.4 | `GET /api/admin/receptionists/{id}` | **200** |
| 5.5 | login بالحساب الجديد (`rec.new@test.local` / `Reception@1`) | **200**. `role` = `Receptionist`، `clinicId` = 1 |
| 5.6 | `POST` بـ `"clinicId": 999` | **400**. `errors.clinicId` |
| 5.7 | `POST` من غير token / بـ token دكتور | **401** / **403** |
| 5.8 | `DELETE /api/admin/receptionists/{id}` (أدمن) | **204** |
| 5.9 | `GET /api/admin/receptionists/{id}` بعد المسح | **404** |
| 5.10 | login بالحساب المحذوف | **401** |

الـ body بتاع 5.1:
```json
{
  "email": "rec.new@test.local",
  "password": "Reception@1",
  "fullName": "New Receptionist",
  "phoneNumber": "01055555555",
  "clinicId": 1
}
```

---

## 6. الدكتور لنفسه

سجّل دخول بـ `doctor1@test.local` (الباسورد `Test@12345`).

### الملف والسعر
| # | الطلب | المتوقع |
|---|---|---|
| 6.1 | `GET /api/doctors/me` | **200**. `id` = 1، `fullName` = `Dr Ahmed Test`، `isApproved` = true، `consultationFee` = 300، `followUpFeePercent` = 50، `followUpWindowDays` = 14 |
| 6.2 | `PUT /api/doctors/me` بـ `{"bio":"استشاري قلب","yearsOfExperience":13,"consultationFee":350,"followUpFeePercent":40,"followUpWindowDays":21}` | **200**. القيم الجديدة في الرد |
| 6.3 | `GET /api/doctors/1` (من غير token) | `consultationFee` = 350 |
| 6.4 | `PUT /api/doctors/me` بـ `"followUpFeePercent": 150` | **400** |
| 6.5 | **رجّع:** `PUT /api/doctors/me` بـ `{"bio":"استشاري قلب وأوعية دموية","yearsOfExperience":12,"consultationFee":300,"followUpFeePercent":50,"followUpWindowDays":14}` | **200** |
| 6.6 | `GET /api/doctors/me` بـ token مريض | **403** |
| 6.7 | `GET /api/doctors/me` من غير token | **401** |
| 6.8 | login بـ `doctor3@test.local` ثم `GET /api/doctors/me` | **200**. `isApproved` = **false** (الدكتور غير المعتمد يقدر يدخل ويجهز بياناته) |

### جدول العمل
| # | الطلب | المتوقع |
|---|---|---|
| 6.9 | `GET /api/doctors/me/schedule` | **200**. عنصرين: `Sunday` من `09:00:00` لـ `13:00:00` بفترة 30، و`Tuesday` من `16:00:00` لـ `20:00:00` بفترة 30 (الأحد الأول) |
| 6.10 | `PUT /api/doctors/me/schedule` بالـ body الأول تحت | **200**. 3 عناصر مرتبة: Sunday 09:00، Sunday 17:00، Wednesday 10:00. **والثلاثاء اتشال** |
| 6.11 | `PUT` بفترتين متقاطعتين (body تاني تحت) | **400**. `errors.items` فيها: `Sunday: working windows overlap (09:00-13:00 and 12:00-15:00).` |
| 6.12 | `PUT` بـ `startTime` `13:00:00` و`endTime` `09:00:00` | **400**. `Item 0: endTime must be after startTime.` |
| 6.13 | `PUT` بفترة 60 دقيقة (10:00 لـ 11:00) و`slotDurationMinutes` = 120 | **400**. `Item 0: slotDurationMinutes is longer than the working window.` |
| 6.14 | `PUT` بـ `slotDurationMinutes` = 500 | **400** (المسموح من 5 لـ 240) |
| 6.15 | `GET /api/doctors/1/schedule` (من غير token) | **200**. الفترات الشغالة بس (3 عناصر بعد 6.10) |
| 6.16 | `GET /api/doctors/3/schedule` (دكتور غير معتمد) | **404** |
| 6.17 | **رجّع الجدول الأصلي:** `PUT` بـ body التالت تحت | **200**. الأحد والثلاثاء زي ما كانوا |

الـ body بتاع 6.10:
```json
{
  "items": [
    { "dayOfWeek": "Sunday", "startTime": "09:00:00", "endTime": "13:00:00", "slotDurationMinutes": 30, "isActive": true },
    { "dayOfWeek": "Sunday", "startTime": "17:00:00", "endTime": "21:00:00", "slotDurationMinutes": 30, "isActive": true },
    { "dayOfWeek": "Wednesday", "startTime": "10:00:00", "endTime": "14:00:00", "slotDurationMinutes": 20, "isActive": true }
  ]
}
```
الـ body بتاع 6.11:
```json
{
  "items": [
    { "dayOfWeek": "Sunday", "startTime": "09:00:00", "endTime": "13:00:00", "slotDurationMinutes": 30, "isActive": true },
    { "dayOfWeek": "Sunday", "startTime": "12:00:00", "endTime": "15:00:00", "slotDurationMinutes": 30, "isActive": true }
  ]
}
```
الـ body بتاع 6.17 (الأصلي):
```json
{
  "items": [
    { "dayOfWeek": "Sunday", "startTime": "09:00:00", "endTime": "13:00:00", "slotDurationMinutes": 30, "isActive": true },
    { "dayOfWeek": "Tuesday", "startTime": "16:00:00", "endTime": "20:00:00", "slotDurationMinutes": 30, "isActive": true }
  ]
}
```

### الإجازات
| # | الطلب | المتوقع |
|---|---|---|
| 6.18 | `GET /api/doctors/me/timeoffs` | **200**. عنصر واحد: `startAt` = `2026-11-10T00:00:00Z`، `endAt` = `2026-11-12T23:59:00Z`، `reason` = `إجازة سفر` |
| 6.19 | `POST /api/doctors/me/timeoffs` بـ `{"startAt":"2026-12-01T00:00:00+02:00","endAt":"2026-12-03T23:59:00+02:00","reason":"مؤتمر"}` | **201**. `startAt` = `2026-11-30T22:00:00Z`، `endAt` = `2026-12-03T21:59:00Z` (اتحول لـ UTC). **سجّل الـ id** |
| 6.20 | `POST` بـ `{"startAt":"2026-11-11T00:00:00Z","endAt":"2026-11-11T12:00:00Z"}` (جوه إجازة 6.18) | **409**. `This period overlaps with another time off.` |
| 6.21 | `POST` بـ `{"startAt":"2026-11-01T09:00:00Z","endAt":"2026-11-01T11:00:00Z"}` (فوق موعد دكتور 1 المؤكد) | **409**. `There are booked appointments in this period. Cancel or move them first.` |
| 6.22 | `POST` بـ `endAt` قبل `startAt` | **400**. `endAt must be after startAt.` |
| 6.23 | `POST` بفترة كلها في الماضي (مثلاً 2026-01-01 لـ 2026-01-02) | **400**. `The time off period must end in the future.` |
| 6.24 | `GET /api/doctors/me/timeoffs?upcomingOnly=true` | **200**. الإجازتين (نوفمبر وديسمبر) |
| 6.25 | `DELETE /api/doctors/me/timeoffs/{id من 6.19}` | **204** |
| 6.26 | نفس الـ DELETE تاني | **404** |
| 6.27 | login بـ `doctor2@test.local` ثم `DELETE /api/doctors/me/timeoffs/{id إجازة دكتور 1 الأصلية}` | **404** (مش 403، عشان ما نكشفش إنها موجودة) |
| 6.28 | `POST /api/doctors/me/timeoffs` بـ token مريض | **403** |

---

## 7. البحث والفلترة (من غير token)

الحالة الأصلية: الدكتور 1 والدكتور 2 معتمدين، والدكتور 3 لأ. لو عملت اختبارات القسم 4 وسبت دكتور جديد معتمد، اسحب اعتماده الأول (`unapprove`).

| # | الطلب | المتوقع |
|---|---|---|
| 7.1 | `GET /api/doctors` | **200**. `totalCount` = 2، `page` = 1، `pageSize` = 10، `totalPages` = 1. الترتيب: **Dr Sara Test** (5.00) وبعدها **Dr Ahmed Test** (4.00). Dr Mona **مش موجودة** |
| 7.2 | `?specialtyId=1` | Dr Ahmed بس |
| 7.3 | `?specialtyId=2` | Dr Sara بس |
| 7.4 | `?clinicId=2` | `totalCount` = 0، `items` = `[]` (الدكتور الوحيد في عيادة 2 غير معتمد) |
| 7.5 | `?minPrice=260` | Dr Ahmed بس (300) |
| 7.6 | `?maxPrice=260` | Dr Sara بس (250) |
| 7.7 | `?minPrice=500&maxPrice=100` | **400**. `errors.minPrice` = `minPrice must be less than or equal to maxPrice.` |
| 7.8 | `?minRating=4.5` | Dr Sara بس |
| 7.9 | `?q=Sara` | Dr Sara بس |
| 7.10 | `?q=Mona` أو `?q=zzz` | `totalCount` = 0 |
| 7.11 | `?sortBy=price` | Dr Sara (250) وبعدها Dr Ahmed (300) |
| 7.12 | `?sortBy=price&sortDir=desc` | Dr Ahmed وبعدها Dr Sara |
| 7.13 | `?sortBy=experience&sortDir=desc` | Dr Ahmed (12) وبعدها Dr Sara (8) |
| 7.14 | `?sortBy=name` | Dr Ahmed Test وبعدها Dr Sara Test |
| 7.15 | `?page=2&pageSize=1` | `items` فيها Dr Ahmed بس، `totalCount` = 2، `totalPages` = 2 |
| 7.16 | `?sortBy=foo` | **400**. `errors.sortBy` |
| 7.17 | `?sortDir=up` | **400**. `errors.sortDir` |
| 7.18 | `?pageSize=100` أو `?page=0` | **400** |
| 7.19 | `GET /api/doctors/1` | **200**. `DoctorDto` من غير `email` ولا `isApproved` |
| 7.20 | `GET /api/doctors/3` | **404** (غير معتمد) |
| 7.21 | `GET /api/doctors/999` | **404** |
| 7.22 | بـ token أدمن: `PUT /api/clinics/1` بـ `"isActive": false` (مع باقي بيانات العيادة)، وبعدها `GET /api/doctors` | `totalCount` = 0 (دكتور 1 و2 في عيادة 1). **رجّع** `isActive` = true بعدها |

---

## 8. مصفوفة الصلاحيات

كل خلية هي الـ Status المتوقع.

| Endpoint | بدون token | Patient | Doctor | Receptionist | Admin |
|---|---|---|---|---|---|
| `GET /api/specialties` | 200 | 200 | 200 | 200 | 200 |
| `POST /api/specialties` | 401 | 403 | 403 | 403 | 201 |
| `DELETE /api/specialties/{id}` | 401 | 403 | 403 | 403 | 204 أو 409 |
| `POST /api/clinics` | 401 | 403 | 403 | 403 | 201 |
| `GET /api/doctors` | 200 | 200 | 200 | 200 | 200 |
| `GET /api/doctors/me` | 401 | 403 | 200 | 403 | 403 |
| `PUT /api/doctors/me/schedule` | 401 | 403 | 200 | 403 | 403 |
| `POST /api/doctors/me/timeoffs` | 401 | 403 | 201 | 403 | 403 |
| `GET /api/admin/doctors` | 401 | 403 | 403 | 403 | 200 |
| `POST /api/admin/receptionists` | 401 | 403 | 403 | 403 | 201 |
| `GET /api/auth/me` | 401 | 200 | 200 | 200 | 200 |

---

## 9. فحص واجهة Swagger نفسها

- فيه زر **Authorize** فوق على اليمين، وأيقونة قفل على كل endpoint.
- بعد Authorize، الـ endpoints بتتنفذ بالـ token أوتوماتيك، وبعد Refresh للصفحة الـ token بيفضل.
- الـ enums (`gender` و`dayOfWeek` و`preferredLanguage`) ظاهرة كنص (Male / Sunday / Ar) مش أرقام.
- `startTime` و`endTime` بيظهروا بصيغة وقت (`HH:mm:ss`)، و`dateOfBirth` بصيغة تاريخ.
- كل endpoint عليه الـ Responses المتوقعة (200 / 201 / 400 / 401 / 403 / 404 / 409).

---

## 10. لو حاجة مطلعتش زي المتوقع

| العرض | السبب المحتمل |
|---|---|
| المشروع مش بيشتغل ويطلع `Jwt:Key is missing...` | ما حطيتش `Jwt:Key` في User Secrets، أو أقل من 32 حرف |
| Login بـ `@test.local` بيرجع 401 | الـ API مش شغال في Development، أو `seed-all-tables.sql` ما اتشغلش. شغّل المشروع تاني وتأكد إن البيئة Development |
| كل الـ endpoints المحمية بترجع 401 بعد Authorize | الـ token اتلصق بكلمة `Bearer` معاه، أو اتنسخ بعلامات التنصيص، أو انتهت صلاحيته (60 دقيقة) |
| 403 على endpoint المفروض يشتغل | الـ token بتاع دور تاني. اعمل Login بالحساب الصح |
| `GET /api/doctors` فاضي | ما فيش دكاترة معتمدين، أو عياداتهم موقوفة |
| 500 | بص على Console أو ملف `Clinic.API/logs/clinic-<التاريخ>.log` |
