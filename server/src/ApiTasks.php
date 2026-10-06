<?php
declare(strict_types=1);

namespace Ks;

/**
 * Задачи (ставят все: себе, коллеге; руководство видит все), комментарии с @упоминаниями, файлы (зашифрованы),
 * и уведомления сотрудникам (телефон забирает их сам — без Firebase).
 */
trait ApiTasks
{
    private static array $STATUSES = ['new', 'work', 'done', 'accepted', 'cancelled'];
    private static array $REPEATS = ['', 'day', 'week', 'month'];

    private function notify(int $employeeId, string $kind, string $title, string $body = '', string $ref = ''): void
    {
        if ($employeeId === (int)$this->me['id']) return;
        $this->db->insert('notifications', [
            'employee_id' => $employeeId, 'kind' => $kind, 'title' => mb_substr($title, 0, 200), 'body' => mb_substr($body, 0, 500),
            'ref' => $ref, 'created_at' => $this->now(),
        ]);
    }

    private function taskRow(array $t, array $names): array
    {
        $late = $t['due_ms'] !== null && !in_array($t['status'], ['done', 'accepted', 'cancelled'], true) && (int)$t['due_ms'] < $this->now();
        return [
            'id' => (int)$t['id'], 'title' => $t['title'], 'body' => (string)$t['body'],
            'authorId' => (int)$t['author_id'], 'author' => $names[(int)$t['author_id']] ?? '—',
            'assigneeId' => (int)$t['assignee_id'], 'assignee' => $names[(int)$t['assignee_id']] ?? '—',
            'status' => $t['status'], 'priority' => (int)$t['priority'],
            'due' => $t['due_ms'] !== null ? (int)$t['due_ms'] : null, 'remind' => $t['remind_ms'] !== null ? (int)$t['remind_ms'] : null,
            'linkType' => (string)$t['link_type'], 'linkId' => (string)$t['link_id'], 'linkTitle' => (string)$t['link_title'],
            'repeat' => (string)$t['repeat_rule'], 'checklist' => json_decode((string)$t['checklist'], true) ?: [],
            'created' => (int)$t['created_at'], 'updated' => (int)$t['updated_at'],
            'doneAt' => $t['done_at'] !== null ? (int)$t['done_at'] : null, 'late' => $late,
        ];
    }

    private function names(): array
    {
        $out = [];
        foreach ($this->db->all('SELECT id, name FROM employees') as $e) $out[(int)$e['id']] = $e['name'];
        return $out;
    }

    /** Можно смотреть: автор, исполнитель, руководство. */
    private function taskFor(int $id, bool $lock = false): array
    {
        $t = $lock ? $this->db->lock('tasks', $id) : $this->db->one('SELECT * FROM tasks WHERE id = ?', [$id]);
        if (!$t) throw new ApiError('Задача не найдена');
        $me = (int)$this->me['id'];
        if ((int)$t['author_id'] !== $me && (int)$t['assignee_id'] !== $me && !Rules::full($this->me['role'])) throw new ApiError('Нет доступа');
        return $t;
    }

    /** Списки: мои (мне поставили), я поставил, все (руководство); фильтр по статусу: открытые / закрытые. */
    private function aTasks(array $req): array
    {
        $scope = (string)($req['scope'] ?? 'mine');
        $me = (int)$this->me['id'];
        [$where, $args] = match ($scope) {
            'created' => ['author_id = ?', [$me]],
            'all' => Rules::full($this->me['role']) ? ['1 = 1', []] : throw new ApiError('Нет доступа'),
            default => ['assignee_id = ?', [$me]],
        };
        $open = ($req['closed'] ?? false) ? "status IN ('accepted', 'cancelled')" : "status NOT IN ('accepted', 'cancelled')";
        $rows = $this->db->all("SELECT * FROM tasks WHERE $where AND $open ORDER BY priority DESC, CASE WHEN due_ms IS NULL THEN 1 ELSE 0 END, due_ms, id DESC LIMIT 300", $args);
        $names = $this->names();
        return ['tasks' => array_map(fn($t) => $this->taskRow($t, $names), $rows)];
    }

