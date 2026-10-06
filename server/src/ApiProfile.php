<?php
declare(strict_types=1);

namespace Ks;

/**
 * Профиль сотрудника (личные данные зашифрованы, фото, сканы документов), соглашения при первом входе,
 * выплаты (начисления и выплаты по месяцам, расчёт за месяц).
 */
trait ApiProfile
{
    private static array $PROFILE_FIELDS = ['birthday', 'phone', 'email', 'address', 'snils', 'inn', 'passportSeries', 'passportNumber',
        'passportIssuedBy', 'passportIssuedAt', 'passportCode', 'card', 'bank', 'emergencyName', 'emergencyPhone'];
    private static array $DOC_KINDS = ['passport', 'passport2', 'snils', 'inn', 'contract', 'other'];
    private static array $PAY_KINDS = ['salary' => 1, 'percent' => 1, 'bonus' => 1, 'fine' => -1, 'advance' => 0, 'payout' => 0];

    /** Кадры: директор, помощник, бухгалтер. */
    private function hr(): bool
    {
        return Rules::full($this->me['role']) || $this->me['role'] === 'accountant';
    }

    private function profileOf(int $id): array
    {
        if ($id !== (int)$this->me['id'] && !$this->hr()) throw new ApiError('Нет доступа');
        if (!$this->db->one('SELECT id FROM employees WHERE id = ?', [$id])) throw new ApiError('Сотрудник не найден');
        return $this->db->one('SELECT * FROM profiles WHERE employee_id = ?', [$id]) ?? ['employee_id' => $id, 'data_enc' => null, 'photo_file' => null,
            'pay_visible' => 0, 'confirmed_by' => null, 'confirmed_at' => null, 'updated_at' => 0];
    }

    private function saveProfileRow(array $p): void
    {
        $have = $this->db->one('SELECT employee_id FROM profiles WHERE employee_id = ?', [$p['employee_id']]);
        $row = array_intersect_key($p, array_flip(['data_enc', 'photo_file', 'pay_visible', 'confirmed_by', 'confirmed_at'])) + ['updated_at' => $this->now()];
        if ($have) {
            $sets = implode(', ', array_map(fn($c) => "$c = ?", array_keys($row)));
            $this->db->run("UPDATE profiles SET $sets WHERE employee_id = ?", [...array_values($row), $p['employee_id']]);
        } else {
            $this->db->insert('profiles', $row + ['employee_id' => $p['employee_id']]);
        }
    }

    private function aProfile(array $req): array
    {
        $id = (int)($req['id'] ?? $this->me['id']);
        $p = $this->profileOf($id);
        $e = $this->db->one('SELECT name, role, position FROM employees WHERE id = ?', [$id]);
        $docs = $this->hr() || $id === (int)$this->me['id']
            ? $this->db->all('SELECT d.id, d.kind, d.file_id, f.name, f.size, d.created_at FROM employee_docs d JOIN files f ON f.id = d.file_id WHERE d.employee_id = ? ORDER BY d.id', [$id])
            : [];
        return [
            'profile' => [
                'id' => $id, 'name' => $e['name'], 'role' => $e['role'], 'position' => $e['position'] ?? '',
                'fields' => json_decode($this->crypto->decrypt($p['data_enc']), true) ?: new \stdClass(),
                'photo' => $p['photo_file'] !== null ? (int)$p['photo_file'] : null, 'payVisible' => (bool)$p['pay_visible'],
                'confirmed' => $p['confirmed_at'] !== null, 'confirmedAt' => $p['confirmed_at'] !== null ? (int)$p['confirmed_at'] : null,
            ],
            'docs' => array_map(fn($d) => ['id' => (int)$d['id'], 'kind' => $d['kind'], 'fileId' => (int)$d['file_id'], 'name' => $d['name'],
                'size' => (int)$d['size'], 'time' => (int)$d['created_at']], $docs),
        ];
    }

