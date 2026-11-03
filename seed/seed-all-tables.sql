-- =====================================================================
-- Seed كل الجداول (بيانات تجريبية)
-- شغّله في SSMS على ClinicDb. آمن للتكرار (كل insert بيتحقق الأول).
-- الترتيب مهم عشان الـ Foreign Keys: Users > Specialties/Clinics > Doctors/Patients/Receptionists > ...
-- =====================================================================
USE ClinicDb;
GO

-- ---------------------------------------------------------------------
-- 1) التخصصات والعيادات
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Specialties WHERE NameEn = N'Cardiology')
    INSERT INTO dbo.Specialties (NameAr, NameEn) VALUES (N'قلب', N'Cardiology');
IF NOT EXISTS (SELECT 1 FROM dbo.Specialties WHERE NameEn = N'Dermatology')
    INSERT INTO dbo.Specialties (NameAr, NameEn) VALUES (N'جلدية', N'Dermatology');
IF NOT EXISTS (SELECT 1 FROM dbo.Specialties WHERE NameEn = N'Pediatrics')
    INSERT INTO dbo.Specialties (NameAr, NameEn) VALUES (N'أطفال', N'Pediatrics');
IF NOT EXISTS (SELECT 1 FROM dbo.Specialties WHERE NameEn = N'Orthopedics')
    INSERT INTO dbo.Specialties (NameAr, NameEn) VALUES (N'عظام', N'Orthopedics');
IF NOT EXISTS (SELECT 1 FROM dbo.Specialties WHERE NameEn = N'Dentistry')
    INSERT INTO dbo.Specialties (NameAr, NameEn) VALUES (N'أسنان', N'Dentistry');
IF NOT EXISTS (SELECT 1 FROM dbo.Specialties WHERE NameEn = N'Internal Medicine')
    INSERT INTO dbo.Specialties (NameAr, NameEn) VALUES (N'باطنة', N'Internal Medicine');

IF NOT EXISTS (SELECT 1 FROM dbo.Clinics WHERE Name = N'Test Clinic 1')
    INSERT INTO dbo.Clinics (Name, Address, City, Phone, IsActive)
    VALUES (N'Test Clinic 1', N'Test Address 1', N'Faiyum', N'0100000001', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Clinics WHERE Name = N'Test Clinic 2')
    INSERT INTO dbo.Clinics (Name, Address, City, Phone, IsActive)
    VALUES (N'Test Clinic 2', N'Test Address 2', N'Faiyum', N'0100000002', 1);

-- ---------------------------------------------------------------------
-- 2) حسابات Identity (AspNetUsers)
--    PasswordHash = NULL: الحسابات دي للبيانات والـ FK بس، مش هتقدر تسجل دخول بالباسورد
--    لحد ما نعمل Register من الـ API في مرحلة Auth.
-- ---------------------------------------------------------------------
DECLARE @Admin1 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @Doc1   UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000011';
DECLARE @Doc2   UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000012';
DECLARE @Doc3   UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000013';
DECLARE @Pat1   UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000021';
DECLARE @Pat2   UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000022';
DECLARE @Pat3   UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000023';
DECLARE @Rec1   UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000031';

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Admin1)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Admin1, N'admin@test.local', N'ADMIN@TEST.LOCAL', N'admin@test.local', N'ADMIN@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000000', 0, 0, 1, 0, N'Admin Test', 1, 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Doc1)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Doc1, N'doctor1@test.local', N'DOCTOR1@TEST.LOCAL', N'doctor1@test.local', N'DOCTOR1@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000011', 0, 0, 1, 0, N'Dr Ahmed Test', 0, 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Doc2)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Doc2, N'doctor2@test.local', N'DOCTOR2@TEST.LOCAL', N'doctor2@test.local', N'DOCTOR2@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000012', 0, 0, 1, 0, N'Dr Sara Test', 0, 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Doc3)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Doc3, N'doctor3@test.local', N'DOCTOR3@TEST.LOCAL', N'doctor3@test.local', N'DOCTOR3@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000013', 0, 0, 1, 0, N'Dr Mona Test', 0, 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Pat1)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Pat1, N'patient1@test.local', N'PATIENT1@TEST.LOCAL', N'patient1@test.local', N'PATIENT1@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000021', 0, 0, 1, 0, N'Ali Test', 0, 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Pat2)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Pat2, N'patient2@test.local', N'PATIENT2@TEST.LOCAL', N'patient2@test.local', N'PATIENT2@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000022', 0, 0, 1, 0, N'Mariam Test', 0, 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Pat3)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Pat3, N'patient3@test.local', N'PATIENT3@TEST.LOCAL', N'patient3@test.local', N'PATIENT3@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000023', 0, 0, 1, 0, N'Omar Test', 1, 1, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers WHERE Id = @Rec1)
    INSERT INTO dbo.AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FullName, PreferredLanguage, IsActive, CreatedAt)
    VALUES (@Rec1, N'reception1@test.local', N'RECEPTION1@TEST.LOCAL', N'reception1@test.local', N'RECEPTION1@TEST.LOCAL', 1, NULL, CONVERT(NVARCHAR(MAX), NEWID()), CONVERT(NVARCHAR(MAX), NEWID()), N'0100000031', 0, 0, 1, 0, N'Reception Test', 0, 1, SYSUTCDATETIME());

