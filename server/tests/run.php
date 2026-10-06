<?php
declare(strict_types=1);

// Тесты сервера: SQLite в памяти (в CI ещё и MySQL — DB_DSN), МойСклад — имитация.
foreach (['Db', 'Crypto', 'Rules', 'Api'] as $f) require_once __DIR__ . "/../src/$f.php";

$checks = 0;
function ok(bool $cond, string $what): void
{
    global $checks;
    $checks++;
    if (!$cond) {
        fwrite(STDERR, "НЕ ПРОЙДЕНО: $what\n");
        exit(1);
    }
}

$dsn = getenv('DB_DSN') ?: 'sqlite::memory:';
$pdo = new PDO($dsn, getenv('DB_USER') ?: null, getenv('DB_PASSWORD') ?: null);
if (str_starts_with($dsn, 'mysql')) {
    foreach (['settings', 'employees', 'sessions', 'shifts', 'jobs', 'stages', 'audit', 'login_fails', 'activity', 'events'] as $t) $pdo->exec("DROP TABLE IF EXISTS $t");
}
$db = new Ks\Db($pdo);
$db->migrate();
$db->migrate(); // повторно — без ошибок
$crypto = new Ks\Crypto(base64_encode(str_repeat("k", 32)));
$config = ['setup_key' => 'setup-123', 'ms_token' => 'x'];
$now = strtotime('2026-10-06 09:05:00 Europe/Moscow') * 1000;
$msCalls = [];
$ms = function (string $method, string $path, ?array $body) use (&$msCalls) {
    $msCalls[] = [$method, $path, $body];
    if ($path === '/entity/employee?limit=1000&offset=0') {
        return ['rows' => [
            ['id' => 'ms-emp-0001', 'firstName' => 'Анна', 'lastName' => 'Петрова', 'position' => 'Оператор', 'phone' => '+79990001122', 'email' => 'a@f.ru'],
            ['id' => 'ms-emp-0002', 'name' => 'Архивный', 'archived' => true],
        ]];
    }
    if (str_starts_with($path, '/audit?')) return ['rows' => [['uid' => 'admin@solver', 'moment' => '2026-10-06 09:00:00', 'eventType' => 'update', 'entityType' => 'product', 'objectCount' => 2]]];
    return [];
};
$api = function (string $ip = '10.0.0.1') use ($db, $crypto, $config, $ms, &$now) {
    return new Ks\Api($db, $crypto, $config, $ip, $ms, function () use (&$now) { return $now; });
};
$call = fn(array $req, string $ip = '10.0.0.1') => $api($ip)->handle($req);

// Шифрование: по базе телефон не прочитать, ключ — только отпечаток.
$box = $crypto->encrypt('+79990001122');
ok($box !== '+79990001122' && $crypto->decrypt($box) === '+79990001122', 'шифрование');
ok($crypto->decrypt('мусор') === '', 'битая запись не роняет сервер');
ok(strlen(Ks\Crypto::newKey()) === 24, 'длина ключа');

// Первый вход директора — один раз и только с ключом установки.
ok($call(['action' => 'setup', 'setup_key' => 'нет'])['ok'] === false, 'неверный ключ установки');
$dir = $call(['action' => 'setup', 'setup_key' => 'setup-123', 'name' => 'Басил']);
ok($dir['ok'] && strlen($dir['token']) === 64, 'директор создан');
ok($call(['action' => 'setup', 'setup_key' => 'setup-123'])['ok'] === false, 'второй раз нельзя');
$D = fn(array $r, string $ip = '10.0.0.1') => $call($r + ['token' => $dir['token']], $ip);
ok($D(['action' => 'me'])['me']['role'] === 'director', 'профиль директора');
ok($call(['action' => 'me', 'token' => 'чужой'])['error'] === 'Нужен вход', 'без входа нельзя');

