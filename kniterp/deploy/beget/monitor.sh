#!/usr/bin/env bash
# Наблюдение за knitERP на сервере (D62): cron каждую минуту запускает ./monitor.sh.
# Проверки: /health приложения, срок сертификата HTTPS, место на диске, свежесть резервных копий.
# Оповещение — после двух неудачных проверок подряд (одиночный сбой не будит людей) и ещё раз, когда всё
# восстановилось. Куда: журнал системы (journalctl -t kniterp-monitor), Telegram (TELEGRAM_BOT_TOKEN и TELEGRAM_CHAT_ID
# в .env) и почта (ALERT_EMAIL_TO и SMTP_* в .env — через почтовый ящик, например Яндекса: порт 25 у облачных серверов
# обычно закрыт, поэтому письмо уходит через SMTP с входом). Нерешённая проблема напоминает о себе раз в 6 часов.
# ./monitor.sh test — отправить пробное оповещение во все настроенные каналы.
# ./monitor.sh status — текущее состояние всех проверок без оповещений.
set -uo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a

HEALTH_URL="${HEALTH_URL:-http://127.0.0.1:8080/health}"
STATE_DIR="${MONITOR_STATE_DIR:-/var/lib/kniterp-monitor}"
DISK_MIN_FREE_PCT="${DISK_MIN_FREE_PCT:-10}"
FULL_MAX_AGE_H="${FULL_MAX_AGE_H:-26}"
LOG_MAX_AGE_MIN="${LOG_MAX_AGE_MIN:-60}"
CERT_MIN_DAYS="${CERT_MIN_DAYS:-14}"
REMIND_MIN="${REMIND_MIN:-360}"
MODE="${1:-check}"
mkdir -p "$STATE_DIR"
chmod 700 "$STATE_DIR"

# Каждая проверка печатает пустую строку, если всё в порядке, иначе — описание проблемы.
check_health() {
  local body
  body="$(curl -fsS --max-time 10 "$HEALTH_URL" 2>/dev/null)" || { echo "Приложение не отвечает ($HEALTH_URL)"; return; }
  [[ "$body" == Healthy ]] || echo "Приложение отвечает «$body» — база недоступна или не применены миграции"
}

check_disk() {
  local free
  free="$(df -P "${BACKUP_DIR:-/}" | awk 'NR==2 { print 100 - $5 }')"
  (( free >= DISK_MIN_FREE_PCT )) || echo "Свободно ${free}% диска (порог ${DISK_MIN_FREE_PCT}%)"
}

# Возраст самого свежего файла по маске в минутах; пусто — файлов нет.
newest_age_min() {
  local newest
  newest="$(find "$BACKUP_DIR/enc" -name "$1" -printf '%T@\n' 2>/dev/null | sort -n | tail -1)"
  [[ -n "$newest" ]] && echo $(( ( $(date +%s) - ${newest%.*} ) / 60 ))
}

check_backups() {
  local full log
  full="$(newest_age_min 'kniterp-full-*.enc')"
  if [[ -z "$full" ]]; then echo "Нет ни одной полной резервной копии в $BACKUP_DIR/enc"; return; fi
  (( full <= FULL_MAX_AGE_H * 60 )) || { echo "Последняя полная копия — $(( full / 60 )) ч назад (порог ${FULL_MAX_AGE_H} ч)"; return; }
  log="$(newest_age_min 'kniterp-log-*.enc')"
  # Первая копия журнала появляется через 15 минут после первой полной.
  if [[ -z "$log" ]]; then (( full <= LOG_MAX_AGE_MIN )) || echo "Нет копий журнала транзакций"; return; fi
  (( log <= LOG_MAX_AGE_MIN )) || echo "Последняя копия журнала — $log мин назад (порог $LOG_MAX_AGE_MIN мин)"
}

