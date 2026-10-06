<?php
declare(strict_types=1);

namespace Ks;

/**
 * API приложения «ФАБРИКА»: один адрес, запрос — JSON {action, token, …}, ответ — {ok, …} или {ok:false, error}.
 * Доступ: ключ входа сотрудника → токен телефона. Не из сети фабрики (allowed_ips) работает только директор.
 * Каждое изменение пишется в журнал (audit).
 */
final class Api
{
    private ?array $me = null;
    /** @var callable(string,string,?array):array */
    private $ms;
    /** @var callable():int */
    private $clock;

    public function __construct(
        private Db $db,
        private Crypto $crypto,
        private array $config,
        private string $ip,
        ?callable $ms = null,
        ?callable $clock = null,
    ) {
        $this->ms = $ms ?? fn(string $method, string $path, ?array $body) => $this->msHttp($method, $path, $body);
        $this->clock = $clock ?? fn() => (int)floor(microtime(true) * 1000);
    }

    private function now(): int
    {
        return ($this->clock)();
    }

    public function handle(array $req): array
    {
        try {
            $action = (string)($req['action'] ?? '');
            if (!preg_match('/^[a-zA-Z]{2,40}$/', $action)) throw new ApiError('Неизвестное действие');
            if ($action === 'setup') return ['ok' => true] + $this->setup($req);
            if ($action === 'login') return ['ok' => true] + $this->login($req);
            $this->auth((string)($req['token'] ?? ''));
            $method = 'a' . ucfirst($action);
            if (!method_exists($this, $method)) throw new ApiError('Неизвестное действие');
            return ['ok' => true] + $this->$method($req);
        } catch (ApiError $e) {
            return ['ok' => false, 'error' => $e->getMessage()];
        } catch (\Throwable $e) {
            error_log('ks api: ' . $e->getMessage());
            return ['ok' => false, 'error' => 'Ошибка сервера'];
        }
    }

    // ---------------------------------------------------------------- Вход

    /** Первый запуск: директор создаётся ключом установки из config (один раз). */
    private function setup(array $req): array
    {
        $setupKey = trim((string)($this->config['setup_key'] ?? ''));
        if ($setupKey === '' || !hash_equals($setupKey, trim((string)($req['setup_key'] ?? '')))) {
            $this->fail();
            throw new ApiError('Неверный ключ установки');
        }
        if ($this->db->one('SELECT id FROM employees LIMIT 1')) throw new ApiError('Директор уже создан');
        $name = trim((string)($req['name'] ?? 'Директор')) ?: 'Директор';
        $id = $this->db->insert('employees', [
            'name' => mb_substr($name, 0, 120), 'role' => 'director', 'position' => 'Директор',
            'schedule' => json_encode(Rules::DEFAULT_SCHEDULE), 'active' => 1, 'created_at' => $this->now(),
        ]);
        $this->me = ['id' => $id, 'role' => 'director', 'name' => $name];
        $this->log('setup', $name);
        return ['token' => $this->newSession($id, (string)($req['device'] ?? ''))];
    }

    /** Ключ сотрудника (выдаёт директор) → токен этого телефона. 5 ошибок — пауза 10 минут. */
    private function login(array $req): array
    {
        $block = $this->db->one('SELECT fails, until_ms FROM login_fails WHERE ip = ?', [$this->ip]);
        if ($block && $block['until_ms'] > $this->now()) throw new ApiError('Слишком много попыток. Подождите 10 минут');
        $key = strtolower(trim((string)($req['key'] ?? '')));
        $emp = $key === '' ? null : $this->db->one('SELECT * FROM employees WHERE key_hash = ? AND active = 1', [$this->crypto->hash($key)]);
        if (!$emp) {
            $this->fail();
            throw new ApiError('Неверный ключ');
        }
        $this->db->run('DELETE FROM login_fails WHERE ip = ?', [$this->ip]);
        $this->me = $emp;
        $this->checkNetwork();
        $this->log('login', (string)($req['device'] ?? ''));
        return ['token' => $this->newSession((int)$emp['id'], (string)($req['device'] ?? ''))] + $this->aMe([]);
    }

