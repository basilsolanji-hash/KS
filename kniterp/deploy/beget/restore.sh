#!/usr/bin/env bash
# Восстановление базы из зашифрованных копий: ./restore.sh <полная копия .bak.gz.enc> [копии журнала .trn.gz.enc …]
# Восстанавливает в базу kniterp_restore (рабочая не трогается) — для ежемесячной проверки восстановления (D54).
# Чтобы заменить рабочую базу, укажите TARGET=kniterp и остановите приложение (docker compose stop app).
set -euo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a
TARGET="${TARGET:-kniterp_restore}"
KEY="${BACKUP_KEY_FILE:-/opt/kniterp/backup.key}"
[[ $# -ge 1 ]] || { echo "Использование: $0 full.bak.gz.enc [log1.trn.gz.enc …]"; exit 2; }
sql() { docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SQL_SA_PASSWORD" "$@"; }
decrypt() { openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass "file:$KEY" -in "$1" | gunzip > "$BACKUP_DIR/raw/$2"; chown 10001:0 "$BACKUP_DIR/raw/$2"; }

FULL="restore-full.bak"; decrypt "$1" "$FULL"; shift
MOVE="MOVE N'kniterp' TO N'/var/opt/mssql/data/$TARGET.mdf', MOVE N'kniterp_log' TO N'/var/opt/mssql/data/${TARGET}_log.ldf'"
sql -Q "RESTORE DATABASE [$TARGET] FROM DISK = N'/var/opt/mssql/backup/$FULL' WITH $MOVE, REPLACE, NORECOVERY, CHECKSUM;"
n=0
for LOG in "$@"; do
  n=$((n+1)); decrypt "$LOG" "restore-$n.trn"
  sql -Q "RESTORE LOG [$TARGET] FROM DISK = N'/var/opt/mssql/backup/restore-$n.trn' WITH NORECOVERY, CHECKSUM;"
done
sql -Q "RESTORE DATABASE [$TARGET] WITH RECOVERY; DBCC CHECKDB([$TARGET]) WITH NO_INFOMSGS;"
rm -f "$BACKUP_DIR"/raw/restore-*
sql -Q "SELECT N'организаций' AS [что], COUNT(*) AS [сколько] FROM [$TARGET].[kniterp].[organizations]
        UNION ALL SELECT N'движений склада', COUNT(*) FROM [$TARGET].[kniterp].[stock_movements]
        UNION ALL SELECT N'записей аудита', COUNT(*) FROM [$TARGET].[kniterp].[audit_log];"
echo "Восстановлено в базу $TARGET."