// Сотрудник: ключ показывается один раз, телефон в базе зашифрован.
$op = $D(['action' => 'employeeSave', 'employee' => ['name' => 'Олег', 'role' => 'operator', 'phone' => '+7 900 111-22-33',
    'schedule' => ['days' => [1, 2, 3, 4, 5], 'start' => '09:00', 'end' => '18:00']]]);
ok($op['ok'] && strlen($op['key']) === 24, 'ключ сотрудника');
$raw = $db->one('SELECT phone_enc, key_hash FROM employees WHERE id = ?', [$op['employee']['id']]);
ok(!str_contains((string)$raw['phone_enc'], '900') && $raw['key_hash'] !== $op['key'], 'в базе нет открытого телефона и ключа');
ok($op['employee']['phone'] === '+7 900 111-22-33', 'директор видит телефон');
ok($D(['action' => 'employeeSave', 'employee' => ['name' => 'X', 'role' => 'хакер']])['ok'] === false, 'неизвестная роль');
$hw = $D(['action' => 'employeeSave', 'employee' => ['name' => 'Мария', 'role' => 'handwork']]);

// Вход сотрудника по ключу; 5 ошибок — пауза.
$opLogin = $call(['action' => 'login', 'key' => strtoupper($op['key']), 'device' => 'Redmi']);
ok($opLogin['ok'] && $opLogin['me']['role'] === 'operator', 'вход оператора');
for ($i = 0; $i < 5; $i++) $call(['action' => 'login', 'key' => 'неверный'], '10.9.9.9');
ok(str_contains($call(['action' => 'login', 'key' => $op['key']], '10.9.9.9')['error'], 'Подождите'), 'пауза после 5 ошибок');
$O = fn(array $r, string $ip = '10.0.0.1') => $call($r + ['token' => $opLogin['token']], $ip);
ok($O(['action' => 'employeeSave', 'employee' => ['name' => 'Y', 'role' => 'director']])['error'] === 'Нет доступа', 'оператор не создаёт сотрудников');
ok(!isset($O(['action' => 'employees'])['employees'][0]['phone']), 'оператор не видит телефоны');

// Wi-Fi фабрики: остальным — только с адреса фабрики, директору — откуда угодно.
ok(str_contains($D(['action' => 'settingsSave', 'allowed_ips' => '10.0.0.1, мусор'])['error'], 'мусор'), 'неверный адрес не сохраняется молча');
$D(['action' => 'settingsSave', 'allowed_ips' => '10.0.0.1, 10.5.0.0/16']);
ok($D(['action' => 'settings'])['allowed_ips'] === '10.0.0.1,10.5.0.0/16', 'адрес фабрики сохранён');
ok($O(['action' => 'me'], '10.5.7.9')['ok'], 'сеть фабрики (CIDR)');
ok($O(['action' => 'me'], '8.8.8.8')['error'] === 'Работа только через Wi-Fi фабрики', 'вне фабрики — нельзя');
ok($O(['action' => 'me'])['ok'], 'в сети фабрики — можно');
ok($D(['action' => 'me'], '8.8.8.8')['ok'], 'директор — откуда угодно');

