<?php
declare(strict_types=1);

namespace Ks;

/** Производство, активность, рейтинг и журнал — часть Api. */
trait ApiWork
{
    // ---------------------------------------------------------------- Производство

    /** Нормализовать маршрут техкарты. Первый срез поддерживает один этап каждого типа. */
    private function normalizeTechOperations(array $operations, string $code, int $versionNo): array
    {
        if (!$operations || count($operations) > 50) throw new ApiError('В техкарте должно быть от 1 до 50 операций');
        $out = [];
        $seen = [];
        $outputIndexes = [];
        foreach (array_values($operations) as $i => $raw) {
            if (!is_array($raw)) throw new ApiError('Неверный формат операции');
            $stage = strtolower(trim((string)($raw['stage'] ?? '')));
            if (!isset(Rules::STAGES[$stage])) throw new ApiError('Неизвестный этап техкарты: ' . $stage);
            if (isset($seen[$stage])) throw new ApiError('Этап техкарты повторяется: ' . Rules::STAGES[$stage]);
            $seen[$stage] = true;
            $name = trim((string)($raw['name'] ?? Rules::STAGES[$stage]));
            if ($name === '') throw new ApiError('У операции должно быть название');
            $norm = $raw['normSeconds'] ?? null;
            if ($norm !== null && (!is_numeric($norm) || (int)$norm < 1 || (int)$norm > 604800)) {
                throw new ApiError('Норма времени должна быть от 1 секунды до 7 суток');
            }
            $isOutput = !empty($raw['outputStage']);
            if ($isOutput) $outputIndexes[] = $i;
            $qr = trim((string)($raw['qrCode'] ?? ''));
            if ($qr === '') $qr = sprintf('TC-%s-V%d-%02d', strtoupper($code), $versionNo, $i + 1);
            if (mb_strlen($qr) > 100) throw new ApiError('QR-код операции длиннее 100 символов');
            $out[] = [
                'sequence_no' => $i + 1,
                'stage' => $stage,
                'name' => mb_substr($name, 0, 200),
                'optional' => !empty($raw['optional']) ? 1 : 0,
                'time_tracking' => !empty($raw['timeTracking']) ? 1 : 0,
                'norm_seconds' => $norm === null ? null : (int)$norm,
                'equipment_required' => ($stage === 'knit' || !empty($raw['equipmentRequired'])) ? 1 : 0,
                'output_stage' => $isOutput ? 1 : 0,
                'qr_code' => mb_substr($qr, 0, 100),
            ];
        }
        if (count($outputIndexes) > 1) throw new ApiError('В маршруте может быть только один этап выпуска');
        if ($outputIndexes && $outputIndexes[0] !== count($out) - 1) throw new ApiError('Выпуск разрешён только на последней операции маршрута');
        if (!$outputIndexes) $out[count($out) - 1]['output_stage'] = 1;
        return $out;
    }

