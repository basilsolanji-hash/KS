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

    public static function full(string $role): bool
    {
        return $role === 'director' || $role === 'assistant';
    }

    public static function canStage(string $role, string $stage, array $stageRoles): bool
    {
        if (self::full($role)) return true;
        return in_array($role, $stageRoles[$stage] ?? [], true);
    }

    /** График: дни недели 1–7 (пн–вс), начало и конец «ЧЧ:ММ». */
    public static function schedule(?string $json): array
    {
        $s = $json ? json_decode($json, true) : null;
        if (!is_array($s)) return self::DEFAULT_SCHEDULE;
        $days = array_values(array_filter(array_map('intval', (array)($s['days'] ?? [])), fn($d) => $d >= 1 && $d <= 7));
        $time = fn($t, $d) => (is_string($t) && preg_match('/^([01]\d|2[0-3]):[0-5]\d$/', $t)) ? $t : $d;
        return ['days' => $days, 'start' => $time($s['start'] ?? null, '09:00'), 'end' => $time($s['end'] ?? null, '18:00')];
    }

    /**
     * Рейтинг за месяц: выработка (этапы × вес), выход по графику, приход вовремя.
     * Производство: 50 % выработка + 30 % выход + 20 % вовремя; остальные: 60 % выход + 40 % вовремя.
     *
     * @param list<array{id:int,name:string,role:string,schedule:array}> $people
     * @param list<array{employee_id:int,start_ms:int}> $shifts
     * @param list<array{finished_by:int,stage:string}> $stages
     */
    public static function rating(array $people, array $shifts, array $stages, int $monthStart, int $monthEnd, int $now, int $lateMinutes, \DateTimeZone $tz): array
    {
        $until = min($monthEnd, $now);
        $output = [];
        foreach ($stages as $s) {
            $output[$s['finished_by']] = ($output[$s['finished_by']] ?? 0) + (self::STAGE_WEIGHT[$s['stage']] ?? 1);
        }
        $best = max([1, ...array_values($output)]);
        $rows = [];
        foreach ($people as $p) {
            $sched = $p['schedule'];
            // Рабочие дни по графику с начала месяца до сегодня.
            $planned = 0;
            for ($t = $monthStart; $t < $until; $t += 86400000) {
                $d = (new \DateTimeImmutable('@' . intdiv($t, 1000)))->setTimezone($tz);
                if (in_array((int)$d->format('N'), $sched['days'], true)) $planned++;
            }
            // Первый вход каждого дня.
            $firstByDay = [];
            foreach ($shifts as $sh) {
                if ($sh['employee_id'] !== $p['id'] || $sh['start_ms'] < $monthStart || $sh['start_ms'] >= $until) continue;
                $day = (new \DateTimeImmutable('@' . intdiv($sh['start_ms'], 1000)))->setTimezone($tz);
                $key = $day->format('Y-m-d');
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
            $production = in_array($p['role'], ['designer', 'operator', 'handwork'], true);
            $out = $output[$p['id']] ?? 0;
            $score = $production
                ? 0.5 * ($out / $best) + 0.3 * $attendance + 0.2 * $punctual
                : 0.6 * $attendance + 0.4 * $punctual;
            $rows[] = [
                'id' => $p['id'], 'name' => $p['name'], 'role' => $p['role'],
                'planned' => $planned, 'present' => $present, 'onTime' => $onTime, 'output' => $out,
                'attendance' => round($attendance * 100), 'punctuality' => round($punctual * 100), 'score' => (int)round($score * 100),
            ];
        }
        usort($rows, fn($a, $b) => $b['score'] <=> $a['score'] ?: strcmp($a['name'], $b['name']));
        return $rows;
    }
}
