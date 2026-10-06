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
    use ApiPeople;
    use ApiWork;

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
}

final class ApiError extends \RuntimeException
{
}