    /** Полный снимок версии техкарты — источник истины для уже назначенного производственного задания. */
    private function techCardVersion(int $versionId): ?array
    {
        $v = $this->db->one('SELECT v.*, c.code, c.name, c.product_name FROM tech_card_versions v
            JOIN tech_cards c ON c.id = v.tech_card_id WHERE v.id = ?', [$versionId]);
        if (!$v) return null;
        $ops = $this->db->all('SELECT sequence_no, stage, name, optional, time_tracking, norm_seconds,
            equipment_required, output_stage, qr_code FROM tech_card_operations WHERE version_id = ? ORDER BY sequence_no', [$versionId]);
        return [
            'id' => (int)$v['id'], 'cardId' => (int)$v['tech_card_id'], 'code' => $v['code'], 'name' => $v['name'],
            'productName' => $v['product_name'], 'version' => (int)$v['version_no'], 'status' => $v['status'],
            'outputQuantity' => (float)$v['output_quantity'], 'outputUnit' => $v['output_unit'],
            'plannedWastePercent' => (float)$v['planned_waste_percent'], 'snapshotHash' => $v['snapshot_hash'],
            'operations' => array_map(fn($o) => [
                'sequence' => (int)$o['sequence_no'], 'stage' => $o['stage'], 'name' => $o['name'],
                'optional' => (bool)$o['optional'], 'timeTracking' => (bool)$o['time_tracking'],
                'normSeconds' => $o['norm_seconds'] === null ? null : (int)$o['norm_seconds'],
                'equipmentRequired' => (bool)$o['equipment_required'], 'outputStage' => (bool)$o['output_stage'],
                'qrCode' => $o['qr_code'],
            ], $ops),
        ];
    }

    /** Реестр техкарт. Черновики видят только технолог, помощник и директор. */
    private function aTechCards(array $req): array
    {
        $canDraft = Rules::full($this->me['role']) || $this->me['role'] === 'designer';
        $cards = $this->db->all("SELECT * FROM tech_cards " . ($canDraft ? '' : "WHERE status = 'active' ") . 'ORDER BY code');
        $result = [];
        foreach ($cards as $card) {
            $versions = $this->db->all('SELECT id FROM tech_card_versions WHERE tech_card_id = ? ' .
                ($canDraft ? '' : "AND status = 'approved' ") . 'ORDER BY version_no DESC', [$card['id']]);
            $result[] = [
                'id' => (int)$card['id'], 'code' => $card['code'], 'name' => $card['name'],
                'productName' => $card['product_name'], 'status' => $card['status'],
                'versions' => array_values(array_filter(array_map(fn($v) => $this->techCardVersion((int)$v['id']), $versions))),
            ];
        }
        return ['techCards' => $result];
    }

    /** Сохранить новую техкарту или новую/существующую черновую версию. */
    private function aTechCardSave(array $req): array
    {
        $this->need('assistant', 'designer');
        $card = (array)($req['card'] ?? []);
        $id = (int)($card['id'] ?? 0);
        $code = trim((string)($card['code'] ?? ''));
        $name = trim((string)($card['name'] ?? ''));
        if (!preg_match('/^[\pL\pN][\pL\pN._-]{1,63}$/u', $code)) throw new ApiError('Код техкарты: 2–64 буквы, цифры, точка, дефис или подчёркивание');
        if ($name === '') throw new ApiError('Нужно название техкарты');
        $outputQty = $card['outputQuantity'] ?? 1;
        $waste = $card['plannedWastePercent'] ?? 0;
        if (!is_numeric($outputQty) || (float)$outputQty <= 0 || (float)$outputQty > 1000000000) throw new ApiError('Неверное количество выпуска');
        if (!is_numeric($waste) || (float)$waste < 0 || (float)$waste >= 100) throw new ApiError('Плановый брак должен быть от 0 до 99,9999%');
        $unit = trim((string)($card['outputUnit'] ?? 'шт')) ?: 'шт';
        if (mb_strlen($unit) > 16) throw new ApiError('Единица измерения длиннее 16 символов');
        $duplicate = $this->db->one('SELECT id FROM tech_cards WHERE code = ? AND id <> ?', [$code, $id]);
        if ($duplicate) throw new ApiError('Техкарта с таким кодом уже существует');

        return $this->db->tx(function () use ($id, $code, $name, $card, $outputQty, $waste, $unit) {
            $cardId = $id;
            if ($cardId === 0) {
                $cardId = $this->db->insert('tech_cards', [
                    'code' => $code, 'name' => mb_substr($name, 0, 200),
                    'product_name' => mb_substr(trim((string)($card['productName'] ?? '')), 0, 200),
                    'status' => 'draft', 'created_by' => $this->me['id'], 'created_at' => $this->now(), 'updated_at' => $this->now(),
                ]);
            } else {
                $have = $this->db->lock('tech_cards', $cardId);
                if (!$have) throw new ApiError('Техкарта не найдена');
                $this->db->run('UPDATE tech_cards SET code = ?, name = ?, product_name = ?, updated_at = ? WHERE id = ?', [
                    $code, mb_substr($name, 0, 200), mb_substr(trim((string)($card['productName'] ?? '')), 0, 200), $this->now(), $cardId,
                ]);
            }
            $version = $this->db->one("SELECT * FROM tech_card_versions WHERE tech_card_id = ? AND status = 'draft' ORDER BY version_no DESC LIMIT 1", [$cardId]);
            if (!$version) {
                $max = $this->db->one('SELECT COALESCE(MAX(version_no), 0) AS n FROM tech_card_versions WHERE tech_card_id = ?', [$cardId]);
                $versionNo = (int)($max['n'] ?? 0) + 1;
                $versionId = $this->db->insert('tech_card_versions', [
                    'tech_card_id' => $cardId, 'version_no' => $versionNo, 'status' => 'draft',
                    'output_quantity' => (string)$outputQty, 'output_unit' => mb_substr($unit, 0, 16),
                    'planned_waste_percent' => (string)$waste, 'created_by' => $this->me['id'], 'created_at' => $this->now(),
                ]);
            } else {
                $versionId = (int)$version['id'];
                $versionNo = (int)$version['version_no'];
                $this->db->run('UPDATE tech_card_versions SET output_quantity = ?, output_unit = ?, planned_waste_percent = ? WHERE id = ?', [
                    (string)$outputQty, mb_substr($unit, 0, 16), (string)$waste, $versionId,
                ]);
                $this->db->run('DELETE FROM tech_card_operations WHERE version_id = ?', [$versionId]);
            }
            $ops = $this->normalizeTechOperations((array)($card['operations'] ?? []), $code, $versionNo);
            foreach ($ops as $op) $this->db->insert('tech_card_operations', ['version_id' => $versionId] + $op);
            $this->log('techCardSave', "$cardId v$versionNo $code");
            return ['techCard' => $this->techCardVersion($versionId)];
        });
    }

    /** Утвердить черновую версию. Ранее утверждённая версия остаётся в истории. */
    private function aTechCardApprove(array $req): array
    {
        $this->need('assistant', 'designer');
        $versionId = (int)($req['version_id'] ?? 0);
        return $this->db->tx(function () use ($versionId) {
            $version = $this->db->lock('tech_card_versions', $versionId);
            if (!$version) throw new ApiError('Версия техкарты не найдена');
            if ($version['status'] !== 'draft') throw new ApiError('Утвердить можно только черновую версию');
            $snapshot = $this->techCardVersion($versionId);
            if (!$snapshot || !$snapshot['operations']) throw new ApiError('В техкарте нет операций');
            $hashData = $snapshot;
            unset($hashData['snapshotHash'], $hashData['status']);
            $hash = hash('sha256', self::json($hashData));
            $this->db->run("UPDATE tech_card_versions SET status = 'archived' WHERE tech_card_id = ? AND status = 'approved'", [$version['tech_card_id']]);
            $this->db->run("UPDATE tech_card_versions SET status = 'approved', snapshot_hash = ?, approved_by = ?, approved_at = ? WHERE id = ?", [
                $hash, $this->me['id'], $this->now(), $versionId,
            ]);
            $this->db->run("UPDATE tech_cards SET status = 'active', updated_at = ? WHERE id = ?", [$this->now(), $version['tech_card_id']]);
            $this->log('techCardApprove', $version['tech_card_id'] . ' v' . $version['version_no']);
            return ['techCard' => $this->techCardVersion($versionId)];
        });
    }

    /** Назначить заданию утверждённую версию техкарты и сохранить неизменяемый снимок. */
    private function aJobTechCardAssign(array $req): array
    {
        $this->need('assistant', 'manager', 'designer');
        $jobId = (int)($req['job_id'] ?? 0);
        $versionId = (int)($req['version_id'] ?? 0);
        return $this->db->tx(function () use ($jobId, $versionId) {
            $job = $this->db->lock('jobs', $jobId);
            if (!$job || $job['done']) throw new ApiError('Заказ не найден или закрыт');
            if ($this->db->one('SELECT id FROM stages WHERE job_id = ? LIMIT 1', [$jobId])) {
                throw new ApiError('После начала работ техкарту задания менять нельзя');
            }
            $snapshot = $this->techCardVersion($versionId);
            if (!$snapshot || $snapshot['status'] !== 'approved') throw new ApiError('Нужна утверждённая версия техкарты');
            $json = self::json($snapshot);
            $have = $this->db->one('SELECT job_id FROM production_task_specs WHERE job_id = ?', [$jobId]);
            if ($have) {
                $this->db->run('UPDATE production_task_specs SET tech_card_version_id = ?, snapshot_json = ?, assigned_by = ?, assigned_at = ? WHERE job_id = ?', [
                    $versionId, $json, $this->me['id'], $this->now(), $jobId,
                ]);
            } else {
                $this->db->insert('production_task_specs', [
                    'job_id' => $jobId, 'tech_card_version_id' => $versionId, 'snapshot_json' => $json,
                    'assigned_by' => $this->me['id'], 'assigned_at' => $this->now(),
                ]);
            }
            $this->log('jobTechCardAssign', "$jobId v$versionId");
            return ['jobId' => $jobId, 'techCard' => $snapshot];
        });
    }

    private function jobRoute(int $jobId): ?array
    {
        $row = $this->db->one('SELECT snapshot_json FROM production_task_specs WHERE job_id = ?', [$jobId]);
        if (!$row) return null;
        $snapshot = json_decode((string)$row['snapshot_json'], true);
        return is_array($snapshot) && isset($snapshot['operations']) && is_array($snapshot['operations']) ? $snapshot : null;
    }

    /** Доступный объём этапа по снимку маршрута; null — старое задание без техкарты. */
    private function routeAvailable(int $jobId, string $stage, int $jobQuantity): ?int
    {
        $route = $this->jobRoute($jobId);
        if ($route === null) return null;
        $ops = array_values($route['operations']);
        $index = array_search($stage, array_column($ops, 'stage'), true);
        if ($index === false) throw new ApiError('Этап не входит в утверждённый маршрут задания');
        $source = $jobQuantity;
        for ($i = $index - 1; $i >= 0; $i--) {
            if (empty($ops[$i]['optional'])) {
                $source = $this->stageDone($jobId, (string)$ops[$i]['stage']);
                break;
            }
        }
        return max(0, $source - $this->stageDone($jobId, $stage));
    }

    private function aJobs(array $req): array
    {
        $jobs = $this->db->all('SELECT * FROM jobs WHERE done = ? ORDER BY created_at DESC LIMIT 200', [!empty($req['done']) ? 1 : 0]);
        $ids = array_map(fn($j) => (int)$j['id'], $jobs);
        $stages = $ids ? $this->db->all(
            'SELECT s.*, a.name AS started_name, b.name AS finished_name FROM stages s
             LEFT JOIN employees a ON a.id = s.started_by LEFT JOIN employees b ON b.id = s.finished_by
             WHERE s.job_id IN (' . implode(',', $ids) . ') ORDER BY s.id',
        ) : [];
        $byJob = [];
        foreach ($stages as $s) {
            $byJob[(int)$s['job_id']][] = [
                'id' => (int)$s['id'], 'stage' => $s['stage'], 'startedBy' => $s['started_name'], 'startedById' => $s['started_by'] !== null ? (int)$s['started_by'] : null,
                'startedAt' => $s['started_at'] !== null ? (int)$s['started_at'] : null,
                'finishedBy' => $s['finished_name'], 'finishedAt' => $s['finished_at'] !== null ? (int)$s['finished_at'] : null,
                'quantity' => (int)$s['quantity'], 'comment' => $s['comment'],
            ];
        }
        $specs = [];
        if ($ids) foreach ($this->db->all('SELECT job_id, snapshot_json FROM production_task_specs WHERE job_id IN (' . implode(',', $ids) . ')') as $s) {
            $specs[(int)$s['job_id']] = json_decode((string)$s['snapshot_json'], true);
        }
        return ['jobs' => array_map(fn($j) => [
            'id' => (int)$j['id'], 'quoteId' => $j['quote_id'], 'title' => $j['title'], 'client' => $j['client'], 'quantity' => (int)$j['quantity'],
            'deadline' => $j['deadline'] !== null ? (int)$j['deadline'] : null, 'done' => (bool)$j['done'],
            'techCard' => $specs[(int)$j['id']] ?? null, 'stages' => $byJob[(int)$j['id']] ?? [],
        ], $jobs)];
    }

    /**
     * Заказ в производстве: новый или изменение переданных полей (срок и связь с КП не стираются).
     * Закрыть заказ с незавершёнными этапами нельзя; директор может закрыть принудительно (force) — с записью в журнал.
     */
    private function aJobSave(array $req): array
    {
        $this->need('assistant', 'manager');
        $j = (array)($req['job'] ?? []);
        $id = (int)($j['id'] ?? 0);
        $row = [];
        if (array_key_exists('title', $j) || $id === 0) {
            $title = trim((string)($j['title'] ?? ''));
            if ($title === '') throw new ApiError('Нужно название заказа');
            $row['title'] = mb_substr($title, 0, 200);
        }
        if (array_key_exists('quoteId', $j)) $row['quote_id'] = mb_substr((string)$j['quoteId'], 0, 64);
        if (array_key_exists('client', $j)) $row['client'] = mb_substr((string)$j['client'], 0, 200);
        if (array_key_exists('quantity', $j)) $row['quantity'] = max(0, min(10_000_000, (int)$j['quantity']));
        if (array_key_exists('deadline', $j)) $row['deadline'] = !empty($j['deadline']) ? (int)$j['deadline'] : null;
        if (array_key_exists('done', $j)) $row['done'] = !empty($j['done']) ? 1 : 0;
        if ($id === 0) {
            $id = $this->db->insert('jobs', $row + ['quote_id' => '', 'client' => '', 'quantity' => 0, 'done' => 0, 'created_by' => $this->me['id'], 'created_at' => $this->now()]);
            $this->log('jobAdd', "$id {$row['title']}");
            return ['id' => $id];
        }
        return $this->db->tx(function () use ($id, $row, $req) {
            $have = $this->db->lock('jobs', $id);
            if (!$have) throw new ApiError('Заказ не найден');
            if (($row['done'] ?? 0) === 1 && !$have['done']) {
                $open = $this->db->one('SELECT COUNT(*) AS n FROM stages WHERE job_id = ? AND finished_at IS NULL', [$id]);
                if ((int)$open['n'] > 0) {
                    if (!(!empty($req['force']) && $this->me['role'] === 'director')) throw new ApiError('Есть незавершённые этапы: ' . $open['n']);
                    $this->log('jobForceClose', "$id: открытых этапов {$open['n']}");
                }
            }
            if ($row) {
                $sets = implode(', ', array_map(fn($c) => "$c = ?", array_keys($row)));
                $this->db->run("UPDATE jobs SET $sets WHERE id = ?", [...array_values($row), $id]);
            }
            $changes = [];
            foreach ($row as $c => $v) if ((string)$v !== (string)$have[$c]) $changes[] = "$c: {$have[$c]} → $v";
            $this->log('jobSave', "$id " . implode('; ', $changes));
            return ['id' => $id];
        });
    }

    /** Сколько штук по этапу уже сделано. */
    private function stageDone(int $jobId, string $stage): int
    {
        return (int)($this->db->one('SELECT COALESCE(SUM(quantity), 0) AS n FROM stages WHERE job_id = ? AND stage = ? AND finished_at IS NOT NULL', [$jobId, $stage])['n'] ?? 0);
    }

    /** «Начать» этап: только своя роль (директор и помощник — любой); отмечается кто и когда. */
    private function aStageStart(array $req): array
    {
        $stage = (string)($req['stage'] ?? '');
        $jobId = (int)($req['job_id'] ?? 0);
        if (!isset(Rules::STAGES[$stage])) throw new ApiError('Неизвестный этап');
        if (!Rules::canStage($this->me['role'], $stage, $this->stageRoles())) throw new ApiError('Этот этап ведёт другой участок');
        return $this->db->tx(function () use ($stage, $jobId) {
            $job = $this->db->lock('jobs', $jobId);
            if (!$job || $job['done']) throw new ApiError('Заказ не найден или закрыт');
            $available = $this->routeAvailable($jobId, $stage, (int)$job['quantity']);
            if ($available !== null && $available <= 0) throw new ApiError('Нет доступного объёма после предыдущего обязательного этапа');
            if ($this->db->one('SELECT id FROM stages WHERE job_id = ? AND stage = ? AND finished_at IS NULL', [$jobId, $stage])) {
                throw new ApiError('Этап уже идёт');
            }
            if ((int)$job['quantity'] > 0 && $this->stageDone($jobId, $stage) >= (int)$job['quantity']) throw new ApiError('Этап уже выполнен полностью');
            $id = $this->db->insert('stages', ['job_id' => $jobId, 'stage' => $stage, 'started_by' => $this->me['id'], 'started_at' => $this->now()]);
            $this->log('stageStart', "$jobId $stage");
            return ['id' => $id];
        });
    }

    /**
     * «Завершить» этап: завершает тот, кто начал (или директор/помощник). Количество обязательно:
     * не больше остатка по заказу; ноль — только с комментарием. Время работы = конец − начало.
     */
    private function aStageFinish(array $req): array
    {
        if (!isset($req['quantity']) || !is_numeric($req['quantity'])) throw new ApiError('Укажите количество');
        $qty = (int)$req['quantity'];
        $comment = trim(mb_substr((string)($req['comment'] ?? ''), 0, 500));
        if ($qty < 0) throw new ApiError('Количество не может быть отрицательным');
        if ($qty === 0 && $comment === '') throw new ApiError('Ноль штук — напишите причину в комментарии');
        return $this->db->tx(function () use ($req, $qty, $comment) {
            $s = $this->db->one('SELECT * FROM stages WHERE id = ?', [(int)($req['id'] ?? 0)]);
            if (!$s) throw new ApiError('Этап не найден');
            $job = $this->db->lock('jobs', (int)$s['job_id']);
            $s = $this->db->one('SELECT * FROM stages WHERE id = ?', [(int)$s['id']]);
            if ($s['finished_at'] !== null) throw new ApiError('Этап уже завершён');
            if (!$job || $job['done']) throw new ApiError('Заказ закрыт');
            if ((int)$s['started_by'] !== (int)$this->me['id'] && !Rules::full($this->me['role'])) throw new ApiError('Этап начал другой сотрудник');
            $routeLeft = $this->routeAvailable((int)$job['id'], (string)$s['stage'], (int)$job['quantity']);
            $left = $routeLeft ?? ((int)$job['quantity'] - $this->stageDone((int)$job['id'], $s['stage']));
            if ((int)$job['quantity'] > 0 && $qty > $left) throw new ApiError("Больше, чем осталось по заказу: $left шт.");
            $n = $this->db->run('UPDATE stages SET finished_by = ?, finished_at = ?, quantity = ?, comment = ? WHERE id = ? AND finished_at IS NULL', [
                $this->me['id'], $this->now(), $qty, $comment, $s['id'],
            ]);
            if ($n !== 1) throw new ApiError('Этап уже завершён');
            $this->log('stageFinish', $s['job_id'] . ' ' . $s['stage'] . " $qty шт.");
            return ['minutes' => intdiv($this->now() - (int)$s['started_at'], 60000)];
        });
    }

    // ---------------------------------------------------------------- Активность в приложении

    /** Сейчас рабочее время по графику (с запасом 30 минут до и после) или открыта смена. */
    private function inWorkTime(array $emp, \DateTimeImmutable $now): bool
    {
        $open = $this->db->one('SELECT id FROM shifts WHERE employee_id = ? AND end_ms IS NULL AND start_ms > ?', [$emp['id'], $this->now() - 16 * 3600000]);
        if ($open) return true;
        $sched = Rules::schedule($emp['schedule']);
        foreach ([$now, $now->modify('-1 day')] as $day) {
            $ymd = $day->format('Y-m-d');
            if (!Rules::worksOn($sched, $ymd)) continue;
            $start = (new \DateTimeImmutable($ymd . ' ' . $sched['start'], $now->getTimezone()))->getTimestamp() * 1000;
            $end = $start + Rules::shiftMinutes($sched) * 60000;
            if ($this->now() >= $start - 1800000 && $this->now() <= $end + 1800000) return true;
        }
        return false;
    }

    /**
     * Отметка «приложение открыто» — раз в минуту, пока приложение на экране.
     * Считается только в рабочее время; перерыв больше 2 минут — новый заход.
     */
    private function aPing(array $req): array
    {
        if (!in_array($this->me['role'], Rules::OFFICE, true)) return ['tracked' => false];
        $tz = new \DateTimeZone('Europe/Moscow');
        $now = (new \DateTimeImmutable('@' . intdiv($this->now(), 1000)))->setTimezone($tz);
        if (!$this->inWorkTime($this->me, $now)) return ['tracked' => false];
        $day = $now->format('Y-m-d');
        // Какой экран открыт — статистика экранов (минута на отметку).
        $screen = preg_replace('/[^a-z_]/', '', strtolower((string)($req['screen'] ?? '')));
        if ($screen !== '') {
            $n = $this->db->run('UPDATE screen_stats SET minutes = minutes + 1 WHERE employee_id = ? AND day = ? AND screen = ?', [$this->me['id'], $day, mb_substr($screen, 0, 32)]);
            if ($n === 0) $this->db->insert('screen_stats', ['employee_id' => $this->me['id'], 'day' => $day, 'screen' => mb_substr($screen, 0, 32), 'minutes' => 1]);
        }
        $this->db->tx(function () use ($day, $now, $tz) {
            $row = $this->db->one('SELECT * FROM activity WHERE employee_id = ? AND day = ?' . ($this->db->sqlite ? '' : ' FOR UPDATE'), [$this->me['id'], $day]);
            if (!$row) {
                // Перерыв с начала смены до первого захода тоже считается.
                $sched = Rules::schedule($this->me['schedule']);
                $start = Rules::worksOn($sched, $day) ? (new \DateTimeImmutable($day . ' ' . $sched['start'], $tz))->getTimestamp() * 1000 : $this->now();
                $this->db->insert('activity', [
                    'employee_id' => $this->me['id'], 'day' => $day, 'minutes' => 1, 'sessions' => 1,
                    'first_ms' => $this->now(), 'last_ms' => $this->now(), 'max_gap_ms' => max(0, $this->now() - $start),
                ]);
                return;
            }
            $gap = $this->now() - (int)$row['last_ms'];
            if ($gap < 30000) return; // чаще раза в полминуты не считаем
            $continued = $gap <= 120000;
            $this->db->run('UPDATE activity SET minutes = ?, sessions = ?, last_ms = ?, max_gap_ms = ? WHERE employee_id = ? AND day = ?', [
                (int)$row['minutes'] + ($continued ? max(1, (int)round($gap / 60000)) : 1),
                (int)$row['sessions'] + ($continued ? 0 : 1),
                $this->now(), max((int)$row['max_gap_ms'], $continued ? 0 : $gap), $this->me['id'], $day,
            ]);
        });
        return ['tracked' => true];
    }

    /** Действие в приложении: создал КП, работал с клиентом, правил товар, отгрузил… */
    private function aEvent(array $req): array
    {
        $kind = (string)($req['kind'] ?? '');
        if (!in_array($kind, self::EVENT_KINDS, true)) throw new ApiError('Неизвестное действие');
        $this->db->insert('events', [
            'employee_id' => $this->me['id'], 'kind' => $kind, 'detail' => mb_substr(trim((string)($req['detail'] ?? '')), 0, 200), 'created_at' => $this->now(),
        ]);
        return [];
    }

    /**
     * Активность офиса за период (1, 7 или 30 дней): минуты, заходы, первое и последнее действие,
     * самый долгий перерыв, счётчики действий; «нет в приложении» — больше порога в рабочее время сейчас.
     */
    private function aActivity(array $req): array
    {
        $this->need('assistant');
        $days = in_array((int)($req['days'] ?? 1), [1, 7, 30], true) ? (int)$req['days'] : 1;
        $tz = new \DateTimeZone('Europe/Moscow');
        $now = (new \DateTimeImmutable('@' . intdiv($this->now(), 1000)))->setTimezone($tz);
        $fromDay = $now->modify('-' . ($days - 1) . ' days')->format('Y-m-d');
        $fromMs = (new \DateTimeImmutable($fromDay . ' 00:00', $tz))->getTimestamp() * 1000;
        $idle = (int)$this->db->setting('idle_minutes', '30') * 60000;
        $roles = Rules::OFFICE;
        $people = $this->db->all('SELECT * FROM employees WHERE active = 1 AND role IN (' . implode(',', array_fill(0, count($roles), '?')) . ') ORDER BY name', $roles);
        $act = [];
        foreach ($this->db->all('SELECT * FROM activity WHERE day >= ?', [$fromDay]) as $a) $act[(int)$a['employee_id']][] = $a;
        $ev = [];
        foreach ($this->db->all('SELECT employee_id, kind, COUNT(*) AS n FROM events WHERE created_at >= ? GROUP BY employee_id, kind', [$fromMs]) as $e) {
            $ev[(int)$e['employee_id']][$e['kind']] = (int)$e['n'];
        }
        $today = $now->format('Y-m-d');
        $rows = [];
        foreach ($people as $p) {
            $list = $act[(int)$p['id']] ?? [];
            $todayRow = current(array_filter($list, fn($a) => $a['day'] === $today)) ?: null;
            $working = $this->inWorkTime($p, $now);
            $lastMs = $todayRow ? (int)$todayRow['last_ms'] : null;
            $rows[] = [
                'id' => (int)$p['id'], 'name' => $p['name'], 'role' => $p['role'],
                'minutes' => array_sum(array_map(fn($a) => (int)$a['minutes'], $list)),
                'sessions' => array_sum(array_map(fn($a) => (int)$a['sessions'], $list)),
                'activeDays' => count($list),
                'first' => $todayRow ? (int)$todayRow['first_ms'] : null, 'last' => $lastMs,
                'maxGapMinutes' => intdiv(max(array_map(fn($a) => (int)$a['max_gap_ms'], $list ?: [['max_gap_ms' => 0]])), 60000),
                'actions' => $ev[(int)$p['id']] ?? [],
                'working' => $working,
                'idle' => $working && ($lastMs === null || $this->now() - $lastMs > $idle),
            ];
        }
        usort($rows, fn($a, $b) => $b['minutes'] <=> $a['minutes']);
        $feed = $this->db->all('SELECT v.kind, v.detail, v.created_at, e.name FROM events v JOIN employees e ON e.id = v.employee_id
            WHERE v.created_at >= ? ORDER BY v.id DESC LIMIT 200', [$fromMs]);
        $screens = $this->db->all('SELECT screen, SUM(minutes) AS m FROM screen_stats WHERE day >= ? GROUP BY screen ORDER BY m DESC LIMIT 20', [$fromDay]);
        return [
            'screens' => array_map(fn($x) => ['screen' => $x['screen'], 'minutes' => (int)$x['m']], $screens),
            'people' => $rows, 'idleMinutes' => intdiv($idle, 60000),
            'feed' => array_map(fn($f) => ['who' => $f['name'], 'kind' => $f['kind'], 'detail' => $f['detail'], 'time' => (int)$f['created_at']], $feed),
        ];
    }

    // ---------------------------------------------------------------- Рейтинг и журнал

    private function aRating(array $req): array
    {
        [$from, $to] = $this->month((string)($req['month'] ?? ''));
        $people = array_map(fn($e) => ['id' => (int)$e['id'], 'name' => $e['name'], 'role' => $e['role'], 'schedule' => Rules::schedule($e['schedule'])],
            $this->db->all("SELECT * FROM employees WHERE active = 1 AND role <> 'director'"));
        // Подведённые часы не помогают: при расхождении с сервером берётся время сервера.
        $shifts = array_map(fn($s) => [
            'employee_id' => (int)$s['employee_id'],
            'start_ms' => $s['fixed_by'] === null && self::drift($s['start_ms'], $s['server_start']) ? (int)$s['server_start'] : (int)$s['start_ms'],
        ], $this->db->all('SELECT employee_id, start_ms, server_start, fixed_by FROM shifts WHERE start_ms >= ? - 86400000 AND start_ms < ?', [$from, $to]));
        $stages = array_map(fn($s) => [
            'finished_by' => (int)$s['finished_by'], 'stage' => $s['stage'], 'quantity' => (int)$s['quantity'],
            'job_quantity' => (int)$s['job_quantity'], 'job_id' => (int)$s['job_id'],
        ], $this->db->all('SELECT s.finished_by, s.stage, s.quantity, s.job_id, j.quantity AS job_quantity FROM stages s JOIN jobs j ON j.id = s.job_id
            WHERE s.finished_at >= ? AND s.finished_at < ?', [$from, $to]));
        $tz = new \DateTimeZone('Europe/Moscow');
        $fromDay = (new \DateTimeImmutable('@' . intdiv($from, 1000)))->setTimezone($tz)->format('Y-m-d');
        $toDay = (new \DateTimeImmutable('@' . intdiv($to, 1000)))->setTimezone($tz)->format('Y-m-d');
        $minutes = [];
        foreach ($this->db->all('SELECT employee_id, SUM(minutes) AS m FROM activity WHERE day >= ? AND day < ? GROUP BY employee_id', [$fromDay, $toDay]) as $a) {
            $minutes[(int)$a['employee_id']] = (int)$a['m'];
        }
        $rows = Rules::rating($people, $shifts, $stages, $from, $to, $this->now(), (int)$this->db->setting('late_minutes', '10'), $tz, $minutes);
        // Задачи в срок: 10 % рейтинга у тех, у кого были задачи со сроком в этом месяце.
        $tasks = [];
        foreach ($this->db->all("SELECT assignee_id, due_ms, done_at, status FROM tasks WHERE due_ms >= ? AND due_ms < ? AND status <> 'cancelled'", [$from, min($to, $this->now())]) as $t) {
            $k = (int)$t['assignee_id'];
            $tasks[$k]['all'] = ($tasks[$k]['all'] ?? 0) + 1;
            if ($t['done_at'] !== null && (int)$t['done_at'] <= (int)$t['due_ms']) $tasks[$k]['ok'] = ($tasks[$k]['ok'] ?? 0) + 1;
        }
        foreach ($rows as &$r) {
            $t = $tasks[$r['id']] ?? null;
            $r['tasksOnTime'] = $t ? (int)round(100 * ($t['ok'] ?? 0) / $t['all']) : null;
            if ($t) $r['score'] = (int)round(0.9 * $r['score'] + 0.1 * $r['tasksOnTime']);
        }
        unset($r);
        usort($rows, fn($a, $b) => $b['score'] <=> $a['score'] ?: strcmp($a['name'], $b['name']));
        // Сотрудник видит рейтинг всех (мотивация), но подробности — только свои.
        if (!Rules::full($this->me['role'])) {
            $rows = array_map(fn($r) => $r['id'] === (int)$this->me['id'] ? $r : ['id' => $r['id'], 'name' => $r['name'], 'role' => $r['role'], 'score' => $r['score']], $rows);
        }
        return ['rating' => $rows];
    }

    private function aAudit(array $req): array
    {
        $this->need('assistant');
        $from = (int)($req['from'] ?? ($this->now() - 7 * 86400000));
        $rows = $this->db->all('SELECT a.*, e.name FROM audit a LEFT JOIN employees e ON e.id = a.employee_id WHERE a.created_at >= ? ORDER BY a.id DESC LIMIT 500', [$from]);
        return ['audit' => array_map(fn($a) => [
            'time' => (int)$a['created_at'], 'who' => $a['name'] ?? '—', 'action' => $a['action'], 'detail' => $a['detail'], 'ip' => $a['ip'],
        ], $rows)];
    }
}