-- ---------------------------------------------------------------------
-- 3) الدكاترة والمرضى والاستقبال
--    Gender: Male=0, Female=1 | IsApproved: 1=معتمد
-- ---------------------------------------------------------------------
DECLARE @Cardio INT = (SELECT Id FROM dbo.Specialties WHERE NameEn = N'Cardiology');
DECLARE @Derma  INT = (SELECT Id FROM dbo.Specialties WHERE NameEn = N'Dermatology');
DECLARE @Pedia  INT = (SELECT Id FROM dbo.Specialties WHERE NameEn = N'Pediatrics');
DECLARE @Clin1  INT = (SELECT Id FROM dbo.Clinics WHERE Name = N'Test Clinic 1');
DECLARE @Clin2  INT = (SELECT Id FROM dbo.Clinics WHERE Name = N'Test Clinic 2');

IF NOT EXISTS (SELECT 1 FROM dbo.Doctors WHERE UserId = @Doc1)
    INSERT INTO dbo.Doctors (UserId, SpecialtyId, ClinicId, Bio, YearsOfExperience, ConsultationFee, FollowUpFeePercent, FollowUpWindowDays, AverageRating, ReviewsCount, IsApproved)
    VALUES (@Doc1, @Cardio, @Clin1, N'استشاري قلب وأوعية دموية', 12, 300.00, 50.00, 14, 4.00, 1, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.Doctors WHERE UserId = @Doc2)
    INSERT INTO dbo.Doctors (UserId, SpecialtyId, ClinicId, Bio, YearsOfExperience, ConsultationFee, FollowUpFeePercent, FollowUpWindowDays, AverageRating, ReviewsCount, IsApproved)
    VALUES (@Doc2, @Derma, @Clin1, N'أخصائية أمراض جلدية', 8, 250.00, 0.00, 14, 5.00, 1, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.Doctors WHERE UserId = @Doc3)
    INSERT INTO dbo.Doctors (UserId, SpecialtyId, ClinicId, Bio, YearsOfExperience, ConsultationFee, FollowUpFeePercent, FollowUpWindowDays, AverageRating, ReviewsCount, IsApproved)
    VALUES (@Doc3, @Pedia, @Clin2, N'استشارية أطفال (في انتظار الاعتماد)', 5, 200.00, 30.00, 7, 0.00, 0, 0);

IF NOT EXISTS (SELECT 1 FROM dbo.Patients WHERE UserId = @Pat1)
    INSERT INTO dbo.Patients (UserId, DateOfBirth, Gender) VALUES (@Pat1, '1990-05-12', 0);