    private function aTask(array $req): array
    {
        $t = $this->taskFor((int)($req['id'] ?? 0));
        $names = $this->names();
        $comments = $this->db->all('SELECT * FROM task_comments WHERE task_id = ? ORDER BY id', [$t['id']]);
        $files = $this->db->all('SELECT id, name, mime, size, owner_id, created_at FROM files WHERE task_id = ? ORDER BY id', [$t['id']]);
        return [
            'task' => $this->taskRow($t, $names),
            'comments' => array_map(fn($c) => ['id' => (int)$c['id'], 'who' => $names[(int)$c['employee_id']] ?? '—', 'text' => $c['text'], 'time' => (int)$c['created_at']], $comments),
            'files' => array_map(fn($f) => ['id' => (int)$f['id'], 'name' => $f['name'], 'mime' => $f['mime'], 'size' => (int)$f['size'],
                'who' => $names[(int)$f['owner_id']] ?? '—', 'time' => (int)$f['created_at']], $files),
        ];
    }

    /** Новая задача или правка (автор или руководство): название, описание, кому, срок, напоминание, приоритет, связь, повтор, чек-лист. */
    private function aTaskSave(array $req): array
    {
        $t = (array)($req['task'] ?? []);
        $id = (int)($t['id'] ?? 0);
        $title = trim((string)($t['title'] ?? ''));
        if ($title === '') throw new ApiError('Нужно название задачи');
        $assignee = (int)($t['assigneeId'] ?? $this->me['id']);
        if (!$this->db->one('SELECT id FROM employees WHERE id = ? AND active = 1', [$assignee])) throw new ApiError('Исполнитель не найден');
        $repeat = (string)($t['repeat'] ?? '');
        if (!in_array($repeat, self::$REPEATS, true)) $repeat = '';
        $checklist = [];
        foreach (array_slice((array)($t['checklist'] ?? []), 0, 50) as $item) {
            $text = trim(mb_substr((string)($item['text'] ?? ''), 0, 200));
            if ($text !== '') $checklist[] = ['text' => $text, 'done' => !empty($item['done'])];
        }
        $row = [
            'title' => mb_substr($title, 0, 200), 'body' => mb_substr((string)($t['body'] ?? ''), 0, 5000), 'assignee_id' => $assignee,
            'priority' => !empty($t['priority']) ? 1 : 0,
            'due_ms' => !empty($t['due']) ? (int)$t['due'] : null, 'remind_ms' => !empty($t['remind']) ? (int)$t['remind'] : null,
            'link_type' => mb_substr((string)($t['linkType'] ?? ''), 0, 24), 'link_id' => mb_substr((string)($t['linkId'] ?? ''), 0, 64),
            'link_title' => mb_substr((string)($t['linkTitle'] ?? ''), 0, 200), 'repeat_rule' => $repeat,
            'checklist' => self::json($checklist), 'updated_at' => $this->now(),
        ];
        if ($id > 0) {
            $have = $this->taskFor($id);
            if ((int)$have['author_id'] !== (int)$this->me['id'] && !Rules::full($this->me['role'])) throw new ApiError('Менять задачу может тот, кто её поставил');
            $sets = implode(', ', array_map(fn($c) => "$c = ?", array_keys($row)));
            $this->db->run("UPDATE tasks SET $sets, notified = 0 WHERE id = ?", [...array_values($row), $id]);
            if ((int)$have['assignee_id'] !== $assignee) $this->notify($assignee, 'task', 'Вам задача: ' . $title, (string)$this->me['name'], (string)$id);
            else $this->notify($assignee, 'task', 'Задача изменена: ' . $title, (string)$this->me['name'], (string)$id);
            $this->log('taskSave', "$id $title");
        } else {
            $id = $this->db->insert('tasks', $row + ['author_id' => $this->me['id'], 'status' => 'new', 'created_at' => $this->now()]);
            $this->notify($assignee, 'task', 'Новая задача: ' . $title, (string)$this->me['name'] . ($row['due_ms'] ? ' · срок ' . $this->fmt((int)$row['due_ms']) : ''), (string)$id);
            $this->log('taskAdd', "$id $title → $assignee");
            $this->eventLog('other', 'Задача: ' . $title);
        }
        return $this->aTask(['id' => $id]);
    }

