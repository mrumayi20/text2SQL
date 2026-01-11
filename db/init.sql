IF DB_ID('Text2SqlDemo') IS NULL
BEGIN
    CREATE DATABASE Text2SqlDemo;
END
GO

USE Text2SqlDemo;
GO

IF OBJECT_ID('dbo.Orders', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Orders (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CustomerName NVARCHAR(100) NOT NULL,
        OrderDate DATE NOT NULL,
        TotalAmount DECIMAL(10,2) NOT NULL
    );
END
GO

-- Seed only if empty
IF NOT EXISTS (SELECT 1 FROM dbo.Orders)
BEGIN
    INSERT INTO dbo.Orders (CustomerName, OrderDate, TotalAmount) VALUES
    ('Alice', '2025-01-05', 120.50),
    ('Bob',   '2025-01-20', 75.00),
    ('Alice', '2025-02-02', 220.10),
    ('Cara',  '2025-02-18', 310.00),
    ('Bob',   '2025-03-07', 45.99);
END
GO
