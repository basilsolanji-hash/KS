-- Проверка восстановления: копия разворачивается в отдельную базу kniterp_restore_check на тестовом сервере.
-- Запускать раз в месяц и после изменения схемы резервного копирования. Рабочую базу не трогает.
-- На тестовом сервере должны быть сертификат и ключ TDE (docs/security.md), иначе шифрованную копию не открыть.

DECLARE @file nvarchar(500) = N'$(BackupFile)';             -- sqlcmd -v BackupFile="D:\Backup\kniterp\kniterp-full-....bak"

RESTORE FILELISTONLY FROM DISK = @file;                     -- логические имена файлов для MOVE ниже

RESTORE DATABASE [kniterp_restore_check] FROM DISK = @file
    WITH MOVE N'kniterp' TO N'D:\Restore\kniterp_restore_check.mdf',
         MOVE N'kniterp_log' TO N'D:\Restore\kniterp_restore_check_log.ldf',
         CHECKSUM, RECOVERY, REPLACE, STATS = 10;

DBCC CHECKDB ([kniterp_restore_check]) WITH NO_INFOMSGS;

-- Контрольные цифры: сравните с рабочей базой на момент копии.
SELECT 'organizations' AS [Таблица], COUNT(*) AS [Строк] FROM [kniterp_restore_check].[kniterp].[organizations]
UNION ALL SELECT 'stock_movements', COUNT(*) FROM [kniterp_restore_check].[kniterp].[stock_movements]
UNION ALL SELECT 'audit_log', COUNT(*) FROM [kniterp_restore_check].[kniterp].[audit_log];

-- Дальше: запустить knitERP на восстановленной базе с тем же мастер-ключом, войти Владельцем и выполнить
-- «Проверку целостности» и «Сверку». Затем удалить проверочную базу:
-- DROP DATABASE [kniterp_restore_check];