    private function fmt(int $ms): string
    {
        return (new \DateTimeImmutable('@' . intdiv($ms, 1000)))->setTimezone(new \DateTimeZone('Europe/Moscow'))->format('d.m H:i');
    }

    /**
     * Статус: исполнитель — «в работе», «сделано»; автор (или руководство) — «принято», вернуть в работу, отменить.
     * Принятая повторяющаяся задача создаёт следующую (через день, неделю, месяц).
     */
    private function aTaskStatus(array $req): array
    {
        $status = (string)($req['status'] ?? '');
        if (!in_array($status, self::$STATUSES, true)) throw new ApiError('Неизвестный статус');
        return $this->db->tx(function () use ($req, $status) {
            $t = $this->taskFor((int)($req['id'] ?? 0), true);
            $me = (int)$this->me['id'];
            $isAuthor = (int)$t['author_id'] === $me || Rules::full($this->me['role']);
            $isAssignee = (int)$t['assignee_id'] === $me;
            $allowed = match ($status) {
                'work' => $isAssignee || $isAuthor,
                'done' => $isAssignee,
                'accepted', 'cancelled' => $isAuthor,
                default => false,
            };
            if (!$allowed) throw new ApiError('Этот шаг делает ' . ($status === 'done' ? 'исполнитель' : 'тот, кто поставил задачу'));
            $extra = match ($status) {
                'done' => ', done_at = ' . $this->now(),
                'accepted' => ', accepted_at = ' . $this->now(),
                'work' => ', done_at = NULL',
                default => '',
            };
            $this->db->run("UPDATE tasks SET status = ?, updated_at = ?$extra WHERE id = ?", [$status, $this->now(), $t['id']]);
            $title = $t['title'];
            $who = (string)$this->me['name'];
            match ($status) {
                'done' => $this->notify((int)$t['author_id'], 'task', 'Сделано: ' . $title, $who . ' — проверьте и примите', (string)$t['id']),
                'accepted' => $this->notify((int)$t['assignee_id'], 'task', 'Принято: ' . $title, $who, (string)$t['id']),
                'cancelled' => $this->notify((int)$t['assignee_id'], 'task', 'Отменена: ' . $title, $who, (string)$t['id']),
                'work' => $isAssignee ? $this->notify((int)$t['author_id'], 'task', 'В работе: ' . $title, $who, (string)$t['id'])
                    : $this->notify((int)$t['assignee_id'], 'task', 'Вернули в работу: ' . $title, $who, (string)$t['id']),
                default => null,
            };
            if ($status === 'accepted' && $t['repeat_rule'] !== '' && $t['due_ms'] !== null) {
                $step = ['day' => '+1 day', 'week' => '+1 week', 'month' => '+1 month'][$t['repeat_rule']];
                $tz = new \DateTimeZone('Europe/Moscow');
                $next = (new \DateTimeImmutable('@' . intdiv((int)$t['due_ms'], 1000)))->setTimezone($tz)->modify($step)->getTimestamp() * 1000;
                $checklist = array_map(fn($i) => ['text' => $i['text'], 'done' => false], json_decode((string)$t['checklist'], true) ?: []);
                $newId = $this->db->insert('tasks', [
                    'title' => $t['title'], 'body' => $t['body'], 'author_id' => $t['author_id'], 'assignee_id' => $t['assignee_id'], 'status' => 'new',
                    'priority' => $t['priority'], 'due_ms' => $next, 'remind_ms' => $t['remind_ms'] !== null ? $next - ((int)$t['due_ms'] - (int)$t['remind_ms']) : null,
                    'link_type' => $t['link_type'], 'link_id' => $t['link_id'], 'link_title' => $t['link_title'], 'repeat_rule' => $t['repeat_rule'],
                    'checklist' => self::json($checklist), 'created_at' => $this->now(), 'updated_at' => $this->now(),
                ]);
                $this->notify((int)$t['assignee_id'], 'task', 'Новая задача: ' . $title, 'повтор · срок ' . $this->fmt($next), (string)$newId);
            }
            $this->log('taskStatus', "{$t['id']} → $status");
            return $this->aTask(['id' => $t['id']]);
        });
    }

