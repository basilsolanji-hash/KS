#!/usr/bin/env bash
# Выпуск новой версии knitERP на сервере (docs/operations.md): копия → сборка → миграции → запуск → проверка.
#   ./update.sh            — последняя версия ветки
set -euo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a

echo "== Код"; git -C ../.. pull --ff-only
echo "== Полная копия перед выпуском (точка отката)"; ./backup.sh full
echo "== Сборка"; docker compose build app
echo "== Миграции"; docker compose stop app
docker compose run --rm -e ConnectionStrings__KnitErp="Server=sql;Database=kniterp;User Id=sa;Password=$SQL_SA_PASSWORD;Encrypt=true;TrustServerCertificate=true" app migrate
echo "== Запуск"; docker compose up -d app
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:8080/health 2>/dev/null | grep -q Healthy; then echo "Готово: Healthy"; exit 0; fi
  sleep 2
done
echo "Приложение не ответило Healthy за минуту: docker compose logs app --tail 100"; exit 1
