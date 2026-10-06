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
    /** Действия после входа (имя → метод); всё остальное — «Неизвестное действие». */
    private const ACTIONS = [
        'me', 'logout', 'settings', 'settingsSave', 'sessions', 'sessionDrop',
        'employees', 'employeeSave', 'employeeKey', 'msEmployeesImport', 'msEmployeeSave', 'msAudit',
        'shiftSave', 'shifts', 'jobs', 'jobSave', 'stageStart', 'stageFinish',
        'rating', 'audit', 'ping', 'event', 'activity',
    ];
    /** Виды действий для учёта активности. */
    private const EVENT_KINDS = ['quote', 'quoteSend', 'client', 'product', 'order', 'payment', 'ship', 'receive', 'inventory', 'label', 'call', 'other'];
    /** Сессия телефона: без работы 30 дней или 180 дней всего — нужен новый вход. */
    private const SESSION_IDLE_MS = 30 * 86400000;
    private const SESSION_MAX_MS = 180 * 86400000;

    private ?array $me = null;
    private string $token = '';
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
            if ($action === 'setup') return ['ok' => true] + $this->setup($req);
            if ($action === 'login') return ['ok' => true] + $this->login($req);
            if (!in_array($action, self::ACTIONS, true)) throw new ApiError('Неизвестное действие');
            $this->auth((string)($req['token'] ?? ''));
            $method = 'a' . ucfirst($action);
            return ['ok' => true] + $this->$method($req);
        } catch (ApiError $e) {
            return ['ok' => false, 'error' => $e->getMessage()];
        } catch (\Throwable $e) {
            error_log('ks api: ' . $e->getMessage());
            return ['ok' => false, 'error' => 'Ошибка сервера'];
        }
    }

    // ---------------------------------------------------------------- Вход

    /** Первый запуск: директор создаётся ключом установки из config (один раз; после — недоступно). */
    private function setup(array $req): array
    {
        $this->checkBlock();
        if ($this->db->one('SELECT id FROM employees LIMIT 1')) throw new ApiError('Директор уже создан');
        $setupKey = trim((string)($this->config['setup_key'] ?? ''));
        if ($setupKey === '' || !hash_equals($setupKey, trim((string)($req['setup_key'] ?? '')))) {
            $this->fail();
            $this->log('setupFail', '');
            throw new ApiError('Неверный ключ установки');
        }
        $name = trim((string)($req['name'] ?? 'Директор')) ?: 'Директор';
        $id = $this->db->insert('employees', [
            'name' => mb_substr($name, 0, 120), 'role' => 'director', 'position' => 'Директор',
            'schedule' => json_encode(Rules::DEFAULT_SCHEDULE), 'active' => 1, 'created_at' => $this->now(),
        ]);
        $this->me = ['id' => $id, 'role' => 'director', 'name' => $name];
        $this->log('setup', $name);
        return ['token' => $this->newSession($id, (string)($req['device'] ?? ''))];
    }

    /** Ключ сотрудника (выдаёт директор) → токен этого телефона. Ошибки подряд — пауза 10 минут. */
    private function login(array $req): array
    {
        $this->checkBlock();
        $key = strtolower(trim((string)($req['key'] ?? '')));
        $emp = $key === '' ? null : $this->db->one('SELECT * FROM employees WHERE key_hash = ? AND active = 1', [$this->crypto->hash($key)]);
        if (!$emp) {
            $this->fail();
            $this->log('loginFail', mb_substr((string)($req['device'] ?? ''), 0, 60));
            throw new ApiError('Неверный ключ');
        }
        $this->db->run('DELETE FROM login_fails WHERE ip = ?', [$this->ip]);
        $this->me = $emp;
        $this->checkNetwork();
        $this->log('login', (string)($req['device'] ?? ''));
        return ['token' => $this->newSession((int)$emp['id'], (string)($req['device'] ?? ''))] + $this->aMe([]);
    }

    private function checkBlock(): void
    {
        $block = $this->db->one('SELECT until_ms FROM login_fails WHERE ip = ?', [$this->ip]);
        if ($block && $block['until_ms'] > $this->now()) throw new ApiError('Слишком много попыток. Подождите 10 минут');
    }

    /** Счётчик ошибок по адресу: из сети фабрики (там все за одним адресом) — 20 попыток, снаружи — 5. */
    private function fail(): void
    {
        $limit = $this->ipAllowed($this->ip, $this->allowedIps()) ? 20 : 5;
        $row = $this->db->one('SELECT fails FROM login_fails WHERE ip = ?', [$this->ip]);
        $fails = (int)($row['fails'] ?? 0) + 1;
        $until = $fails >= $limit ? $this->now() + 600000 : 0;
        if ($row) $this->db->run('UPDATE login_fails SET fails = ?, until_ms = ? WHERE ip = ?', [$fails >= $limit ? 0 : $fails, $until, $this->ip]);
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
        $hash = $this->crypto->hash($token);
        $s = $this->db->one('SELECT * FROM sessions WHERE token_hash = ?', [$hash]);
        if ($s && ($this->now() - (int)$s['last_seen'] > self::SESSION_IDLE_MS || $this->now() - (int)$s['created_at'] > self::SESSION_MAX_MS)) {
            $this->db->run('DELETE FROM sessions WHERE token_hash = ?', [$hash]);
            $s = null;
        }
        $emp = $s ? $this->db->one('SELECT * FROM employees WHERE id = ? AND active = 1', [$s['employee_id']]) : null;
        if (!$emp) throw new ApiError('Нужен вход');
        $this->me = $emp;
        $this->token = $token;
        $this->checkNetwork();
        $this->db->run('UPDATE sessions SET last_seen = ? WHERE token_hash = ?', [$this->now(), $hash]);
    }

    private function allowedIps(): array
    {
        return array_values(array_filter(array_map('trim', explode(',', (string)$this->db->setting('allowed_ips', '')))));
    }

    /** Адрес или сеть (CIDR, например 95.31.10.0/24). */
    private function ipAllowed(string $ip, array $allowed): bool
    {
        foreach ($allowed as $rule) {
            if (!str_contains($rule, '/')) {
                if ($rule === $ip) return true;
                continue;
            }
            [$net, $bits] = explode('/', $rule, 2);
            $a = @inet_pton($ip);
            $b = @inet_pton($net);
            if ($a === false || $b === false || strlen($a) !== strlen($b)) continue;
            $bits = (int)$bits;
            $bytes = intdiv($bits, 8);
            if (substr($a, 0, $bytes) !== substr($b, 0, $bytes)) continue;
            $rest = $bits % 8;
            if ($rest === 0 || ((ord($a[$bytes]) ^ ord($b[$bytes])) & (0xFF << (8 - $rest)) & 0xFF) === 0) return true;
        }
        return false;
    }

    private static function validIpRule(string $rule): bool
    {
        if (!str_contains($rule, '/')) return (bool)filter_var($rule, FILTER_VALIDATE_IP);
        [$net, $bits] = explode('/', $rule, 2);
        if (!ctype_digit($bits)) return false;
        if (filter_var($net, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4)) return (int)$bits >= 8 && (int)$bits <= 32;
        return filter_var($net, FILTER_VALIDATE_IP, FILTER_FLAG_IPV6) && (int)$bits >= 16 && (int)$bits <= 128;
    }

    /** Только Wi-Fi фабрики (по адресу интернета), кроме директора. */
    private function checkNetwork(): void
    {
        if ($this->me['role'] === 'director') return;
        $allowed = $this->allowedIps();
        if ($allowed && !$this->ipAllowed($this->ip, $allowed)) {
            $this->log('networkDenied', $this->ip);
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

    private static function json(mixed $v): string
    {
        return (string)json_encode($v, JSON_UNESCAPED_UNICODE);
    }

    // ---------------------------------------------------------------- Профиль, выход, устройства

    private function aMe(array $req): array
    {
        $roles = $this->stageRoles();
        return [
            'me' => ['id' => (int)$this->me['id'], 'name' => $this->me['name'], 'role' => $this->me['role'], 'position' => $this->me['position'] ?? ''],
            'roles' => Rules::ROLES,
            'stages' => array_map(fn($k, $v) => ['key' => $k, 'name' => $v, 'mine' => Rules::canStage($this->me['role'], $k, $roles)],
                array_keys(Rules::STAGES), array_values(Rules::STAGES)),
            // Учёт активности: телефон отмечается раз в минуту (только офис и только в рабочее время).
            'tracked' => in_array($this->me['role'], Rules::OFFICE, true),
            'serverTime' => $this->now(),
        ];
    }

    /** Выход: токен этого телефона перестаёт работать. */
    private function aLogout(array $req): array
    {
        $this->db->run('DELETE FROM sessions WHERE token_hash = ?', [$this->crypto->hash($this->token)]);
        $this->log('logout');
        return [];
    }

    /** Телефоны, с которых вошли сотрудники (директор). */
    private function aSessions(array $req): array
    {
        $this->need();
        $rows = $this->db->all('SELECT s.token_hash, s.device, s.created_at, s.last_seen, e.name, e.id AS emp FROM sessions s
            JOIN employees e ON e.id = s.employee_id ORDER BY s.last_seen DESC LIMIT 300');
        $mine = $this->crypto->hash($this->token);
        return ['sessions' => array_map(fn($s) => [
            'id' => substr($s['token_hash'], 0, 16), 'employeeId' => (int)$s['emp'], 'person' => $s['name'], 'device' => $s['device'],
            'created' => (int)$s['created_at'], 'lastSeen' => (int)$s['last_seen'], 'current' => $s['token_hash'] === $mine,
        ], $rows)];
    }

    private function aSessionDrop(array $req): array
    {
        $this->need();
        $id = (string)($req['id'] ?? '');
        if (!preg_match('/^[0-9a-f]{16}$/', $id)) throw new ApiError('Неверное устройство');
        $n = $this->db->run('DELETE FROM sessions WHERE token_hash LIKE ?', [$id . '%']);
        $this->log('sessionDrop', $id);
        return ['removed' => $n];
    }

    // ---------------------------------------------------------------- Настройки

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
            'idle_minutes' => (int)$this->db->setting('idle_minutes', '30'),
            'stage_roles' => $this->stageRoles(),
            'my_ip' => $this->ip,
        ];
    }

    private function aSettingsSave(array $req): array
    {
        $this->need();
        $before = $this->aSettings([]);
        if (isset($req['allowed_ips'])) {
            $rules = array_values(array_filter(array_map('trim', preg_split('/[,\s]+/', (string)$req['allowed_ips']))));
            $bad = array_filter($rules, fn($r) => !self::validIpRule($r));
            if ($bad) throw new ApiError('Неверный адрес: ' . implode(', ', $bad));
            $this->db->setSetting('allowed_ips', implode(',', array_unique($rules)));
        }
        if (isset($req['late_minutes'])) $this->db->setSetting('late_minutes', (string)max(0, min(120, (int)$req['late_minutes'])));
        if (isset($req['idle_minutes'])) $this->db->setSetting('idle_minutes', (string)max(5, min(240, (int)$req['idle_minutes'])));
        if (isset($req['stage_roles']) && is_array($req['stage_roles'])) {
            $clean = [];
            foreach (Rules::STAGES as $k => $_) {
                $clean[$k] = array_values(array_intersect((array)($req['stage_roles'][$k] ?? $this->stageRoles()[$k]), array_keys(Rules::ROLES)));
            }
            $this->db->setSetting('stage_roles', self::json($clean));
        }
        $after = $this->aSettings([]);
        $diff = [];
        foreach (['allowed_ips', 'late_minutes', 'idle_minutes', 'stage_roles'] as $k) {
            if ($before[$k] !== $after[$k]) $diff[] = $k . ': ' . self::json($before[$k]) . ' → ' . self::json($after[$k]);
        }
        $this->log('settings', implode('; ', $diff));
        return $after;
    }

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
                'id' => (int)$s['id'], 'stage' => $s['stage'], 'startedBy' => $s['started_name'], 'startedById' => $s['started_by'] !== null ? (int)$s['started_by'] : null,
                'startedAt' => $s['started_at'] !== null ? (int)$s['started_at'] : null,
                'finishedBy' => $s['finished_name'], 'finishedAt' => $s['finished_at'] !== null ? (int)$s['finished_at'] : null,
                'quantity' => (int)$s['quantity'], 'comment' => $s['comment'],
            ];
        }
        return ['jobs' => array_map(fn($j) => [
            'id' => (int)$j['id'], 'quoteId' => $j['quote_id'], 'title' => $j['title'], 'client' => $j['client'], 'quantity' => (int)$j['quantity'],
            'deadline' => $j['deadline'] !== null ? (int)$j['deadline'] : null, 'done' => (bool)$j['done'], 'stages' => $byJob[(int)$j['id']] ?? [],
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
            $left = (int)$job['quantity'] - $this->stageDone((int)$job['id'], $s['stage']);
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
        return [
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