// Производство: этапы только своей роли; кто начал, кто закончил, сколько минут.
$job = $D(['action' => 'jobSave', 'job' => ['title' => 'КП-12 Подвязы 600', 'client' => 'ООО Ромашка', 'quantity' => 600]]);
ok($O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'spec'])['error'] === 'Этот этап ведёт другой участок', 'ТЗ — не оператор');
$st = $O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'knit']);
ok($st['ok'], 'оператор начал вязание');
ok($O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'knit'])['error'] === 'Этап уже идёт', 'дважды не начать');
$now += 95 * 60000;
$fin = $O(['action' => 'stageFinish', 'id' => $st['id'], 'quantity' => 600]);
ok($fin['ok'] && $fin['minutes'] === 95, 'вязание: 95 минут');
$jobs = $D(['action' => 'jobs'])['jobs'];
ok($jobs[0]['stages'][0]['startedBy'] === 'Олег' && $jobs[0]['stages'][0]['quantity'] === 600, 'кто и сколько');
// Выработку нельзя накрутить: этап сделан полностью — новый не начать; больше остатка — нельзя; чужой этап — нельзя.
ok($O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'knit'])['error'] === 'Этап уже выполнен полностью', 'вязание уже сделано');
$op2 = $D(['action' => 'employeeSave', 'employee' => ['name' => 'Игорь', 'role' => 'operator']]);
$I = fn(array $r) => $call($r + ['token' => $call(['action' => 'login', 'key' => $op2['key']])['token']]);
$job2 = $D(['action' => 'jobSave', 'job' => ['title' => 'КП-13', 'quantity' => 100, 'deadline' => 1900000000000, 'quoteId' => 'q-13']]);
$s2 = $O(['action' => 'stageStart', 'job_id' => $job2['id'], 'stage' => 'pack']);
ok($I(['action' => 'stageFinish', 'id' => $s2['id'], 'quantity' => 10])['error'] === 'Этап начал другой сотрудник', 'чужой этап не завершить');
ok($O(['action' => 'stageFinish', 'id' => $s2['id']])['error'] === 'Укажите количество', 'без количества нельзя');
ok(str_contains($O(['action' => 'stageFinish', 'id' => $s2['id'], 'quantity' => 0])['error'], 'причину'), 'ноль — только с причиной');
ok(str_contains($O(['action' => 'stageFinish', 'id' => $s2['id'], 'quantity' => 101])['error'], '100'), 'не больше заказа');
ok(str_contains($D(['action' => 'jobSave', 'job' => ['id' => $job2['id'], 'done' => true]])['error'], 'незавершённые'), 'с идущим этапом заказ не закрыть');
ok($O(['action' => 'stageFinish', 'id' => $s2['id'], 'quantity' => 40])['ok'], 'упаковано 40');
ok($O(['action' => 'stageFinish', 'id' => $s2['id'], 'quantity' => 40])['ok'] === false, 'дважды не завершить');
ok($D(['action' => 'jobSave', 'job' => ['id' => $job2['id'], 'done' => true]])['ok'], 'заказ закрыт');
$j2 = $db->one('SELECT * FROM jobs WHERE id = ?', [$job2['id']]);
ok((int)$j2['deadline'] === 1900000000000 && $j2['quote_id'] === 'q-13', 'закрытие не стирает срок и КП');
ok($O(['action' => 'stageStart', 'job_id' => $job2['id'], 'stage' => 'qc'])['error'] === 'Заказ не найден или закрыт', 'в закрытом заказе этап не начать');

// Оператор помогает на ВТО; директор меняет распределение этапов.
ok($O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'wto'])['ok'], 'оператор помогает на ВТО');
$D(['action' => 'settingsSave', 'stage_roles' => ['wto' => ['handwork']]]);
ok($O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'qc'])['ok'], 'ОТК по-прежнему можно');
ok(!in_array('operator', $D(['action' => 'settings'])['stage_roles']['wto'], true), 'ВТО теперь только ручная работа');