check_cert() {
  [[ -n "${KNITERP_DOMAIN:-}" ]] || return 0
  local end days
  end="$(echo | timeout 15 openssl s_client -connect "$KNITERP_DOMAIN:443" -servername "$KNITERP_DOMAIN" 2>/dev/null \
         | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2)"
  [[ -n "$end" ]] || { echo "Не удалось получить сертификат https://$KNITERP_DOMAIN"; return; }
  days=$(( ( $(date -d "$end" +%s) - $(date +%s) ) / 86400 ))
  (( days >= CERT_MIN_DAYS )) || echo "Сертификат HTTPS истекает через $days дн. — проверьте Caddy (journalctl -u caddy)"
}

send_email() {
  local subject="$1" text="$2" to
  {
    printf 'From: knitERP <%s>\r\n' "${SMTP_FROM:-$SMTP_USER}"
    printf 'To: %s\r\n' "$ALERT_EMAIL_TO"
    printf 'Subject: =?UTF-8?B?%s?=\r\n' "$(printf '%s' "$subject" | base64 -w0)"
    printf 'Date: %s\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\nContent-Transfer-Encoding: base64\r\n\r\n' "$(date -R)"
    printf '%s\n' "$text" | base64
  } > "$STATE_DIR/mail.eml"
  local rcpt=()
  for to in ${ALERT_EMAIL_TO//,/ }; do rcpt+=(--mail-rcpt "$to"); done
  # Шифрование обязательно (smtps:// или STARTTLS); SMTP_REQUIRE_TLS=no — только для проверки на тестовом стенде.
  local tls=(--ssl-reqd)
  [[ "${SMTP_REQUIRE_TLS:-yes}" == no ]] && tls=()
  curl -fsS --max-time 30 "${tls[@]}" "$SMTP_URL" --user "$SMTP_USER:$SMTP_PASSWORD" \
    --mail-from "${SMTP_FROM:-$SMTP_USER}" "${rcpt[@]}" --upload-file "$STATE_DIR/mail.eml" >/dev/null
}

notify() {
  local text="knitERP ${KNITERP_DOMAIN:-}: $1"
  logger -t kniterp-monitor -- "$1"
  if [[ -n "${TELEGRAM_BOT_TOKEN:-}" && -n "${TELEGRAM_CHAT_ID:-}" ]]; then
    curl -fsS --max-time 15 "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/sendMessage" \
      --data-urlencode "chat_id=$TELEGRAM_CHAT_ID" --data-urlencode "text=$text" >/dev/null \
      || logger -t kniterp-monitor -- "Не удалось отправить оповещение в Telegram"
  fi
  if [[ -n "${ALERT_EMAIL_TO:-}" && -n "${SMTP_URL:-}" && -n "${SMTP_USER:-}" ]]; then
    send_email "$text" "$1" || logger -t kniterp-monitor -- "Не удалось отправить оповещение на почту"
  fi
}

# Состояние проверки: число неудач подряд, было ли оповещение и когда.
run() {
  local name="$1" problem="$2" file="$STATE_DIR/$1" fails=0 alerted=0 at=0 now
  now="$(date +%s)"
  [[ -f "$file" ]] && read -r fails alerted at < "$file"
  if [[ "$MODE" == status ]]; then
    printf '%-8s %s\n' "$name" "${problem:-в порядке}"
    return
  fi

  if [[ -z "$problem" ]]; then
    (( alerted )) && notify "Восстановлено: $name"
    echo "0 0 0" > "$file"
    return
  fi

  fails=$(( fails + 1 ))
  if (( fails >= 2 )) && { (( ! alerted )) || (( now - at >= REMIND_MIN * 60 )); }; then
    notify "Проблема: $problem"
    alerted=1; at=$now
  fi
  echo "$fails $alerted $at" > "$file"
}

if [[ "$MODE" == test ]]; then
  notify "Пробное оповещение: каналы настроены. Время сервера $(date '+%d.%m.%Y %H:%M')."
  echo "Отправлено во все настроенные каналы; ошибки — в journalctl -t kniterp-monitor."
  exit 0
fi

run health "$(check_health)"
run disk "$(check_disk)"
run backups "$(check_backups)"
# Сертификат меняется медленно — раз в час достаточно (и при ручном status).
if [[ "$MODE" == status || "$(date +%M)" == 07 ]]; then run cert "$(check_cert)"; fi