    /** Сотрудник заполняет сам (подтверждение сбрасывается), кадры — любого. */
    private function aProfileSave(array $req): array
    {
        $id = (int)($req['id'] ?? $this->me['id']);
        $p = $this->profileOf($id);
        $old = json_decode($this->crypto->decrypt($p['data_enc']), true) ?: [];
        $in = (array)($req['fields'] ?? []);
        $data = $old;
        foreach (self::$PROFILE_FIELDS as $f) {
            if (array_key_exists($f, $in)) $data[$f] = mb_substr(trim((string)$in[$f]), 0, 300);
        }
        if (!empty($data['inn']) && !preg_match('/^\d{12}$/', $data['inn'])) throw new ApiError('ИНН физлица — 12 цифр');
        if (!empty($data['snils']) && strlen(preg_replace('/\D/', '', $data['snils'])) !== 11) throw new ApiError('СНИЛС — 11 цифр');
        if (!empty($data['birthday']) && !preg_match('/^\d{2}\.\d{2}\.\d{4}$/', $data['birthday'])) throw new ApiError('Дата рождения: ДД.ММ.ГГГГ');
        $p['data_enc'] = $this->crypto->encrypt(self::json($data));
        if ($id === (int)$this->me['id'] && $this->me['role'] !== 'director') {
            $p['confirmed_by'] = null;
            $p['confirmed_at'] = null;
        }
        $this->saveProfileRow($p);
        $this->log('profileSave', (string)$id);
        if ($id === (int)$this->me['id'] && $this->me['role'] !== 'director') {
            foreach ($this->db->all("SELECT id FROM employees WHERE role = 'director' AND active = 1") as $d) {
                $this->notify((int)$d['id'], 'profile', 'Профиль изменён: ' . $this->me['name'], 'проверьте и подтвердите', '');
            }
        }
        return $this->aProfile(['id' => $id]);
    }

    private function aProfileConfirm(array $req): array
    {
        $this->need();
        $id = (int)($req['id'] ?? 0);
        $p = $this->profileOf($id);
        $p['confirmed_by'] = $this->me['id'];
        $p['confirmed_at'] = $this->now();
        $this->saveProfileRow($p);
        $this->log('profileConfirm', (string)$id);
        $this->notify($id, 'profile', 'Профиль подтверждён', (string)$this->me['name'], '');
        return $this->aProfile(['id' => $id]);
    }

    /** Сохранить файл (фото, скан): зашифрован на диске, до 700 КБ. */
    private function storeFile(string $data, string $name, string $mime): int
    {
        $bytes = base64_decode($data, true);
        if ($bytes === false || $bytes === '') throw new ApiError('Пустой файл');
        if (strlen($bytes) > 700_000) throw new ApiError('Файл больше 700 КБ — уменьшите фото');
        $path = bin2hex(random_bytes(16));
        if (file_put_contents($this->filesDir() . '/' . $path, $this->crypto->encrypt($bytes)) === false) throw new ApiError('Не удалось сохранить файл');
        return $this->db->insert('files', [
            'task_id' => null, 'owner_id' => $this->me['id'], 'name' => mb_substr(trim(preg_replace('/[\\\\\/:*?"<>|]+/u', '_', $name)) ?: 'file', 0, 200),
            'mime' => preg_match('~^[\w.+-]+/[\w.+-]+$~', $mime) ? $mime : 'application/octet-stream',
            'size' => strlen($bytes), 'path' => $path, 'created_at' => $this->now(),
        ]);
    }

    private function aProfilePhoto(array $req): array
    {
        $id = (int)($req['id'] ?? $this->me['id']);
        $p = $this->profileOf($id);
        $p['photo_file'] = $this->storeFile((string)($req['data'] ?? ''), 'photo.jpg', 'image/jpeg');
        $this->saveProfileRow($p);
        return $this->aProfile(['id' => $id]);
    }

    /** Скан документа: загружает сам сотрудник или кадры; смотрят только кадры (и сам сотрудник). */
    private function aProfileDoc(array $req): array
    {
        $id = (int)($req['id'] ?? $this->me['id']);
        $this->profileOf($id);
        $kind = (string)($req['kind'] ?? 'other');
        if (!in_array($kind, self::$DOC_KINDS, true)) $kind = 'other';
        $file = $this->storeFile((string)($req['data'] ?? ''), (string)($req['name'] ?? $kind), (string)($req['mime'] ?? 'image/jpeg'));
        $this->db->insert('employee_docs', ['employee_id' => $id, 'kind' => $kind, 'file_id' => $file, 'created_at' => $this->now()]);
        $this->log('profileDoc', "$id $kind");
        return $this->aProfile(['id' => $id]);
    }

