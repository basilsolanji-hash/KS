<?php
declare(strict_types=1);

namespace Ks;

/** Роли, этапы производства и расчёт рейтинга — без базы (легко проверить тестами). */
final class Rules
{
    /** Роли: директор видит всё; остальные — своё. */
    public const ROLES = [
        'director' => 'Директор',
        'assistant' => 'Помощник директора',
        'accountant' => 'Бухгалтер',
        'manager' => 'Менеджер',
        'merch' => 'Товаровед',
        'designer' => 'Дессинатор',
        'operator' => 'Оператор',
        'handwork' => 'Ручная работа',
    ];

    /** Этапы производства по порядку (цвета — в приложении). */
    public const STAGES = [
        'yarn' => 'Подготовка пряжи',
        'spec' => 'Техническое задание',
        'setup' => 'Заправка — запуск',
        'knit' => 'Вязание',
        'coupons' => 'Разделение купонов',
        'wto' => 'ВТО',
        'qc' => 'ОТК',
        'pack' => 'Упаковка — маркировка',
    ];

    /**
     * Кто ведёт этап (директор и помощник — любой). Оператор может помогать на ручных операциях.
     * Директор меняет распределение в настройках (stage_roles).
     */
    public const STAGE_ROLES = [
        'yarn' => ['designer', 'operator'],
        'spec' => ['designer'],
        'setup' => ['designer'],
        'knit' => ['operator'],
        'coupons' => ['handwork', 'operator'],
        'wto' => ['handwork', 'operator'],
        'qc' => ['handwork', 'operator'],
        'pack' => ['handwork', 'operator'],
    ];

    /** Вес этапа в выработке (вязание и заправка — сложнее). */
    public const STAGE_WEIGHT = ['yarn' => 1, 'spec' => 1, 'setup' => 2, 'knit' => 3, 'coupons' => 1, 'wto' => 1, 'qc' => 1, 'pack' => 1];

    public const DEFAULT_SCHEDULE = ['days' => [1, 2, 3, 4, 5], 'start' => '09:00', 'end' => '18:00'];

    /** Производство: рейтинг по выработке; офис (заказы, клиенты, товары) — по активности в приложении. */
    public const PRODUCTION = ['designer', 'operator', 'handwork'];
    public const OFFICE = ['assistant', 'accountant', 'manager', 'merch'];

    public static function full(string $role): bool
    {
        return $role === 'director' || $role === 'assistant';
    }

    public static function canStage(string $role, string $stage, array $stageRoles): bool
    {
        if (self::full($role)) return true;
        return in_array($role, $stageRoles[$stage] ?? [], true);
    }

    /**
     * График: дни недели 1–7 (пн–вс) или цикл «N через M» от опорной даты (2/2 и т. п.); начало и конец «ЧЧ:ММ».
     * Конец раньше начала — ночная смена (до утра следующего дня).
     */
    public static function schedule(?string $json): array
    {
        $s = $json ? json_decode($json, true) : null;
        if (!is_array($s)) return self::DEFAULT_SCHEDULE;
        $time = fn($t, $d) => (is_string($t) && preg_match('/^([01]\d|2[0-3]):[0-5]\d$/', $t)) ? $t : $d;
        $out = ['days' => [], 'start' => $time($s['start'] ?? null, '09:00'), 'end' => $time($s['end'] ?? null, '18:00')];
        $anchor = (string)($s['anchor'] ?? '');
        $on = (int)($s['on'] ?? 0);
        $off = (int)($s['off'] ?? 0);
        if (($s['type'] ?? '') === 'cycle' && preg_match('/^\d{4}-\d{2}-\d{2}$/', $anchor) && $on >= 1 && $on <= 14 && $off >= 0 && $off <= 14) {
            return ['type' => 'cycle', 'anchor' => $anchor, 'on' => $on, 'off' => $off] + $out;
        }
        $out['days'] = array_values(array_unique(array_filter(array_map('intval', (array)($s['days'] ?? [])), fn($d) => $d >= 1 && $d <= 7)));
        return $out;
    }

    /** Рабочий ли день по графику (дата «ГГГГ-ММ-ДД»). */
    public static function worksOn(array $sched, string $ymd): bool
    {
        if (($sched['type'] ?? '') === 'cycle') {
            $days = intdiv(strtotime($ymd . ' 12:00 UTC') - strtotime($sched['anchor'] . ' 12:00 UTC'), 86400);
            $len = $sched['on'] + $sched['off'];
            return (($days % $len) + $len) % $len < $sched['on'];
        }
        return in_array((int)date('N', strtotime($ymd . ' 12:00 UTC')), $sched['days'], true);
    }

    /** Длительность смены по графику в минутах (ночная — через полночь). */
    public static function shiftMinutes(array $sched): int
    {
        [$h1, $m1] = array_map('intval', explode(':', $sched['start']));
        [$h2, $m2] = array_map('intval', explode(':', $sched['end']));
        $d = ($h2 * 60 + $m2) - ($h1 * 60 + $m1);
        return $d > 0 ? $d : $d + 1440;
    }

