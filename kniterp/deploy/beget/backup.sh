#!/usr/bin/env bash
# Резервная копия knitERP (D54): ./backup.sh full | log
# SQL Server Express не шифрует копии сам, поэтому файл сразу шифруется на сервере (AES-256, ключ /opt/kniterp/backup.key)
# и исходник удаляется. Хранение 35 дней. Если в .env задан RCLONE_REMOTE (например, beget-s3:kniterp-backups),
# зашифрованные копии ещё и выгружаются во внешнее хранилище.
set -euo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a
umask 077
KIND="${1:-full}"
STAMP="$(date -u +%Y%m%d-%H%M%S)"
KEY=/opt/kniterp/backup.key
sql() { docker compose exec -T sql /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P "$SQL_SA_PASSWORD" "$@"; }

case "$KIND" in
  full)
    FILE="kniterp-full-$STAMP.bak"
    sql -Q "BACKUP DATABASE [kniterp] TO DISK = N'/var/opt/mssql/backup/$FILE' WITH CHECKSUM, INIT, NAME = N'knitERP full $STAMP';
            RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/$FILE' WITH CHECKSUM;" >/dev/null ;;
  log)
    # Журнал копируется только после первой полной копии.
    ls "$BACKUP_DIR"/enc/kniterp-full-*.enc >/dev/null 2>&1 || exit 0
    FILE="kniterp-log-$STAMP.trn"
    sql -Q "BACKUP LOG [kniterp] TO DISK = N'/var/opt/mssql/backup/$FILE' WITH CHECKSUM, INIT;" >/dev/null ;;
  *) echo "Использование: $0 full|log"; exit 2 ;;
esac

gzip -c "$BACKUP_DIR/raw/$FILE" | openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -pass "file:$KEY" > "$BACKUP_DIR/enc/$FILE.gz.enc"
rm -f "$BACKUP_DIR/raw/$FILE"
find "$BACKUP_DIR/enc" -name 'kniterp-*' -mtime +35 -delete

if [[ -n "${RCLONE_REMOTE:-}" ]] && command -v rclone >/dev/null; then
  rclone copy "$BACKUP_DIR/enc/$FILE.gz.enc" "$RCLONE_REMOTE" --quiet
fi
echo "$(date -u +%FT%TZ) $KIND ok $FILE.gz.enc"