    private function fail(): void
    {
        $row = $this->db->one('SELECT fails FROM login_fails WHERE ip = ?', [$this->ip]);
        $fails = (int)($row['fails'] ?? 0) + 1;
        $until = $fails >= 5 ? $this->now() + 600000 : 0;
        if ($row) $this->db->run('UPDATE login_fails SET fails = ?, until_ms = ? WHERE ip = ?', [$fails >= 5 ? 0 : $fails, $until, $this->ip]);
        else $this->db->insert('login_fails', ['ip' => $this->ip, 'fails' => $fails, 'until_ms' => $until]);
    }

    private function newSession(int $employeeId, string $device): string
    {
        $token = bin2hex(random_bytes(32));
        $this->db->insert('sessions', [
            'token_hash' => $this->crypto->hash($token), 'employee_id' => $employeeId, 'device' => mb_substr($device, 0, 120),
            'created_at' => $this->now(), 'last_seen' => $this->now(),
        ]);
        return $token;
    }

    private function auth(string $token): void
    {
        if ($token === '') throw new ApiError('Нужен вход');
        $s = $this->db->one('SELECT employee_id FROM sessions WHERE token_hash = ?', [$this->crypto->hash($token)]);
        $emp = $s ? $this->db->one('SELECT * FROM employees WHERE id = ? AND active = 1', [$s['employee_id']]) : null;
        if (!$emp) throw new ApiError('Нужен вход');
        $this->me = $emp;
        $this->checkNetwork();
        $this->db->run('UPDATE sessions SET last_seen = ? WHERE token_hash = ?', [$this->now(), $this->crypto->hash($token)]);
    }

    /** Только Wi-Fi фабрики (по адресу интернета), кроме директора. */
    private function checkNetwork(): void
    {
        if ($this->me['role'] === 'director') return;
        $allowed = array_filter(array_map('trim', explode(',', (string)$this->db->setting('allowed_ips', ''))));
        if ($allowed && !in_array($this->ip, $allowed, true)) {
            throw new ApiError('Работа только через Wi-Fi фабрики');
        }
    }

    private function need(string ...$roles): void
    {
        if (!in_array($this->me['role'], ['director', ...$roles], true)) throw new ApiError('Нет доступа');
    }

    private function log(string $action, string $detail = ''): void
    {
        $this->db->insert('audit', [
            'employee_id' => $this->me['id'] ?? null, 'action' => $action, 'detail' => mb_substr($detail, 0, 500),
            'ip' => $this->ip, 'created_at' => $this->now(),
        ]);
    }

    // ---------------------------------------------------------------- Профиль и настройки

    private function aMe(array $req): array
    {
        $roles = $this->stageRoles();
        return [
            'me' => ['id' => (int)$this->me['id'], 'name' => $this->me['name'], 'role' => $this->me['role'], 'position' => $this->me['position'] ?? ''],
            'roles' => Rules::ROLES,
            'stages' => array_map(fn($k, $v) => ['key' => $k, 'name' => $v, 'mine' => Rules::canStage($this->me['role'], $k, $roles)],
                array_keys(Rules::STAGES), array_values(Rules::STAGES)),
            'serverTime' => $this->now(),
        ];
    }

    private function stageRoles(): array
    {
        $saved = json_decode((string)$this->db->setting('stage_roles', ''), true);
        return is_array($saved) ? $saved + Rules::STAGE_ROLES : Rules::STAGE_ROLES;
    }

    private function aSettings(array $req): array
    {
        $this->need();
        return [
            'allowed_ips' => (string)$this->db->setting('allowed_ips', ''),
            'late_minutes' => (int)$this->db->setting('late_minutes', '10'),
            'stage_roles' => $this->stageRoles(),
            'my_ip' => $this->ip,
        ];
    }

