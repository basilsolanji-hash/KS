-- Резервное копирование базы knitERP (SQL Server). Запускается заданием SQL Server Agent или планировщиком.
-- Копии шифруются сертификатом TDE (см. docs/security.md) и проверяются контрольной суммой.
-- Параметры: путь к папке копий (на другом диске или сетевом хранилище, не на диске с базой).
-- Допущение D54: полная копия — раз в сутки, журнал транзакций — каждые 15 минут, хранение — 35 дней.

DECLARE @folder nvarchar(400) = N'D:\Backup\kniterp\';     -- поменяйте под свой сервер
DECLARE @stamp nvarchar(20) = FORMAT(SYSUTCDATETIME(), 'yyyyMMdd-HHmmss');
DECLARE @kind nvarchar(10) = N'$(Kind)';                    -- FULL или LOG: sqlcmd -v Kind=FULL
DECLARE @file nvarchar(500);

IF @kind = N'FULL'
BEGIN
    SET @file = @folder + N'kniterp-full-' + @stamp + N'.bak';
    BACKUP DATABASE [kniterp] TO DISK = @file
        WITH COMPRESSION, CHECKSUM, ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = KnitErpTde),
             NAME = N'knitERP full', STATS = 10;
END
ELSE
BEGIN
    SET @file = @folder + N'kniterp-log-' + @stamp + N'.trn';
    BACKUP LOG [kniterp] TO DISK = @file
        WITH COMPRESSION, CHECKSUM, ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = KnitErpTde),
             NAME = N'knitERP log';
END

-- Копия должна читаться: проверка сразу после записи.
RESTORE VERIFYONLY FROM DISK = @file WITH CHECKSUM;