IF NOT EXISTS (SELECT 1 FROM dbo.Patients WHERE UserId = @Pat2)
    INSERT INTO dbo.Patients (UserId, DateOfBirth, Gender) VALUES (@Pat2, '1985-11-03', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Patients WHERE UserId = @Pat3)
    INSERT INTO dbo.Patients (UserId, DateOfBirth, Gender) VALUES (@Pat3, '2001-02-20', 0);

IF NOT EXISTS (SELECT 1 FROM dbo.Receptionists WHERE UserId = @Rec1)
    INSERT INTO dbo.Receptionists (UserId, ClinicId) VALUES (@Rec1, @Clin1);

DECLARE @DocId1 INT = (SELECT Id FROM dbo.Doctors WHERE UserId = @Doc1);
DECLARE @DocId2 INT = (SELECT Id FROM dbo.Doctors WHERE UserId = @Doc2);
DECLARE @DocId3 INT = (SELECT Id FROM dbo.Doctors WHERE UserId = @Doc3);
DECLARE @PatId1 INT = (SELECT Id FROM dbo.Patients WHERE UserId = @Pat1);
DECLARE @PatId2 INT = (SELECT Id FROM dbo.Patients WHERE UserId = @Pat2);
DECLARE @PatId3 INT = (SELECT Id FROM dbo.Patients WHERE UserId = @Pat3);

-- ---------------------------------------------------------------------
-- 4) أوقات العمل والإجازات
--    DayOfWeek: Sunday=0 ... Saturday=6
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.WorkingSchedules WHERE DoctorId = @DocId1 AND DayOfWeek = 0 AND StartTime = '09:00:00')
    INSERT INTO dbo.WorkingSchedules (DoctorId, DayOfWeek, StartTime, EndTime, SlotDurationMinutes, IsActive)
    VALUES (@DocId1, 0, '09:00:00', '13:00:00', 30, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.WorkingSchedules WHERE DoctorId = @DocId1 AND DayOfWeek = 2 AND StartTime = '16:00:00')
    INSERT INTO dbo.WorkingSchedules (DoctorId, DayOfWeek, StartTime, EndTime, SlotDurationMinutes, IsActive)
    VALUES (@DocId1, 2, '16:00:00', '20:00:00', 30, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.WorkingSchedules WHERE DoctorId = @DocId2 AND DayOfWeek = 1 AND StartTime = '10:00:00')
    INSERT INTO dbo.WorkingSchedules (DoctorId, DayOfWeek, StartTime, EndTime, SlotDurationMinutes, IsActive)
    VALUES (@DocId2, 1, '10:00:00', '14:00:00', 20, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.WorkingSchedules WHERE DoctorId = @DocId3 AND DayOfWeek = 3 AND StartTime = '09:00:00')
    INSERT INTO dbo.WorkingSchedules (DoctorId, DayOfWeek, StartTime, EndTime, SlotDurationMinutes, IsActive)
    VALUES (@DocId3, 3, '09:00:00', '12:00:00', 30, 1);

IF NOT EXISTS (SELECT 1 FROM dbo.TimeOffs WHERE DoctorId = @DocId1 AND StartAt = '2026-11-10T00:00:00')
    INSERT INTO dbo.TimeOffs (DoctorId, StartAt, EndAt, Reason)
    VALUES (@DocId1, '2026-11-10T00:00:00', '2026-11-12T23:59:00', N'إجازة سفر');

-- ---------------------------------------------------------------------
-- 5) المواعيد
--    Type: New=0, FollowUp=1 | Status: PendingPayment=0, Confirmed=1, Completed=3, Expired=6
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Appointments WHERE DoctorId = @DocId1 AND StartAt = '2026-10-20T09:00:00')
    INSERT INTO dbo.Appointments (DoctorId, PatientId, PreviousAppointmentId, StartAt, EndAt, Type, Status, PriceSnapshot, ExpiresAt, CancelledAt, CancelReason, CreatedAt)
    VALUES (@DocId1, @PatId1, NULL, '2026-10-20T09:00:00', '2026-10-20T09:30:00', 0, 3, 300.00, NULL, NULL, NULL, '2026-10-15T10:00:00');

