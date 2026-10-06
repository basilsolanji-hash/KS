<?php
declare(strict_types=1);

namespace Ks;

/** Сотрудники, МойСклад, смены — часть Api (файлы до 25 КБ: так их надёжно принимает FTP beget). */
trait ApiPeople
{
    // ---------------------------------------------------------------- Сотрудники

    private function employeeRow(array $e, bool $private): array
    {
        $row = [
            'id' => (int)$e['id'], 'name' => $e['name'], 'role' => $e['role'], 'position' => $e['position'] ?? '',
            'active' => (bool)$e['active'],
        ];
        if ($private) {
            $row['ms_id'] = $e['ms_id'];
            $row['schedule'] = Rules::schedule($e['schedule']);
            $row['hasKey'] = !empty($e['key_hash']);
            $row['phone'] = $this->crypto->decrypt($e['phone_enc']);
            $row['email'] = $this->crypto->decrypt($e['email_enc']);
        }
        return $row;
    }

    private function aEmployees(array $req): array
    {
        $full = Rules::full($this->me['role']) || $this->me['role'] === 'accountant';
        $rows = $this->db->all('SELECT * FROM employees ORDER BY active DESC, name');
        // Всем — имена и роли (для чата и задач); телефоны, графики, ключи — директору, помощнику, бухгалтеру.
        return ['employees' => array_map(fn($e) => $this->employeeRow($e, $full), $full ? $rows : array_values(array_filter($rows, fn($e) => $e['active'])))];
    }

    /**
     * Новый или изменённый сотрудник; новому выдаётся ключ входа (показывается один раз).
     * При изменении меняются только переданные поля (телефон не стирается, если его не прислали).
     */
    private function aEmployeeSave(array $req): array
    {
        $this->need();
        $e = (array)($req['employee'] ?? []);
        $id = (int)($e['id'] ?? 0);
        $have = $id > 0 ? $this->db->one('SELECT * FROM employees WHERE id = ?', [$id]) : null;
        if ($id > 0 && !$have) throw new ApiError('Сотрудник не найден');
        $row = [];
        if (array_key_exists('name', $e) || !$have) {
            $name = trim((string)($e['name'] ?? ''));
            if ($name === '') throw new ApiError('Нужно имя');
            $row['name'] = mb_substr($name, 0, 120);
        }
        if (array_key_exists('role', $e) || !$have) {
            $role = (string)($e['role'] ?? 'manager');
            if (!isset(Rules::ROLES[$role])) throw new ApiError('Неизвестная роль');
            $row['role'] = $role;
        }
        if (array_key_exists('position', $e)) $row['position'] = mb_substr((string)$e['position'], 0, 120);
        if (array_key_exists('phone', $e)) $row['phone_enc'] = $this->crypto->encrypt(trim((string)$e['phone']));
        if (array_key_exists('email', $e)) $row['email_enc'] = $this->crypto->encrypt(trim((string)$e['email']));
        if (array_key_exists('schedule', $e) || !$have) $row['schedule'] = self::json(Rules::schedule(self::json($e['schedule'] ?? null)));
        if (array_key_exists('active', $e) || !$have) $row['active'] = !isset($e['active']) || !empty($e['active']) ? 1 : 0;
        if (array_key_exists('ms_id', $e)) {
            $ms = trim((string)$e['ms_id']);
            if ($ms !== '' && !preg_match('/^[\w-]{8,64}$/', $ms)) throw new ApiError('Неверный сотрудник МойСклад');
            $other = $ms === '' ? null : $this->db->one('SELECT id FROM employees WHERE ms_id = ? AND id <> ?', [$ms, $id]);
            if ($other) throw new ApiError('Этот сотрудник МойСклад уже привязан');
            $row['ms_id'] = $ms === '' ? null : $ms;
        }
        $key = null;
        if ($have) {
            if ($id === (int)$this->me['id'] && (($row['role'] ?? 'director') !== 'director' || (isset($row['active']) && !$row['active']))) {
                throw new ApiError('Нельзя снять права с себя');
            }
            if ($row) {
                $sets = implode(', ', array_map(fn($c) => "$c = ?", array_keys($row)));
                $this->db->run("UPDATE employees SET $sets WHERE id = ?", [...array_values($row), $id]);
            }
            if (isset($row['active']) && !$row['active']) $this->db->run('DELETE FROM sessions WHERE employee_id = ?', [$id]);
            $changes = [];
            foreach (['name', 'role', 'position', 'schedule', 'active', 'ms_id'] as $c) {
                if (array_key_exists($c, $row) && (string)$row[$c] !== (string)$have[$c]) $changes[] = "$c: {$have[$c]} → {$row[$c]}";
            }
            if (isset($row['phone_enc']) || isset($row['email_enc'])) $changes[] = 'контакты';
            $this->log('employeeSave', "$id " . implode('; ', $changes));
        } else {
            $key = Crypto::newKey();
            $id = $this->db->insert('employees', $row + ['key_hash' => $this->crypto->hash($key), 'created_at' => $this->now()]);
            $this->log('employeeAdd', "$id {$row['name']} ({$row['role']})");
        }
        return ['employee' => $this->employeeRow($this->db->one('SELECT * FROM employees WHERE id = ?', [$id]), true), 'key' => $key];
    }

