<?php
declare(strict_types=1);

namespace Ks;

use PDO;

/**
 * База данных проекта: MySQL на beget (в тестах — SQLite в памяти).
 * Таблицы создаются и дополняются сами (migrate), версия схемы — в таблице settings (SCHEMA).
 */
final class Db
{
    public PDO $pdo;
    public bool $sqlite;

    public function __construct(PDO $pdo)
    {
        $this->pdo = $pdo;
        $this->pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
        $this->pdo->setAttribute(PDO::ATTR_DEFAULT_FETCH_MODE, PDO::FETCH_ASSOC);
        $this->sqlite = $pdo->getAttribute(PDO::ATTR_DRIVER_NAME) === 'sqlite';
    }

    public static function fromConfig(array $c): self
    {
        $dsn = sprintf('mysql:host=%s;dbname=%s;charset=utf8mb4', $c['db_host'], $c['db_name']);
        return new self(new PDO($dsn, $c['db_user'], $c['db_password'], [PDO::ATTR_EMULATE_PREPARES => false]));
    }

    /** @return list<array<string, mixed>> */
    public function all(string $sql, array $args = []): array
    {
        $st = $this->pdo->prepare($sql);
        $st->execute($args);
        return $st->fetchAll();
    }

    public function one(string $sql, array $args = []): ?array
    {
        $rows = $this->all($sql, $args);
        return $rows[0] ?? null;
    }

    public function run(string $sql, array $args = []): int
    {
        $st = $this->pdo->prepare($sql);
        $st->execute($args);
        return $st->rowCount();
    }

    public function insert(string $table, array $row): int
    {
        $cols = array_keys($row);
        $sql = sprintf('INSERT INTO %s (%s) VALUES (%s)', $table, implode(',', $cols), implode(',', array_fill(0, count($cols), '?')));
        $this->run($sql, array_values($row));
        return (int)$this->pdo->lastInsertId();
    }

    private function id(): string
    {
        return $this->sqlite ? 'INTEGER PRIMARY KEY AUTOINCREMENT' : 'BIGINT AUTO_INCREMENT PRIMARY KEY';
    }

    private function table(string $name, string $body): void
    {
        $tail = $this->sqlite ? '' : ' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4';
        $this->pdo->exec("CREATE TABLE IF NOT EXISTS $name ($body)$tail");
    }

    /** Версия схемы: при совпадении миграция не выполняется (быстрый ответ на каждый запрос). */
    public const SCHEMA = 3;