// Смены: время сервера и телефона; чужую смену правит только директор.
ok($O(['action' => 'shiftSave', 'shift' => ['id' => 'sh-oleg-1', 'start' => $now - 3 * 3600000]])['ok'], 'смена с подведёнными часами');
ok($O(['action' => 'shiftSave', 'shift' => ['id' => 'sh-oleg-2', 'start' => $now]])['ok'], 'смена вовремя');
$shifts = $D(['action' => 'shifts', 'month' => '2026-10'])['shifts'];
ok($shifts[0]['suspicious'] === true && $shifts[1]['suspicious'] === false, 'подведённые часы видны директору');
$hwLogin = $call(['action' => 'login', 'key' => $hw['key']]);
ok($call(['action' => 'shiftSave', 'token' => $hwLogin['token'], 'shift' => ['id' => 'sh-oleg-1', 'start' => 1, 'end' => 2]])['error'] === 'Это смена другого сотрудника', 'чужая смена');
ok(str_contains($O(['action' => 'shiftSave', 'shift' => ['id' => 'sh-oleg-2', 'start' => $now - 3600000]])['error'], 'директор'), 'начало смены не переписать');
ok($O(['action' => 'shiftSave', 'shift' => ['id' => 'sh-oleg-2', 'start' => $now, 'end' => $now + 8 * 3600000]])['ok'], 'смена закрыта');
ok(str_contains($O(['action' => 'shiftSave', 'shift' => ['id' => 'sh-oleg-2', 'start' => $now, 'end' => $now + 12 * 3600000]])['error'], 'закрыта'), 'закрытую смену не продлить');
ok($D(['action' => 'shiftSave', 'shift' => ['id' => 'sh-oleg-2', 'start' => $now, 'end' => $now + 9 * 3600000]])['ok'], 'директор исправил смену');

// Рейтинг: выработка + выход по графику + вовремя; сотрудник видит подробности только свои.
$rating = $D(['action' => 'rating', 'month' => '2026-10'])['rating'];
ok($rating[0]['name'] === 'Олег' && $rating[0]['output'] >= 3 && $rating[0]['present'] === 1, 'рейтинг: Олег первый');
$mine = $call(['action' => 'rating', 'token' => $hwLogin['token'], 'month' => '2026-10'])['rating'];
$own = array_values(array_filter($mine, fn($r) => $r['id'] === $hw['employee']['id']))[0];
ok(!isset($mine[0]['output']) && isset($own['planned']), 'чужие подробности скрыты');

// МойСклад: сотрудники добавляются без доступа; карточка правится только директором.
$imp = $D(['action' => 'msEmployeesImport']);
ok($imp['added'] === 1, 'импорт сотрудников МойСклад');
ok($D(['action' => 'msAudit', 'days' => 1])['events'][0]['entity'] === 'product', 'журнал МойСклад');
$anna = $db->one("SELECT * FROM employees WHERE ms_id = 'ms-emp-0001'");
ok($anna['name'] === 'Петрова Анна' && (int)$anna['active'] === 0 && $anna['key_hash'] === null, 'из МойСклад — без доступа');
ok($D(['action' => 'msEmployeesImport'])['updated'] === 1, 'повторный импорт обновляет');
ok($D(['action' => 'msEmployeeSave', 'ms_id' => 'ms-emp-0001', 'card' => ['position' => 'Старший оператор', 'evil' => 1]])['saved'] === ['position'], 'карточка МойСклад');
ok(end($msCalls)[1] === '/entity/employee/ms-emp-0001' && end($msCalls)[2] === ['position' => 'Старший оператор'], 'в МойСклад ушла только должность');
ok($O(['action' => 'msEmployeeSave', 'ms_id' => 'ms-emp-0001', 'card' => ['position' => 'x']])['error'] === 'Нет доступа', 'оператор не правит МойСклад');

// Новый ключ — старые входы не работают; журнал — директору.
$D(['action' => 'employeeKey', 'id' => $op['employee']['id']]);
ok($O(['action' => 'me'])['error'] === 'Нужен вход', 'старый вход закрыт');
$audit = $D(['action' => 'audit', 'from' => 0])['audit'];
ok(count($audit) > 5 && $audit[0]['action'] === 'employeeKey', 'журнал действий');
ok($call(['action' => '../etc'])['error'] === 'Неизвестное действие', 'неизвестное действие');

// График: неверные значения заменяются стандартом.
ok(Ks\Rules::schedule('{"days":[1,9],"start":"25:00"}') === ['days' => [1], 'start' => '09:00', 'end' => '18:00'], 'проверка графика');

