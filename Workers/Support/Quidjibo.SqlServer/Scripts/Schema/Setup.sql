IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = N'Quidjibo')
BEGIN
EXEC sys.sp_executesql N'CREATE SCHEMA [Quidjibo]'
END