    /** Отметить пункт чек-листа (исполнитель или автор). */
    private function aTaskCheck(array $req): array
    {
        $t = $this->taskFor((int)($req['id'] ?? 0));
        $list = json_decode((string)$t['checklist'], true) ?: [];
        $i = (int)($req['index'] ?? -1);
        if (!isset($list[$i])) throw new ApiError('Нет такого пункта');
        $list[$i]['done'] = !empty($req['done']);
        $this->db->run('UPDATE tasks SET checklist = ?, updated_at = ? WHERE id = ?', [self::json($list), $this->now(), $t['id']]);
        return $this->aTask(['id' => $t['id']]);
    }

    /** Комментарий; участникам задачи и упомянутым через @Имя — уведомление. */
    private function aTaskComment(array $req): array
    {
        $t = $this->taskFor((int)($req['id'] ?? 0));
        $text = trim(mb_substr((string)($req['text'] ?? ''), 0, 3000));
        if ($text === '') throw new ApiError('Пустой комментарий');
        $this->db->insert('task_comments', ['task_id' => $t['id'], 'employee_id' => $this->me['id'], 'text' => $text, 'created_at' => $this->now()]);
        $to = [(int)$t['author_id'], (int)$t['assignee_id']];
        foreach ($this->db->all('SELECT id, name FROM employees WHERE active = 1') as $e) {
            $first = explode(' ', trim($e['name']))[0];
            if ($first !== '' && mb_stripos($text, '@' . $first) !== false) $to[] = (int)$e['id'];
        }
        foreach (array_unique($to) as $id) $this->notify($id, 'comment', 'Комментарий: ' . $t['title'], $this->me['name'] . ': ' . mb_substr($text, 0, 200), (string)$t['id']);
        return $this->aTask(['id' => $t['id']]);
    }

    private function filesDir(): string
    {
        $dir = (string)($this->config['files_dir'] ?? (sys_get_temp_dir() . '/ks-files'));
        if (!is_dir($dir)) @mkdir($dir, 0700, true);
        return $dir;
    }

    /** Файл к задаче (фото, документ, голос) — до 700 КБ, на диске зашифрован. */
    private function aFileUpload(array $req): array
    {
        $t = $this->taskFor((int)($req['taskId'] ?? 0));
        $bytes = base64_decode((string)($req['data'] ?? ''), true);
        if ($bytes === false || $bytes === '') throw new ApiError('Пустой файл');
        if (strlen($bytes) > 700_000) throw new ApiError('Файл больше 700 КБ — уменьшите фото');
        $name = trim(preg_replace('/[\\\\\/:*?"<>|]+/u', '_', (string)($req['name'] ?? 'file'))) ?: 'file';
        $mime = preg_match('~^[\w.+-]+/[\w.+-]+$~', (string)($req['mime'] ?? '')) ? (string)$req['mime'] : 'application/octet-stream';
        $path = bin2hex(random_bytes(16));
        if (file_put_contents($this->filesDir() . '/' . $path, $this->crypto->encrypt($bytes)) === false) throw new ApiError('Не удалось сохранить файл');
        $id = $this->db->insert('files', [
            'task_id' => $t['id'], 'owner_id' => $this->me['id'], 'name' => mb_substr($name, 0, 200), 'mime' => $mime,
            'size' => strlen($bytes), 'path' => $path, 'created_at' => $this->now(),
        ]);
        foreach ([(int)$t['author_id'], (int)$t['assignee_id']] as $who) $this->notify($who, 'file', 'Файл к задаче: ' . $t['title'], $name, (string)$t['id']);
        return ['id' => $id] + $this->aTask(['id' => $t['id']]);
    }