    public function migrate(): void
    {
        try {
            if ((int)$this->setting('schema', '0') >= self::SCHEMA) return;
        } catch (\PDOException $e) {
            // таблицы settings ещё нет — первая установка
        }
        $id = $this->id();
        $this->table('settings', 'name VARCHAR(64) PRIMARY KEY, value TEXT');
        // Сотрудник: роль, связь с МойСклад, ключ входа (только хэш), телефон и e-mail — зашифрованы.
        $this->table('employees', "id $id, name VARCHAR(120) NOT NULL, role VARCHAR(24) NOT NULL, position VARCHAR(120) DEFAULT '',
            ms_id VARCHAR(64) DEFAULT NULL, phone_enc TEXT, email_enc TEXT, key_hash VARCHAR(64) DEFAULT NULL,
            schedule TEXT, active INTEGER NOT NULL DEFAULT 1, created_at BIGINT NOT NULL");
        $this->table('sessions', 'token_hash VARCHAR(64) PRIMARY KEY, employee_id BIGINT NOT NULL, device VARCHAR(120),
            created_at BIGINT NOT NULL, last_seen BIGINT NOT NULL');
        // Смена: время телефона и время сервера (сверка подведённых часов).
        $this->table('shifts', "id VARCHAR(64) PRIMARY KEY, employee_id BIGINT NOT NULL, start_ms BIGINT NOT NULL, end_ms BIGINT DEFAULT NULL,
            server_start BIGINT NOT NULL, server_end BIGINT DEFAULT NULL, fixed_by BIGINT DEFAULT NULL");
        // Производство: заказ и его этапы — кто начал, кто закончил, когда.
        $this->table('jobs', "id $id, quote_id VARCHAR(64) DEFAULT '', title VARCHAR(200) NOT NULL, client VARCHAR(200) DEFAULT '',
            quantity INTEGER DEFAULT 0, deadline BIGINT DEFAULT NULL, created_by BIGINT, created_at BIGINT NOT NULL, done INTEGER NOT NULL DEFAULT 0");
        $this->table('stages', "id $id, job_id BIGINT NOT NULL, stage VARCHAR(24) NOT NULL, started_by BIGINT, started_at BIGINT,
            finished_by BIGINT, finished_at BIGINT, quantity INTEGER DEFAULT 0, comment VARCHAR(500) DEFAULT ''");
        $this->table('audit', "id $id, employee_id BIGINT, action VARCHAR(64) NOT NULL, detail VARCHAR(500) DEFAULT '', ip VARCHAR(64),
            created_at BIGINT NOT NULL");
        $this->table('login_fails', 'ip VARCHAR(64) PRIMARY KEY, fails INTEGER NOT NULL, until_ms BIGINT NOT NULL');
        // Активность в приложении: минуты на экране за день, заходы, самый долгий перерыв в рабочее время.
        $this->table('activity', "employee_id BIGINT NOT NULL, day VARCHAR(10) NOT NULL, minutes INTEGER NOT NULL DEFAULT 0,
            sessions INTEGER NOT NULL DEFAULT 0, first_ms BIGINT, last_ms BIGINT, max_gap_ms BIGINT NOT NULL DEFAULT 0, PRIMARY KEY (employee_id, day)");
        // Действия в приложении: КП, клиент, товар, заказ, отгрузка, приёмка…
        $this->table('events', "id $id, employee_id BIGINT NOT NULL, kind VARCHAR(24) NOT NULL, detail VARCHAR(200) DEFAULT '', created_at BIGINT NOT NULL");
        $this->index('stages_job', 'stages', 'job_id');
        $this->index('shifts_emp', 'shifts', 'employee_id, start_ms');
        $this->index('audit_time', 'audit', 'created_at');
        $this->index('events_emp', 'events', 'employee_id, created_at');
        $this->index('employees_ms', 'employees', 'ms_id', true);
        $this->setSetting('schema', (string)self::SCHEMA);
    }

    /** Индекс без «IF NOT EXISTS» (его нет в MySQL): повторное создание просто пропускаем. */
    private function index(string $name, string $table, string $cols, bool $unique = false): void
    {
        try {
            $this->pdo->exec('CREATE ' . ($unique ? 'UNIQUE ' : '') . "INDEX $name ON $table ($cols)");
        } catch (\PDOException $e) {
            // уже есть
        }
    }

    public function setting(string $name, ?string $default = null): ?string
    {
        $row = $this->one('SELECT value FROM settings WHERE name = ?', [$name]);
        return $row['value'] ?? $default;
    }

    /** Транзакция: этапы и смены меняются атомарно (два одновременных запроса не создают дубль). */
    public function tx(callable $fn): mixed
    {
        $this->pdo->beginTransaction();
        try {
            $r = $fn();
            $this->pdo->commit();
            return $r;
        } catch (\Throwable $e) {
            $this->pdo->rollBack();
            throw $e;
        }
    }

    /** Блокировка строки до конца транзакции (MySQL; SQLite блокирует всю базу сама). */
    public function lock(string $table, int $id): ?array
    {
        return $this->one("SELECT * FROM $table WHERE id = ?" . ($this->sqlite ? '' : ' FOR UPDATE'), [$id]);
    }

    public function setSetting(string $name, string $value): void
    {
        if ($this->setting($name) === null) {
            $this->run('INSERT INTO settings (name, value) VALUES (?, ?)', [$name, $value]);
        } else {
            $this->run('UPDATE settings SET value = ? WHERE name = ?', [$value, $name]);
        }
    }
}