    /** Новый ключ входа: старый и все входы сотрудника перестают работать. */
    private function aEmployeeKey(array $req): array
    {
        $this->need();
        $id = (int)($req['id'] ?? 0);
        if (!$this->db->one('SELECT id FROM employees WHERE id = ?', [$id])) throw new ApiError('Сотрудник не найден');
        $key = Crypto::newKey();
        $this->db->run('UPDATE employees SET key_hash = ? WHERE id = ?', [$this->crypto->hash($key), $id]);
        $this->db->run('DELETE FROM sessions WHERE employee_id = ?', [$id]);
        $this->log('employeeKey', (string)$id);
        return ['key' => $key];
    }

    // ---------------------------------------------------------------- МойСклад

    private function msHttp(string $method, string $path, ?array $body): array
    {
        $token = (string)($this->config['ms_token'] ?? '');
        if ($token === '') throw new ApiError('МойСклад не подключён на сервере');
        $ch = curl_init('https://api.moysklad.ru/api/remap/1.2' . $path);
        curl_setopt_array($ch, [
            CURLOPT_CUSTOMREQUEST => $method, CURLOPT_RETURNTRANSFER => true, CURLOPT_TIMEOUT => 25, CURLOPT_ENCODING => 'gzip',
            CURLOPT_HTTPHEADER => ['Authorization: Bearer ' . $token, 'Accept: application/json;charset=utf-8', 'Content-Type: application/json'],
        ]);
        if ($body !== null) curl_setopt($ch, CURLOPT_POSTFIELDS, self::json($body));
        $text = (string)curl_exec($ch);
        $code = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);
        $data = json_decode($text, true) ?: [];
        if ($code >= 400 || $code === 0) throw new ApiError('МойСклад: ' . ($data['errors'][0]['error'] ?? "ошибка $code"));
        return $data;
    }

    /**
     * Сотрудники МойСклад: новые добавляются без доступа (роль и ключ выдаёт директор).
     * Связь — по id МойСклад, для добавленных вручную — по совпадению ФИО. Пустые поля МойСклад не стирают данные.
     * Уволенные (архив) в МойСклад — отключаются и здесь.
     */
    private function aMsEmployeesImport(array $req): array
    {
        $this->need('assistant');
        $rows = [];
        for ($offset = 0; $offset < 10000; $offset += 1000) {
            $page = (($this->ms)('GET', '/entity/employee?limit=1000&offset=' . $offset, null))['rows'] ?? [];
            $rows = [...$rows, ...$page];
            if (count($page) < 1000) break;
        }
        $added = 0;
        $updated = 0;
        $archived = 0;
        foreach ($rows as $r) {
            if (!preg_match('/^[\w-]{8,64}$/', (string)($r['id'] ?? ''))) continue;
            $name = mb_substr(trim(implode(' ', array_filter([$r['lastName'] ?? '', $r['firstName'] ?? '', $r['middleName'] ?? ''])) ?: ($r['name'] ?? '')), 0, 120);
            $have = $this->db->one('SELECT * FROM employees WHERE ms_id = ?', [$r['id']])
                ?? ($name !== '' ? $this->db->one('SELECT * FROM employees WHERE ms_id IS NULL AND name = ?', [$name]) : null);
            if (!empty($r['archived'])) {
                if ($have && $have['active'] && $have['role'] !== 'director') {
                    $this->db->run('UPDATE employees SET active = 0 WHERE id = ?', [$have['id']]);
                    $this->db->run('DELETE FROM sessions WHERE employee_id = ?', [$have['id']]);
                    $archived++;
                }
                continue;
            }
            $fields = ['ms_id' => $r['id']];
            if ($name !== '') $fields['name'] = $name;
            if (trim((string)($r['position'] ?? '')) !== '') $fields['position'] = mb_substr((string)$r['position'], 0, 120);
            if (trim((string)($r['phone'] ?? '')) !== '') $fields['phone_enc'] = $this->crypto->encrypt((string)$r['phone']);
            if (trim((string)($r['email'] ?? '')) !== '') $fields['email_enc'] = $this->crypto->encrypt((string)$r['email']);
            if ($have) {
                $sets = implode(', ', array_map(fn($c) => "$c = ?", array_keys($fields)));
                $this->db->run("UPDATE employees SET $sets WHERE id = ?", [...array_values($fields), $have['id']]);
                $updated++;
            } elseif ($name !== '') {
                $this->db->insert('employees', $fields + [
                    'name' => $name, 'role' => 'manager', 'schedule' => self::json(Rules::DEFAULT_SCHEDULE), 'active' => 0, 'created_at' => $this->now(),
                ]);
                $added++;
            }
        }
        $this->log('msEmployeesImport', "+$added, обновлено $updated, отключено $archived");
        return ['added' => $added, 'updated' => $updated, 'archived' => $archived];
    }

    /** Карточка сотрудника в МойСклад: ФИО, должность, телефон, e-mail, описание. */
    private function aMsEmployeeSave(array $req): array
    {
        $this->need();
        $msId = (string)($req['ms_id'] ?? '');
        if (!preg_match('/^[\w-]{8,64}$/', $msId)) throw new ApiError('Неверный сотрудник');
        $c = (array)($req['card'] ?? []);
        $body = [];
        foreach (['lastName', 'firstName', 'middleName', 'position', 'phone', 'email', 'description', 'inn'] as $k) {
            if (array_key_exists($k, $c)) $body[$k] = mb_substr(trim((string)$c[$k]), 0, 255);
        }
        if (!$body) throw new ApiError('Нет изменений');
        ($this->ms)('PUT', '/entity/employee/' . $msId, $body);
        $this->log('msEmployeeSave', $msId . ' ' . implode(',', array_keys($body)));
        return ['saved' => array_keys($body)];
    }

    /** Журнал МойСклад: кто что менял прямо в МойСклад (товары, заказы, контрагенты…), за N дней. */
    private function aMsAudit(array $req): array
    {
        $this->need('assistant');
        $days = max(1, min(31, (int)($req['days'] ?? 1)));
        $from = (new \DateTimeImmutable('@' . intdiv($this->now() - $days * 86400000, 1000)))->setTimezone(new \DateTimeZone('Europe/Moscow'))->format('Y-m-d H:i:s');
        $data = ($this->ms)('GET', '/audit?limit=100&filter=' . rawurlencode('moment>' . $from), null);
        return ['events' => array_map(fn($r) => [
            'who' => (string)($r['uid'] ?? ''), 'moment' => (string)($r['moment'] ?? ''), 'event' => (string)($r['eventType'] ?? ''),
            'entity' => (string)($r['entityType'] ?? ''), 'count' => (int)($r['objectCount'] ?? 1), 'source' => (string)($r['source'] ?? ''),
            'info' => mb_substr((string)($r['info'] ?? ''), 0, 200),
        ], (array)($data['rows'] ?? []))];
    }

    // ---------------------------------------------------------------- Смены

    /**
     * Смена: начало ставится один раз; конец — один раз (закрытую смену сотрудник не меняет).
     * Править начало и закрытую смену может только директор (с записью «было → стало» в журнал).
     */
    private function aShiftSave(array $req): array
    {
        $s = (array)($req['shift'] ?? []);
        $id = (string)($s['id'] ?? '');
        if (!preg_match('/^[\w-]{6,64}$/', $id)) throw new ApiError('Неверная смена');
        $start = (int)($s['start'] ?? 0);
        $end = isset($s['end']) && $s['end'] !== null ? (int)$s['end'] : null;
        if ($start <= 0 || ($end !== null && $end < $start)) throw new ApiError('Неверное время смены');
        if ($end !== null && $end - $start > 24 * 3600000) throw new ApiError('Смена длиннее суток');
        $director = $this->me['role'] === 'director';
        return $this->db->tx(function () use ($id, $start, $end, $director) {
            $have = $this->db->one('SELECT * FROM shifts WHERE id = ?' . ($this->db->sqlite ? '' : ' FOR UPDATE'), [$id]);
            $mine = !$have || (int)$have['employee_id'] === (int)$this->me['id'];
            if (!$mine && !$director) throw new ApiError('Это смена другого сотрудника');
            if (!$have) {
                $this->db->insert('shifts', [
                    'id' => $id, 'employee_id' => $this->me['id'], 'start_ms' => $start, 'end_ms' => $end,
                    'server_start' => $this->now(), 'server_end' => $end !== null ? $this->now() : null,
                ]);
                $this->log('shiftStart', $id);
                return ['id' => $id];
            }
            if ($director && !$mine || $director && $have['end_ms'] !== null && (int)$have['end_ms'] !== $end) {
                $this->db->run('UPDATE shifts SET start_ms = ?, end_ms = ?, fixed_by = ? WHERE id = ?', [$start, $end, $this->me['id'], $id]);
                $this->log('shiftFix', "$id: {$have['start_ms']}–{$have['end_ms']} → {$start}–{$end}");
                return ['id' => $id];
            }
            if ((int)$have['start_ms'] !== $start && !$director) throw new ApiError('Начало смены уже отмечено — исправит директор');
            if ($have['end_ms'] !== null) {
                if ($end === null || (int)$have['end_ms'] === $end) return ['id' => $id];
                throw new ApiError('Смена закрыта — исправит директор');
            }
            if ($end !== null) {
                $this->db->run('UPDATE shifts SET end_ms = ?, server_end = ? WHERE id = ?', [$end, $this->now(), $id]);
                $this->log('shiftEnd', $id);
            }
            return ['id' => $id];
        });
    }

    /** Подведённые часы: время телефона отличается от времени сервера больше чем на 15 минут. */
    private static function drift(mixed $phone, mixed $server): bool
    {
        return $phone !== null && $server !== null && abs((int)$phone - (int)$server) > 15 * 60000;
    }

    private function aShifts(array $req): array
    {
        [$from, $to] = $this->month((string)($req['month'] ?? ''));
        $all = Rules::full($this->me['role']) || $this->me['role'] === 'accountant';
        $sql = 'SELECT s.*, e.name FROM shifts s JOIN employees e ON e.id = s.employee_id WHERE s.start_ms >= ? AND s.start_ms < ?';
        $args = [$from, $to];
        if (!$all) {
            $sql .= ' AND s.employee_id = ?';
            $args[] = $this->me['id'];
        }
        return ['shifts' => array_map(fn($s) => [
            'id' => $s['id'], 'employeeId' => (int)$s['employee_id'], 'person' => $s['name'], 'start' => (int)$s['start_ms'],
            'end' => $s['end_ms'] !== null ? (int)$s['end_ms'] : null, 'fixed' => $s['fixed_by'] !== null,
            'suspicious' => $s['fixed_by'] === null && (self::drift($s['start_ms'], $s['server_start']) || self::drift($s['end_ms'], $s['server_end'])),
        ], $this->db->all($sql . ' ORDER BY s.start_ms', $args))];
    }

    /** Месяц «2026-10» → [начало, конец) в мс по Москве. */
    private function month(string $m): array
    {
        $tz = new \DateTimeZone('Europe/Moscow');
        if (!preg_match('/^\d{4}-(0[1-9]|1[0-2])$/', $m)) $m = (new \DateTimeImmutable('@' . intdiv($this->now(), 1000)))->setTimezone($tz)->format('Y-m');
        $start = new \DateTimeImmutable($m . '-01 00:00', $tz);
        return [$start->getTimestamp() * 1000, $start->modify('+1 month')->getTimestamp() * 1000];
    }
}