    private function aProfileDocDelete(array $req): array
    {
        $d = $this->db->one('SELECT * FROM employee_docs WHERE id = ?', [(int)($req['docId'] ?? 0)]);
        if (!$d) throw new ApiError('Документ не найден');
        $this->profileOf((int)$d['employee_id']);
        $this->db->run('DELETE FROM employee_docs WHERE id = ?', [$d['id']]);
        $this->log('profileDocDelete', $d['employee_id'] . ' ' . $d['kind']);
        return $this->aProfile(['id' => (int)$d['employee_id']]);
    }

    /** Доступ к файлу без задачи: фото — всем; скан документа — владельцу и кадрам (просмотр — в журнал). */
    private function fileAccess(array $f): void
    {
        if ($this->db->one('SELECT employee_id FROM profiles WHERE photo_file = ?', [$f['id']])) return;
        $doc = $this->db->one('SELECT employee_id, kind FROM employee_docs WHERE file_id = ?', [$f['id']]);
        if ($doc) {
            if ((int)$doc['employee_id'] !== (int)$this->me['id'] && !$this->hr()) throw new ApiError('Нет доступа');
            $this->log('docView', $doc['employee_id'] . ' ' . $doc['kind']);
            return;
        }
        if ((int)$f['owner_id'] !== (int)$this->me['id'] && !$this->hr()) throw new ApiError('Нет доступа');
    }

    // ---------------------------------------------------------------- Соглашения

    private function aLegal(array $req): array
    {
        $accepted = array_column($this->db->all('SELECT doc FROM consents WHERE employee_id = ? AND version = ?', [$this->me['id'], Legal::VERSION]), 'doc');
        return ['version' => Legal::VERSION, 'docs' => array_map(fn($d) => $d + ['accepted' => in_array($d['key'], $accepted, true)], Legal::docs()),
            'allAccepted' => !array_diff(Legal::keys(), $accepted)];
    }

    private function aLegalAccept(array $req): array
    {
        foreach (Legal::keys() as $k) {
            if (!$this->db->one('SELECT id FROM consents WHERE employee_id = ? AND doc = ? AND version = ?', [$this->me['id'], $k, Legal::VERSION])) {
                $this->db->insert('consents', ['employee_id' => $this->me['id'], 'doc' => $k, 'version' => Legal::VERSION, 'accepted_at' => $this->now(), 'ip' => $this->ip]);
            }
        }
        $this->log('legalAccept', Legal::VERSION);
        return $this->aLegal([]);
    }

    // ---------------------------------------------------------------- Выплаты

