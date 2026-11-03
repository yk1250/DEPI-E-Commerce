-- Seed: التخصصات والعيادات التجريبية
-- شغّل الملف ده في SSMS على قاعدة ClinicDb
-- آمن للتكرار: مابيكررش الصفوف لو اتشغل تاني

USE ClinicDb;
GO

-- التخصصات
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

-- العيادات
IF NOT EXISTS (SELECT 1 FROM dbo.Clinics WHERE Name = N'Test Clinic 1')
    INSERT INTO dbo.Clinics (Name, Address, City, Phone, IsActive)
    VALUES (N'Test Clinic 1', N'Test Address 1', N'Faiyum', N'0100000001', 1);

IF NOT EXISTS (SELECT 1 FROM dbo.Clinics WHERE Name = N'Test Clinic 2')
    INSERT INTO dbo.Clinics (Name, Address, City, Phone, IsActive)
    VALUES (N'Test Clinic 2', N'Test Address 2', N'Faiyum', N'0100000002', 1);

-- تأكيد
SELECT 'Specialties' AS TableName, COUNT(*) AS Total FROM dbo.Specialties
UNION ALL
SELECT 'Clinics', COUNT(*) FROM dbo.Clinics;
GO