// Изменение сотрудника — только переданные поля (телефон не стирается).
$D(['action' => 'employeeSave', 'employee' => ['id' => $hw['employee']['id'], 'phone' => '+7 911 000-00-00']]);
$D(['action' => 'employeeSave', 'employee' => ['id' => $hw['employee']['id'], 'name' => 'Мария И.']]);
$m = array_values(array_filter($D(['action' => 'employees'])['employees'], fn($e) => $e['id'] === $hw['employee']['id']))[0];
ok($m['name'] === 'Мария И.' && $m['phone'] === '+7 911 000-00-00' && $m['role'] === 'handwork', 'частичное сохранение');
ok(!isset($call(['action' => 'employees', 'token' => $hwLogin['token']])['employees'][0]['schedule']), 'графики чужих скрыты');

// Активность офиса: товаровед в рабочее время отмечается раз в минуту; действия видны директору.
$mr = $D(['action' => 'employeeSave', 'employee' => ['name' => 'Ольга', 'role' => 'merch']]);
$mrTok = $call(['action' => 'login', 'key' => $mr['key']])['token'];
$M = fn(array $r) => $call($r + ['token' => $mrTok]);
ok($M(['action' => 'me'])['tracked'] === true, 'товаровед — учёт активности');
ok($I(['action' => 'ping'])['tracked'] === false, 'производство не отслеживается');
$now = strtotime('2026-10-07 10:00:00 Europe/Moscow') * 1000;
for ($i = 0; $i < 5; $i++) { $M(['action' => 'ping']); $now += 60000; }
$now += 45 * 60000;
$M(['action' => 'ping']);
ok($M(['action' => 'event', 'kind' => 'product', 'detail' => 'Подвязы — фото'])['ok'], 'действие записано');
ok($M(['action' => 'event', 'kind' => 'взлом'])['ok'] === false, 'неизвестное действие не пишется');
$actv = $D(['action' => 'activity', 'days' => 1]);
$olga = array_values(array_filter($actv['people'], fn($p) => $p['name'] === 'Ольга'))[0];
ok($olga['minutes'] >= 5 && $olga['sessions'] === 2 && $olga['maxGapMinutes'] >= 45 && $olga['actions']['product'] === 1, 'активность: минуты, заходы, перерыв');
ok($actv['feed'][0]['who'] === 'Ольга', 'лента действий');
ok($M(['action' => 'activity'])['error'] === 'Нет доступа', 'активность видит руководство');
$now = strtotime('2026-10-07 23:30:00 Europe/Moscow') * 1000;
ok($M(['action' => 'ping'])['tracked'] === false, 'вне рабочего времени не отслеживается');

// График 2/2 от опорной даты.
$cyc = Ks\Rules::schedule('{"type":"cycle","anchor":"2026-10-01","on":2,"off":2,"start":"08:00","end":"20:00"}');
ok(Ks\Rules::worksOn($cyc, '2026-10-02') && !Ks\Rules::worksOn($cyc, '2026-10-03') && Ks\Rules::worksOn($cyc, '2026-10-05'), 'график 2/2');
ok(Ks\Rules::shiftMinutes(Ks\Rules::schedule('{"days":[1],"start":"20:00","end":"08:00"}')) === 720, 'ночная смена');

// Выход: токен больше не работает; старая сессия истекает.
ok($M(['action' => 'logout'])['ok'] && $M(['action' => 'me'])['error'] === 'Нужен вход', 'выход отзывает токен');
$now += 31 * 86400000;
ok($call(['action' => 'me', 'token' => $hwLogin['token']])['error'] === 'Нужен вход', 'сессия истекает после 30 дней без работы');

// Установка после создания директора недоступна даже с верным ключом; блок проверяется до ключа.
for ($i = 0; $i < 5; $i++) $call(['action' => 'login', 'key' => 'нет'], '10.7.7.7');
ok(str_contains($call(['action' => 'setup', 'setup_key' => 'setup-123'], '10.7.7.7')['error'], 'Подождите'), 'setup тоже под паузой');

echo "Сервер: все проверки пройдены ($checks)\n";