DECLARE @Apt1 INT = (SELECT Id FROM dbo.Appointments WHERE DoctorId = @DocId1 AND StartAt = '2026-10-20T09:00:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Appointments WHERE DoctorId = @DocId1 AND StartAt = '2026-11-01T09:30:00')
    INSERT INTO dbo.Appointments (DoctorId, PatientId, PreviousAppointmentId, StartAt, EndAt, Type, Status, PriceSnapshot, ExpiresAt, CancelledAt, CancelReason, CreatedAt)
    VALUES (@DocId1, @PatId1, @Apt1, '2026-11-01T09:30:00', '2026-11-01T10:00:00', 1, 1, 150.00, NULL, NULL, NULL, '2026-10-26T12:00:00');

DECLARE @Apt2 INT = (SELECT Id FROM dbo.Appointments WHERE DoctorId = @DocId1 AND StartAt = '2026-11-01T09:30:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Appointments WHERE DoctorId = @DocId2 AND StartAt = '2026-10-26T10:00:00')
    INSERT INTO dbo.Appointments (DoctorId, PatientId, PreviousAppointmentId, StartAt, EndAt, Type, Status, PriceSnapshot, ExpiresAt, CancelledAt, CancelReason, CreatedAt)
    VALUES (@DocId2, @PatId3, NULL, '2026-10-26T10:00:00', '2026-10-26T10:20:00', 0, 3, 250.00, NULL, NULL, NULL, '2026-10-22T09:00:00');

DECLARE @Apt3 INT = (SELECT Id FROM dbo.Appointments WHERE DoctorId = @DocId2 AND StartAt = '2026-10-26T10:00:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Appointments WHERE DoctorId = @DocId2 AND StartAt = '2026-11-02T11:00:00')
    INSERT INTO dbo.Appointments (DoctorId, PatientId, PreviousAppointmentId, StartAt, EndAt, Type, Status, PriceSnapshot, ExpiresAt, CancelledAt, CancelReason, CreatedAt)
    VALUES (@DocId2, @PatId2, NULL, '2026-11-02T11:00:00', '2026-11-02T11:20:00', 0, 0, 250.00, '2026-10-27T18:00:00', NULL, NULL, '2026-10-27T17:45:00');

DECLARE @Apt4 INT = (SELECT Id FROM dbo.Appointments WHERE DoctorId = @DocId2 AND StartAt = '2026-11-02T11:00:00');

-- ---------------------------------------------------------------------
-- 6) قائمة الانتظار والكوبونات والدفع
--    Method: Online=0, AtClinic=1 | Status: Pending=0, Paid=1 | DiscountType: Percent=0, Fixed=1
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.WaitlistEntries WHERE DoctorId = @DocId1 AND PatientId = @PatId2 AND DesiredDate = '2026-11-08')
    INSERT INTO dbo.WaitlistEntries (DoctorId, PatientId, DesiredDate, IsActive, NotifiedAt)
    VALUES (@DocId1, @PatId2, '2026-11-08', 1, NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.Coupons WHERE Code = N'WELCOME10')
    INSERT INTO dbo.Coupons (Code, DiscountType, DiscountValue, ExpiresAt, MaxUses, UsedCount, IsActive)
    VALUES (N'WELCOME10', 0, 10.00, '2026-12-31T23:59:00', 100, 1, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Coupons WHERE Code = N'FIXED50')
    INSERT INTO dbo.Coupons (Code, DiscountType, DiscountValue, ExpiresAt, MaxUses, UsedCount, IsActive)
    VALUES (N'FIXED50', 1, 50.00, '2026-12-31T23:59:00', 10, 0, 1);

DECLARE @Coupon1 INT = (SELECT Id FROM dbo.Coupons WHERE Code = N'WELCOME10');

IF NOT EXISTS (SELECT 1 FROM dbo.Payments WHERE AppointmentId = @Apt1)
    INSERT INTO dbo.Payments (AppointmentId, CouponId, Method, Status, OriginalAmount, DiscountAmount, Amount, PaymobOrderId, TransactionId, PaidAt, CreatedAt)
    VALUES (@Apt1, NULL, 1, 1, 300.00, 0.00, 300.00, NULL, NULL, '2026-10-20T09:30:00', '2026-10-15T10:00:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Payments WHERE AppointmentId = @Apt2)
    INSERT INTO dbo.Payments (AppointmentId, CouponId, Method, Status, OriginalAmount, DiscountAmount, Amount, PaymobOrderId, TransactionId, PaidAt, CreatedAt)
    VALUES (@Apt2, NULL, 0, 1, 150.00, 0.00, 150.00, N'PMB-100001', N'TXN-100001', '2026-10-26T12:05:00', '2026-10-26T12:00:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Payments WHERE AppointmentId = @Apt3)
    INSERT INTO dbo.Payments (AppointmentId, CouponId, Method, Status, OriginalAmount, DiscountAmount, Amount, PaymobOrderId, TransactionId, PaidAt, CreatedAt)
    VALUES (@Apt3, NULL, 1, 1, 250.00, 0.00, 250.00, NULL, NULL, '2026-10-26T10:00:00', '2026-10-22T09:00:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Payments WHERE AppointmentId = @Apt4)
    INSERT INTO dbo.Payments (AppointmentId, CouponId, Method, Status, OriginalAmount, DiscountAmount, Amount, PaymobOrderId, TransactionId, PaidAt, CreatedAt)
    VALUES (@Apt4, @Coupon1, 0, 0, 250.00, 25.00, 225.00, N'PMB-100002', NULL, NULL, '2026-10-27T17:45:00');

-- ---------------------------------------------------------------------
-- 7) السجل الطبي والمرفقات والتقييمات
--    ملاحظة: البيانات الطبية هنا plain text للتجربة، والتشفير يتضاف في مرحلة السجل الطبي.
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.MedicalHistories WHERE PatientId = @PatId1)
    INSERT INTO dbo.MedicalHistories (PatientId, ChronicConditions, Allergies, CurrentMedications, UpdatedAt)
    VALUES (@PatId1, N'ضغط دم مرتفع', N'البنسلين', N'ليزينوبريل 10 مج', SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.MedicalHistories WHERE PatientId = @PatId2)
    INSERT INTO dbo.MedicalHistories (PatientId, ChronicConditions, Allergies, CurrentMedications, UpdatedAt)
    VALUES (@PatId2, NULL, N'لا يوجد', NULL, SYSUTCDATETIME());

IF NOT EXISTS (SELECT 1 FROM dbo.Attachments WHERE PatientId = @PatId1 AND FileName = N'lab-results.pdf')
    INSERT INTO dbo.Attachments (PatientId, AppointmentId, UploadedByUserId, FileName, StoredPath, ContentType, SizeBytes, UploadedAt)
    VALUES (@PatId1, @Apt1, @Pat1, N'lab-results.pdf', N'/secure-uploads/test/lab-results.pdf', N'application/pdf', 245760, '2026-10-20T09:10:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Reviews WHERE AppointmentId = @Apt1)
    INSERT INTO dbo.Reviews (AppointmentId, DoctorId, PatientId, Rating, Comment, CreatedAt)
    VALUES (@Apt1, @DocId1, @PatId1, 4, N'كشف كويس والدكتور شرح كل حاجة', '2026-10-21T08:00:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Reviews WHERE AppointmentId = @Apt3)
    INSERT INTO dbo.Reviews (AppointmentId, DoctorId, PatientId, Rating, Comment, CreatedAt)
    VALUES (@Apt3, @DocId2, @PatId3, 5, N'ممتازة ومتعاونة', '2026-10-27T09:00:00');

-- ---------------------------------------------------------------------
-- 8) المحادثات والرسائل والإشعارات
--    Notification Type: Reminder=0, Waitlist=1, Booking=2, Message=3 | Channel: InApp=0, Email=1, Sms=2
-- ---------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.Conversations WHERE AppointmentId = @Apt1)
    INSERT INTO dbo.Conversations (AppointmentId, DoctorId, PatientId, OpenedAt, ExpiresAt, IsClosed)
    VALUES (@Apt1, @DocId1, @PatId1, '2026-10-20T09:30:00', '2026-11-03T09:30:00', 0);

DECLARE @Conv1 INT = (SELECT Id FROM dbo.Conversations WHERE AppointmentId = @Apt1);

IF NOT EXISTS (SELECT 1 FROM dbo.Messages WHERE ConversationId = @Conv1 AND Body = N'السلام عليكم دكتور، نتيجة التحليل نزلت')
    INSERT INTO dbo.Messages (ConversationId, SenderUserId, AttachmentId, Body, SentAt, ReadAt)
    VALUES (@Conv1, @Pat1, NULL, N'السلام عليكم دكتور، نتيجة التحليل نزلت', '2026-10-21T10:00:00', '2026-10-21T10:05:00');

IF NOT EXISTS (SELECT 1 FROM dbo.Messages WHERE ConversationId = @Conv1 AND Body = N'تمام، ابعتهالي هنا')
    INSERT INTO dbo.Messages (ConversationId, SenderUserId, AttachmentId, Body, SentAt, ReadAt)
    VALUES (@Conv1, @Doc1, NULL, N'تمام، ابعتهالي هنا', '2026-10-21T10:06:00', NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.Notifications WHERE UserId = @Pat1 AND Title = N'تم تأكيد الحجز')
    INSERT INTO dbo.Notifications (UserId, AppointmentId, Type, Channel, Title, Body, ScheduledFor, SentAt, ReadAt)
    VALUES (@Pat1, @Apt2, 2, 0, N'تم تأكيد الحجز', N'تم تأكيد موعدك مع الدكتور أحمد يوم الأحد 09:30', NULL, '2026-10-26T12:05:00', NULL);

IF NOT EXISTS (SELECT 1 FROM dbo.Notifications WHERE UserId = @Pat1 AND Title = N'تذكير بالموعد')
    INSERT INTO dbo.Notifications (UserId, AppointmentId, Type, Channel, Title, Body, ScheduledFor, SentAt, ReadAt)
    VALUES (@Pat1, @Apt2, 0, 1, N'تذكير بالموعد', N'موعدك بكرة الساعة 09:30', '2026-10-31T09:30:00', NULL, NULL);

-- ---------------------------------------------------------------------
-- تأكيد: عدد الصفوف في كل جدول
-- ---------------------------------------------------------------------
SELECT 'AspNetUsers' AS TableName, COUNT(*) AS Total FROM dbo.AspNetUsers
UNION ALL SELECT 'Specialties', COUNT(*) FROM dbo.Specialties
UNION ALL SELECT 'Clinics', COUNT(*) FROM dbo.Clinics
UNION ALL SELECT 'Doctors', COUNT(*) FROM dbo.Doctors
UNION ALL SELECT 'Patients', COUNT(*) FROM dbo.Patients
UNION ALL SELECT 'Receptionists', COUNT(*) FROM dbo.Receptionists
UNION ALL SELECT 'WorkingSchedules', COUNT(*) FROM dbo.WorkingSchedules
UNION ALL SELECT 'TimeOffs', COUNT(*) FROM dbo.TimeOffs
UNION ALL SELECT 'Appointments', COUNT(*) FROM dbo.Appointments
UNION ALL SELECT 'WaitlistEntries', COUNT(*) FROM dbo.WaitlistEntries
UNION ALL SELECT 'Coupons', COUNT(*) FROM dbo.Coupons
UNION ALL SELECT 'Payments', COUNT(*) FROM dbo.Payments
UNION ALL SELECT 'MedicalHistories', COUNT(*) FROM dbo.MedicalHistories
UNION ALL SELECT 'Attachments', COUNT(*) FROM dbo.Attachments
UNION ALL SELECT 'Reviews', COUNT(*) FROM dbo.Reviews
UNION ALL SELECT 'Conversations', COUNT(*) FROM dbo.Conversations
UNION ALL SELECT 'Messages', COUNT(*) FROM dbo.Messages
UNION ALL SELECT 'Notifications', COUNT(*) FROM dbo.Notifications;
GO