    /**
     * Рейтинг за месяц.
     * Производство: 50 % выработка + 30 % выход + 20 % вовремя.
     * Офис: 40 % активность в приложении + 35 % выход + 25 % вовремя.
     * Остальные: 60 % выход + 40 % вовремя.
     * Выработка — вес этапа × доля заказа (количество / тираж): дробление на мелкие этапы не прибавляет баллы.
     * Сегодня считается плановым днём только после начала смены + опоздание.
     *
     * @param list<array{id:int,name:string,role:string,schedule:array}> $people
     * @param list<array{employee_id:int,start_ms:int}> $shifts
     * @param list<array{finished_by:int,stage:string,quantity?:int,job_quantity?:int,job_id?:int}> $stages
     * @param array<int,int> $activeMinutes минуты в приложении за месяц
     */
    public static function rating(array $people, array $shifts, array $stages, int $monthStart, int $monthEnd, int $now, int $lateMinutes, \DateTimeZone $tz, array $activeMinutes = []): array
    {
        $until = min($monthEnd, $now);
        // Выработка: по каждому человеку, заказу и этапу — не больше полного веса этапа.
        $share = [];
        foreach ($stages as $s) {
            $jobQty = (int)($s['job_quantity'] ?? 0);
            $part = $jobQty > 0 ? min(1.0, max(0, (int)($s['quantity'] ?? 0)) / $jobQty) : 1.0;
            $k = $s['finished_by'] . '|' . ($s['job_id'] ?? 0) . '|' . $s['stage'];
            $share[$k] = min(1.0, ($share[$k] ?? 0) + $part);
        }
        $output = [];
        foreach ($share as $k => $part) {
            [$who, , $stage] = explode('|', $k);
            $output[(int)$who] = ($output[(int)$who] ?? 0) + $part * (self::STAGE_WEIGHT[$stage] ?? 1);
        }
        $productionIds = array_map(fn($p) => $p['id'], array_filter($people, fn($p) => in_array($p['role'], self::PRODUCTION, true)));
        $best = max([1, ...array_map(fn($id) => $output[$id] ?? 0, $productionIds)]);
        $rows = [];
        foreach ($people as $p) {
            $sched = $p['schedule'];
            // Рабочие дни по графику с начала месяца до сегодня (сегодня — после начала смены с допуском).
            $planned = 0;
            for ($t = $monthStart; $t < $until; $t += 86400000) {
                $ymd = (new \DateTimeImmutable('@' . intdiv($t, 1000)))->setTimezone($tz)->format('Y-m-d');
                if (!self::worksOn($sched, $ymd)) continue;
                $begin = (new \DateTimeImmutable($ymd . ' ' . $sched['start'], $tz))->getTimestamp() * 1000 + $lateMinutes * 60000;
                if ($begin > $now) continue;
                $planned++;
            }
            // Первый вход каждого дня.
            $firstByDay = [];
            foreach ($shifts as $sh) {
                if ($sh['employee_id'] !== $p['id'] || $sh['start_ms'] < $monthStart || $sh['start_ms'] > $until) continue;
                $key = (new \DateTimeImmutable('@' . intdiv($sh['start_ms'], 1000)))->setTimezone($tz)->format('Y-m-d');
                if (!isset($firstByDay[$key]) || $sh['start_ms'] < $firstByDay[$key]) $firstByDay[$key] = $sh['start_ms'];
            }
            $present = count($firstByDay);
            $onTime = 0;
            foreach ($firstByDay as $key => $start) {
                $limit = (new \DateTimeImmutable($key . ' ' . $sched['start'], $tz))->getTimestamp() * 1000 + $lateMinutes * 60000;
                if ($start <= $limit) $onTime++;
            }
            $attendance = $planned > 0 ? min(1.0, $present / $planned) : ($present > 0 ? 1.0 : 0.0);
            $punctual = $present > 0 ? $onTime / $present : 0.0;
            $out = round($output[$p['id']] ?? 0, 1);
            $minutes = (int)($activeMinutes[$p['id']] ?? 0);
            // Активность: 70 % рабочего времени в приложении — уже максимум.
            $activity = $planned > 0 ? min(1.0, $minutes / ($planned * self::shiftMinutes($sched) * 0.7)) : 0.0;
            if (in_array($p['role'], self::PRODUCTION, true)) {
                $score = 0.5 * (($output[$p['id']] ?? 0) / $best) + 0.3 * $attendance + 0.2 * $punctual;
            } elseif (in_array($p['role'], self::OFFICE, true)) {
                $score = 0.4 * $activity + 0.35 * $attendance + 0.25 * $punctual;
            } else {
                $score = 0.6 * $attendance + 0.4 * $punctual;
            }
            $rows[] = [
                'id' => $p['id'], 'name' => $p['name'], 'role' => $p['role'],
                'planned' => $planned, 'present' => $present, 'onTime' => $onTime, 'output' => $out,
                'attendance' => round($attendance * 100), 'punctuality' => round($punctual * 100),
                'activity' => in_array($p['role'], self::OFFICE, true) ? round($activity * 100) : null, 'activeMinutes' => $minutes,
                'score' => (int)round($score * 100),
            ];
        }
        usort($rows, fn($a, $b) => $b['score'] <=> $a['score'] ?: strcmp($a['name'], $b['name']));
        return $rows;
    }
}