    private function aSettingsSave(array $req): array
    {
        $this->need();
        if (isset($req['allowed_ips'])) {
            $ips = array_filter(array_map('trim', explode(',', (string)$req['allowed_ips'])), fn($ip) => filter_var($ip, FILTER_VALIDATE_IP));
            $this->db->setSetting('allowed_ips', implode(',', $ips));
        }
        if (isset($req['late_minutes'])) $this->db->setSetting('late_minutes', (string)max(0, min(120, (int)$req['late_minutes'])));
        if (isset($req['stage_roles']) && is_array($req['stage_roles'])) {
            $clean = [];
            foreach (Rules::STAGES as $k => $_) {
                $clean[$k] = array_values(array_intersect((array)($req['stage_roles'][$k] ?? Rules::STAGE_ROLES[$k]), array_keys(Rules::ROLES)));
            }
            $this->db->setSetting('stage_roles', json_encode($clean));
        }
        $this->log('settings', json_encode(array_intersect_key($req, ['allowed_ips' => 1, 'late_minutes' => 1]), JSON_UNESCAPED_UNICODE));
        return $this->aSettings($req);
    }

    // ---------------------------------------------------------------- Сотрудники

    private function employeeRow(array $e, bool $private): array
    {
        $row = [
            'id' => (int)$e['id'], 'name' => $e['name'], 'role' => $e['role'], 'position' => $e['position'] ?? '',
            'ms_id' => $e['ms_id'], 'active' => (bool)$e['active'], 'schedule' => Rules::schedule($e['schedule']),
            'hasKey' => !empty($e['key_hash']),
        ];
        if ($private) {
            $row['phone'] = $this->crypto->decrypt($e['phone_enc']);
            $row['email'] = $this->crypto->decrypt($e['email_enc']);
        }
        return $row;
    }

    private function aEmployees(array $req): array
    {
        $full = Rules::full($this->me['role']) || $this->me['role'] === 'accountant';
        $rows = $this->db->all('SELECT * FROM employees ORDER BY active DESC, name');
        // Всем — имена и роли (для чата и задач); телефоны и графики — директору, помощнику, бухгалтеру.
        return ['employees' => array_map(fn($e) => $this->employeeRow($e, $full), $full ? $rows : array_values(array_filter($rows, fn($e) => $e['active'])))];
    }