    private function aFileGet(array $req): array
    {
        $f = $this->db->one('SELECT * FROM files WHERE id = ?', [(int)($req['id'] ?? 0)]);
        if (!$f) throw new ApiError('Файл не найден');
        if ($f['task_id'] !== null) $this->taskFor((int)$f['task_id']);
        $raw = @file_get_contents($this->filesDir() . '/' . $f['path']);
        $bytes = $raw === false ? '' : $this->crypto->decrypt($raw);
        if ($bytes === '') throw new ApiError('Файл повреждён');
        return ['name' => $f['name'], 'mime' => $f['mime'], 'data' => base64_encode($bytes)];
    }

    /**
     * Новые уведомления (непрочитанные). Заодно — напоминания о сроках: за время «напомнить» и при просрочке
     * (каждой задаче — один раз за изменение срока).
     */
    private function aNotifications(array $req): array
    {
        $me = (int)$this->me['id'];
        foreach ($this->db->all("SELECT * FROM tasks WHERE assignee_id = ? AND status IN ('new', 'work') AND notified < 2 AND due_ms IS NOT NULL", [$me]) as $t) {
            $remind = $t['remind_ms'] !== null ? (int)$t['remind_ms'] : (int)$t['due_ms'] - 3600000;
            if ((int)$t['notified'] < 1 && $this->now() >= $remind && $this->now() < (int)$t['due_ms']) {
                $this->db->insert('notifications', ['employee_id' => $me, 'kind' => 'due', 'title' => 'Скоро срок: ' . $t['title'],
                    'body' => 'до ' . $this->fmt((int)$t['due_ms']), 'ref' => (string)$t['id'], 'created_at' => $this->now()]);
                $this->db->run('UPDATE tasks SET notified = 1 WHERE id = ?', [$t['id']]);
            } elseif ($this->now() >= (int)$t['due_ms']) {
                $this->db->insert('notifications', ['employee_id' => $me, 'kind' => 'late', 'title' => 'Просрочено: ' . $t['title'],
                    'body' => 'срок был ' . $this->fmt((int)$t['due_ms']), 'ref' => (string)$t['id'], 'created_at' => $this->now()]);
                $this->db->run('UPDATE tasks SET notified = 2 WHERE id = ?', [$t['id']]);
                $this->notifyAuthorLate($t);
            }
        }
        $rows = $this->db->all('SELECT * FROM notifications WHERE employee_id = ? AND read_at IS NULL ORDER BY id DESC LIMIT 50', [$me]);
        $open = $this->db->one("SELECT COUNT(*) AS n FROM tasks WHERE assignee_id = ? AND status IN ('new', 'work')", [$me]);
        return [
            'notifications' => array_map(fn($n) => ['id' => (int)$n['id'], 'kind' => $n['kind'], 'title' => $n['title'], 'body' => $n['body'],
                'ref' => $n['ref'], 'time' => (int)$n['created_at']], $rows),
            'openTasks' => (int)($open['n'] ?? 0),
        ];
    }

    private function notifyAuthorLate(array $t): void
    {
        if ((int)$t['author_id'] !== (int)$t['assignee_id']) {
            $this->db->insert('notifications', ['employee_id' => (int)$t['author_id'], 'kind' => 'late', 'title' => 'Просрочена задача: ' . $t['title'],
                'body' => 'исполнитель не успел к ' . $this->fmt((int)$t['due_ms']), 'ref' => (string)$t['id'], 'created_at' => $this->now()]);
        }
    }

    /** Прочитано: показанные телефоном уведомления больше не приходят. */
    private function aNotificationsRead(array $req): array
    {
        $ids = array_map('intval', array_slice((array)($req['ids'] ?? []), 0, 200));
        if ($ids) {
            $this->db->run('UPDATE notifications SET read_at = ? WHERE employee_id = ? AND read_at IS NULL AND id IN (' . implode(',', $ids) . ')', [$this->now(), $this->me['id']]);
        }
        return [];
    }
}