    /** Расчёт за месяц: начислено (оклад, процент, премия − штраф), выплачено (аванс, выплата), остаток; работа за месяц. */
    private function aPayroll(array $req): array
    {
        $id = (int)($req['id'] ?? $this->me['id']);
        $self = $id === (int)$this->me['id'];
        if (!$this->hr()) {
            if (!$self) throw new ApiError('Нет доступа');
            $p = $this->db->one('SELECT pay_visible FROM profiles WHERE employee_id = ?', [$id]);
            if (!$p || !(int)$p['pay_visible']) throw new ApiError('Выплаты откроет директор');
        }
        [$from, $to] = $this->month((string)($req['month'] ?? ''));
        $m = (new \DateTimeImmutable('@' . intdiv($from, 1000)))->setTimezone(new \DateTimeZone('Europe/Moscow'))->format('Y-m');
        $rows = $this->db->all('SELECT * FROM payroll WHERE employee_id = ? AND month = ? ORDER BY id', [$id, $m]);
        $accrued = 0.0;
        $paid = 0.0;
        $items = [];
        foreach ($rows as $r) {
            $amount = (float)$this->crypto->decrypt($r['amount_enc']);
            $sign = self::$PAY_KINDS[$r['kind']] ?? 0;
            if ($sign !== 0) $accrued += $sign * $amount; else $paid += $amount;
            $items[] = ['id' => (int)$r['id'], 'kind' => $r['kind'], 'amount' => $amount, 'comment' => $r['comment'], 'time' => (int)$r['created_at']];
        }
        $shifts = $this->db->all('SELECT start_ms, end_ms FROM shifts WHERE employee_id = ? AND start_ms >= ? AND start_ms < ?', [$id, $from, $to]);
        $minutes = array_sum(array_map(fn($s) => $s['end_ms'] !== null ? intdiv((int)$s['end_ms'] - (int)$s['start_ms'], 60000) : 0, $shifts));
        $stages = (int)($this->db->one('SELECT COUNT(*) AS n FROM stages WHERE finished_by = ? AND finished_at >= ? AND finished_at < ?', [$id, $from, $to])['n'] ?? 0);
        $tasks = (int)($this->db->one("SELECT COUNT(*) AS n FROM tasks WHERE assignee_id = ? AND done_at >= ? AND done_at < ?", [$id, $from, $to])['n'] ?? 0);
        return [
            'month' => $m, 'items' => $items, 'accrued' => round($accrued, 2), 'paid' => round($paid, 2), 'balance' => round($accrued - $paid, 2),
            'work' => ['days' => count($shifts), 'hours' => round($minutes / 60, 1), 'stages' => $stages, 'tasks' => $tasks],
        ];
    }

    private function aPayrollSave(array $req): array
    {
        $this->need('accountant');
        $id = (int)($req['id'] ?? 0);
        if (!$this->db->one('SELECT id FROM employees WHERE id = ?', [$id])) throw new ApiError('Сотрудник не найден');
        $kind = (string)($req['kind'] ?? '');
        if (!isset(self::$PAY_KINDS[$kind])) throw new ApiError('Неизвестный вид');
        $amount = round((float)($req['amount'] ?? 0), 2);
        if ($amount <= 0 || $amount > 10_000_000) throw new ApiError('Сумма должна быть больше нуля');
        $m = (string)($req['month'] ?? '');
        if (!preg_match('/^\d{4}-(0[1-9]|1[0-2])$/', $m)) throw new ApiError('Неверный месяц');
        $this->db->insert('payroll', [
            'employee_id' => $id, 'month' => $m, 'kind' => $kind, 'amount_enc' => $this->crypto->encrypt((string)$amount),
            'comment' => mb_substr(trim((string)($req['comment'] ?? '')), 0, 300), 'created_by' => $this->me['id'], 'created_at' => $this->now(),
        ]);
        $this->log('payrollSave', "$id $m $kind");
        if (in_array($kind, ['advance', 'payout'], true)) $this->notify($id, 'pay', 'Выплата', number_format($amount, 2, ',', ' ') . ' ₽', '');
        return $this->aPayroll(['id' => $id, 'month' => $m]);
    }

    private function aPayrollDelete(array $req): array
    {
        $this->need('accountant');
        $r = $this->db->one('SELECT * FROM payroll WHERE id = ?', [(int)($req['rowId'] ?? 0)]);
        if (!$r) throw new ApiError('Запись не найдена');
        $this->db->run('DELETE FROM payroll WHERE id = ?', [$r['id']]);
        $this->log('payrollDelete', $r['employee_id'] . ' ' . $r['month'] . ' ' . $r['kind']);
        return $this->aPayroll(['id' => (int)$r['employee_id'], 'month' => $r['month']]);
    }

    /** Директор разрешает сотруднику видеть свои выплаты и историю работы. */
    private function aPayVisible(array $req): array
    {
        $this->need();
        $id = (int)($req['id'] ?? 0);
        $p = $this->profileOf($id);
        $p['pay_visible'] = !empty($req['visible']) ? 1 : 0;
        $this->saveProfileRow($p);
        $this->log('payVisible', "$id " . $p['pay_visible']);
        return $this->aProfile(['id' => $id]);
    }
}