    /** Новый или изменённый сотрудник; новому выдаётся ключ входа (показывается один раз). */
    private function aEmployeeSave(array $req): array
    {
        $this->need();
        $e = (array)($req['employee'] ?? []);
        $role = (string)($e['role'] ?? 'manager');
        if (!isset(Rules::ROLES[$role])) throw new ApiError('Неизвестная роль');
        $name = trim((string)($e['name'] ?? ''));
        if ($name === '') throw new ApiError('Нужно имя');
        $row = [
            'name' => mb_substr($name, 0, 120), 'role' => $role, 'position' => mb_substr((string)($e['position'] ?? ''), 0, 120),
            'phone_enc' => $this->crypto->encrypt(trim((string)($e['phone'] ?? ''))),
            'email_enc' => $this->crypto->encrypt(trim((string)($e['email'] ?? ''))),
            'schedule' => json_encode(Rules::schedule(json_encode($e['schedule'] ?? null))),
            'active' => !empty($e['active']) || !isset($e['active']) ? 1 : 0,
        ];
        $id = (int)($e['id'] ?? 0);
        $key = null;
        if ($id > 0) {
            if (!$this->db->one('SELECT id FROM employees WHERE id = ?', [$id])) throw new ApiError('Сотрудник не найден');
            if ($id === (int)$this->me['id'] && ($row['role'] !== 'director' || !$row['active'])) throw new ApiError('Нельзя снять права с себя');
            $sets = implode(', ', array_map(fn($c) => "$c = ?", array_keys($row)));
            $this->db->run("UPDATE employees SET $sets WHERE id = ?", [...array_values($row), $id]);
            if (!$row['active']) $this->db->run('DELETE FROM sessions WHERE employee_id = ?', [$id]);
        } else {
            $key = Crypto::newKey();
            $id = $this->db->insert('employees', $row + ['key_hash' => $this->crypto->hash($key), 'created_at' => $this->now()]);
        }
        $this->log('employeeSave', "$id $name ($role)");
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

    // ---------------------------------------------------------------- МойСклад: сотрудники

    private function msHttp(string $method, string $path, ?array $body): array
    {
        $token = (string)($this->config['ms_token'] ?? '');
        if ($token === '') throw new ApiError('МойСклад не подключён на сервере');
        $ch = curl_init('https://api.moysklad.ru/api/remap/1.2' . $path);
        curl_setopt_array($ch, [
            CURLOPT_CUSTOMREQUEST => $method, CURLOPT_RETURNTRANSFER => true, CURLOPT_TIMEOUT => 25, CURLOPT_ENCODING => 'gzip',
            CURLOPT_HTTPHEADER => ['Authorization: Bearer ' . $token, 'Accept: application/json;charset=utf-8', 'Content-Type: application/json'],
        ]);
        if ($body !== null) curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($body, JSON_UNESCAPED_UNICODE));
        $text = (string)curl_exec($ch);
        $code = (int)curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);
        $data = json_decode($text, true) ?: [];
        if ($code >= 400 || $code === 0) throw new ApiError('МойСклад: ' . ($data['errors'][0]['error'] ?? "ошибка $code"));
        return $data;
    }

    /** Сотрудники МойСклад: новые добавляются без доступа (роль и ключ выдаёт директор), данные — обновляются. */
    private function aMsEmployeesImport(array $req): array
    {
        $this->need('assistant');
        $rows = (($this->ms)('GET', '/entity/employee?limit=1000', null))['rows'] ?? [];
        $added = 0;
        $updated = 0;
        foreach ($rows as $r) {
            if (!empty($r['archived'])) continue;
            $name = trim(implode(' ', array_filter([$r['lastName'] ?? '', $r['firstName'] ?? '', $r['middleName'] ?? ''])) ?: ($r['name'] ?? ''));
            $fields = [
                'name' => mb_substr($name, 0, 120), 'position' => mb_substr((string)($r['position'] ?? ''), 0, 120),
                'phone_enc' => $this->crypto->encrypt((string)($r['phone'] ?? '')), 'email_enc' => $this->crypto->encrypt((string)($r['email'] ?? '')),
            ];
            $have = $this->db->one('SELECT id FROM employees WHERE ms_id = ?', [$r['id']]);
            if ($have) {
                $this->db->run('UPDATE employees SET name = ?, position = ?, phone_enc = ?, email_enc = ? WHERE id = ?', [...array_values($fields), $have['id']]);
                $updated++;
            } else {
                $this->db->insert('employees', $fields + [
                    'ms_id' => $r['id'], 'role' => 'manager', 'schedule' => json_encode(Rules::DEFAULT_SCHEDULE), 'active' => 0, 'created_at' => $this->now(),
                ]);
                $added++;
            }
        }
        $this->log('msEmployeesImport', "+$added, обновлено $updated");
        return ['added' => $added, 'updated' => $updated];
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

    // ---------------------------------------------------------------- Смены

    private function aShiftSave(array $req): array
    {
        $s = (array)($req['shift'] ?? []);
        $id = (string)($s['id'] ?? '');
        if (!preg_match('/^[\w-]{6,64}$/', $id)) throw new ApiError('Неверная смена');
        $start = (int)($s['start'] ?? 0);
        $end = isset($s['end']) && $s['end'] !== null ? (int)$s['end'] : null;
        if ($start <= 0 || ($end !== null && $end < $start)) throw new ApiError('Неверное время смены');
        $have = $this->db->one('SELECT * FROM shifts WHERE id = ?', [$id]);
        $mine = !$have || (int)$have['employee_id'] === (int)$this->me['id'];
        if (!$mine && $this->me['role'] !== 'director') throw new ApiError('Это смена другого сотрудника');
        if ($have) {
            $this->db->run('UPDATE shifts SET start_ms = ?, end_ms = ?, server_end = ?, fixed_by = ? WHERE id = ?', [
                $start, $end, $end !== null && $mine ? $this->now() : $have['server_end'], $mine ? $have['fixed_by'] : $this->me['id'], $id,
            ]);
            if (!$mine) $this->log('shiftFix', $id);
        } else {
            $this->db->insert('shifts', [
                'id' => $id, 'employee_id' => $this->me['id'], 'start_ms' => $start, 'end_ms' => $end,
                'server_start' => $this->now(), 'server_end' => $end !== null ? $this->now() : null,
            ]);
        }
        return ['id' => $id];
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
        $drift = fn($phone, $server) => $phone !== null && $server !== null && abs((int)$phone - (int)$server) > 15 * 60000;
        return ['shifts' => array_map(fn($s) => [
            'id' => $s['id'], 'employeeId' => (int)$s['employee_id'], 'person' => $s['name'], 'start' => (int)$s['start_ms'],
            'end' => $s['end_ms'] !== null ? (int)$s['end_ms'] : null, 'fixed' => $s['fixed_by'] !== null,
            'suspicious' => $drift($s['start_ms'], $s['server_start']) || $drift($s['end_ms'], $s['server_end']),
        ], $this->db->all($sql . ' ORDER BY s.start_ms', $args))];
    }

    /** Месяц «2026-10» → [начало, конец) в мс по Москве. */
    private function month(string $m): array
    {
        $tz = new \DateTimeZone('Europe/Moscow');
        if (!preg_match('/^\d{4}-\d{2}$/', $m)) $m = (new \DateTimeImmutable('@' . intdiv($this->now(), 1000)))->setTimezone($tz)->format('Y-m');
        $start = new \DateTimeImmutable($m . '-01 00:00', $tz);
        return [$start->getTimestamp() * 1000, $start->modify('+1 month')->getTimestamp() * 1000];
    }

    // ---------------------------------------------------------------- Производство

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
                'id' => (int)$s['id'], 'stage' => $s['stage'], 'startedBy' => $s['started_name'], 'startedAt' => $s['started_at'] !== null ? (int)$s['started_at'] : null,
                'finishedBy' => $s['finished_name'], 'finishedAt' => $s['finished_at'] !== null ? (int)$s['finished_at'] : null,
                'quantity' => (int)$s['quantity'], 'comment' => $s['comment'],
            ];
        }
        return ['jobs' => array_map(fn($j) => [
            'id' => (int)$j['id'], 'quoteId' => $j['quote_id'], 'title' => $j['title'], 'client' => $j['client'], 'quantity' => (int)$j['quantity'],
            'deadline' => $j['deadline'] !== null ? (int)$j['deadline'] : null, 'done' => (bool)$j['done'], 'stages' => $byJob[(int)$j['id']] ?? [],
        ], $jobs)];
    }

    private function aJobSave(array $req): array
    {
        $this->need('assistant', 'manager');
        $j = (array)($req['job'] ?? []);
        $title = trim((string)($j['title'] ?? ''));
        if ($title === '') throw new ApiError('Нужно название заказа');
        $row = [
            'quote_id' => mb_substr((string)($j['quoteId'] ?? ''), 0, 64), 'title' => mb_substr($title, 0, 200),
            'client' => mb_substr((string)($j['client'] ?? ''), 0, 200), 'quantity' => max(0, (int)($j['quantity'] ?? 0)),
            'deadline' => !empty($j['deadline']) ? (int)$j['deadline'] : null, 'done' => !empty($j['done']) ? 1 : 0,
        ];
        $id = (int)($j['id'] ?? 0);
        if ($id > 0) {
            $sets = implode(', ', array_map(fn($c) => "$c = ?", array_keys($row)));
            $this->db->run("UPDATE jobs SET $sets WHERE id = ?", [...array_values($row), $id]);
        } else {
            $id = $this->db->insert('jobs', $row + ['created_by' => $this->me['id'], 'created_at' => $this->now()]);
        }
        $this->log('jobSave', "$id $title");
        return ['id' => $id];
    }

    /** «Начать» этап: только своя роль (директор и помощник — любой); отмечается кто и когда. */
    private function aStageStart(array $req): array
    {
        $stage = (string)($req['stage'] ?? '');
        $jobId = (int)($req['job_id'] ?? 0);
        if (!isset(Rules::STAGES[$stage])) throw new ApiError('Неизвестный этап');
        if (!Rules::canStage($this->me['role'], $stage, $this->stageRoles())) throw new ApiError('Этот этап ведёт другой участок');
        if (!$this->db->one('SELECT id FROM jobs WHERE id = ? AND done = 0', [$jobId])) throw new ApiError('Заказ не найден или закрыт');
        if ($this->db->one('SELECT id FROM stages WHERE job_id = ? AND stage = ? AND finished_at IS NULL', [$jobId, $stage])) {
            throw new ApiError('Этап уже идёт');
        }
        $id = $this->db->insert('stages', ['job_id' => $jobId, 'stage' => $stage, 'started_by' => $this->me['id'], 'started_at' => $this->now()]);
        $this->log('stageStart', "$jobId $stage");
        return ['id' => $id];
    }

    /** «Завершить» этап: количество и комментарий; время работы = конец − начало. */
    private function aStageFinish(array $req): array
    {
        $s = $this->db->one('SELECT * FROM stages WHERE id = ?', [(int)($req['id'] ?? 0)]);
        if (!$s || $s['finished_at'] !== null) throw new ApiError('Этап не найден или уже завершён');
        if (!Rules::canStage($this->me['role'], $s['stage'], $this->stageRoles())) throw new ApiError('Этот этап ведёт другой участок');
        $this->db->run('UPDATE stages SET finished_by = ?, finished_at = ?, quantity = ?, comment = ? WHERE id = ?', [
            $this->me['id'], $this->now(), max(0, (int)($req['quantity'] ?? 0)), mb_substr((string)($req['comment'] ?? ''), 0, 500), $s['id'],
        ]);
        $this->log('stageFinish', $s['job_id'] . ' ' . $s['stage']);
        return ['minutes' => intdiv($this->now() - (int)$s['started_at'], 60000)];
    }

    // ---------------------------------------------------------------- Рейтинг и журнал

    private function aRating(array $req): array
    {
        [$from, $to] = $this->month((string)($req['month'] ?? ''));
        $people = array_map(fn($e) => ['id' => (int)$e['id'], 'name' => $e['name'], 'role' => $e['role'], 'schedule' => Rules::schedule($e['schedule'])],
            $this->db->all("SELECT * FROM employees WHERE active = 1 AND role <> 'director'"));
        $shifts = array_map(fn($s) => ['employee_id' => (int)$s['employee_id'], 'start_ms' => (int)$s['start_ms']],
            $this->db->all('SELECT employee_id, start_ms FROM shifts WHERE start_ms >= ? AND start_ms < ?', [$from, $to]));
        $stages = array_map(fn($s) => ['finished_by' => (int)$s['finished_by'], 'stage' => $s['stage']],
            $this->db->all('SELECT finished_by, stage FROM stages WHERE finished_at >= ? AND finished_at < ?', [$from, $to]));
        $rows = Rules::rating($people, $shifts, $stages, $from, $to, $this->now(), (int)$this->db->setting('late_minutes', '10'), new \DateTimeZone('Europe/Moscow'));
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

final class ApiError extends \RuntimeException
{
}
