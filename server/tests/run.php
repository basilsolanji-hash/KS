<?php
declare(strict_types=1);

// Тесты сервера: SQLite в памяти (в CI ещё и MySQL — DB_DSN), МойСклад — имитация.
foreach (['Db', 'Crypto', 'Rules', 'Legal', 'ApiPeople', 'ApiWork', 'ApiOrders', 'ApiTasks', 'ApiProfile', 'ApiServer'] as $f) require_once __DIR__ . "/../src/$f.php";

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
    foreach (['settings', 'employees', 'sessions', 'shifts', 'jobs', 'stages', 'production_task_specs', 'tech_card_operations',
        'tech_card_versions', 'tech_cards', 'audit', 'login_fails', 'activity', 'events', 'tasks', 'task_comments', 'files',
        'notifications', 'profiles', 'employee_docs', 'consents', 'payroll', 'screen_stats'] as $t) $pdo->exec("DROP TABLE IF EXISTS $t");
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
    $ord = 'a1b2c3d4-0000-0000-0000-000000000001';
    if (str_starts_with($path, '/entity/customerorder?')) return ['meta' => ['size' => 1], 'rows' => [[
        'id' => $ord, 'name' => '00042', 'moment' => '2026-10-06 10:00:00.000', 'agent' => ['name' => 'ООО Ромашка'], 'organization' => ['name' => 'ООО «Солвер»'],
        'sum' => 9480000, 'payedSum' => 5000000, 'shippedSum' => 0, 'state' => ['name' => 'Новый', 'color' => 15106326],
    ]]];
    if ($path === "/entity/customerorder/$ord?expand=agent,organization,state,store,project") return [
        'id' => $ord, 'name' => '00042', 'agent' => ['name' => 'ООО Ромашка', 'inn' => '7700000000'], 'organization' => ['name' => 'ООО «Солвер»'],
        'sum' => 9480000, 'payedSum' => 5000000, 'state' => ['name' => 'Новый', 'meta' => ['href' => 'https://x/entity/customerorder/metadata/states/st000001-aaaa']],
        'demands' => [['meta' => ['href' => 'https://x/entity/demand/dm000001-aaaa', 'type' => 'demand']]],
        'payments' => [['meta' => ['href' => 'https://x/entity/paymentin/pm000001-aaaa', 'type' => 'paymentin']]],
    ];
    if ($path === '/entity/customerorder/metadata') return ['states' => [['id' => 'st000001-aaaa', 'name' => 'Новый'], ['id' => 'st000002-bbbb', 'name' => 'Отгружен']]];
    if ($path === "/entity/customerorder/$ord/positions?limit=1000&expand=assortment") return ['rows' => [[
        'id' => 'ps000001-aaaa', 'quantity' => 600, 'price' => 15800, 'discount' => 0, 'vat' => 22,
        'assortment' => ['name' => 'Подвяз 14×100', 'article' => '11-001', 'meta' => ['href' => 'https://x/entity/product/pr000001-aaaa', 'type' => 'product']],
    ]]];
    if ($path === '/entity/demand/dm000001-aaaa') return ['name' => '00010', 'sum' => 0, 'applicable' => false];
    if ($path === '/entity/paymentin/pm000001-aaaa') return ['name' => '00007', 'sum' => 5000000];
    if ($path === '/entity/customerorder/metadata/embeddedtemplate') return ['rows' => [['id' => 'tp000001-aaaa', 'name' => 'Заказ покупателя']]];
    if ($path === '/entity/customerorder/metadata/customtemplate') return ['rows' => []];
    if (str_ends_with($path, '/export')) return ['_location' => 'https://online.moysklad.ru/file/x.pdf'];
    if ($method === 'DOWNLOAD') return ['_bytes' => '%PDF-1.4 test'];
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
$again = $call(['action' => 'setup', 'setup_key' => 'setup-123', 'name' => 'Чужой']);
ok($again['ok'] && $call(['action' => 'me', 'token' => $again['token']])['me']['name'] === 'Басил', 'повторно — вход прежнего директора, нового не создаёт');
ok((int)$db->one('SELECT COUNT(*) AS n FROM employees')['n'] === 1, 'второго директора нет');
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
$designer = $D(['action' => 'employeeSave', 'employee' => ['name' => 'Пелагея', 'role' => 'designer']]);

// Вход сотрудника по ключу; 5 ошибок — пауза.
$opLogin = $call(['action' => 'login', 'key' => strtoupper($op['key']), 'device' => 'Redmi']);
ok($opLogin['ok'] && $opLogin['me']['role'] === 'operator', 'вход оператора');
for ($i = 0; $i < 5; $i++) $call(['action' => 'login', 'key' => 'неверный'], '10.9.9.9');
ok(str_contains($call(['action' => 'login', 'key' => $op['key']], '10.9.9.9')['error'], 'Подождите'), 'пауза после 5 ошибок');
$O = fn(array $r, string $ip = '10.0.0.1') => $call($r + ['token' => $opLogin['token']], $ip);
$designerLogin = $call(['action' => 'login', 'key' => $designer['key']]);
$T = fn(array $r) => $call($r + ['token' => $designerLogin['token']]);
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

// Техкарты: черновик → утверждение → неизменяемый снимок производственного задания.
$cardBody = [
    'code' => 'RIB-100', 'name' => 'Подвяз 2×2', 'productName' => 'Подвяз 14×100',
    'outputQuantity' => 1, 'outputUnit' => 'шт', 'plannedWastePercent' => 3,
    'operations' => [
        ['stage' => 'knit', 'name' => 'Вязание', 'timeTracking' => true, 'normSeconds' => 210, 'equipmentRequired' => true],
        ['stage' => 'qc', 'name' => 'ОТК'],
        ['stage' => 'pack', 'name' => 'Упаковка', 'outputStage' => true],
    ],
];
ok($O(['action' => 'techCardSave', 'card' => $cardBody])['error'] === 'Нет доступа', 'оператор не правит техкарты');
$draft = $T(['action' => 'techCardSave', 'card' => $cardBody]);
ok($draft['ok'] && $draft['techCard']['version'] === 1 && $draft['techCard']['status'] === 'draft', 'создана первая версия техкарты');
$version1 = $draft['techCard']['id'];
$approved = $T(['action' => 'techCardApprove', 'version_id' => $version1]);
ok($approved['ok'] && $approved['techCard']['status'] === 'approved' && strlen($approved['techCard']['snapshotHash']) === 64, 'версия техкарты утверждена');
ok(count($O(['action' => 'techCards'])['techCards']) === 1, 'оператор видит утверждённую техкарту');

$routedJob = $D(['action' => 'jobSave', 'job' => ['title' => 'КП-TECH Подвязы', 'quantity' => 100]]);
$assigned = $T(['action' => 'jobTechCardAssign', 'job_id' => $routedJob['id'], 'version_id' => $version1]);
ok($assigned['ok'] && $assigned['techCard']['version'] === 1, 'утверждённая техкарта назначена заданию');
ok(str_contains($O(['action' => 'stageStart', 'job_id' => $routedJob['id'], 'stage' => 'wto'])['error'], 'не входит'), 'этап вне маршрута запрещён');
ok(str_contains($O(['action' => 'stageStart', 'job_id' => $routedJob['id'], 'stage' => 'qc'])['error'], 'предыдущего'), 'следующий этап ждёт предыдущий');
$routeKnit = $O(['action' => 'stageStart', 'job_id' => $routedJob['id'], 'stage' => 'knit']);
ok($O(['action' => 'stageFinish', 'id' => $routeKnit['id'], 'quantity' => 60])['ok'], 'частичная выработка первого этапа');
$routeQc = $O(['action' => 'stageStart', 'job_id' => $routedJob['id'], 'stage' => 'qc']);
ok($O(['action' => 'stageFinish', 'id' => $routeQc['id'], 'quantity' => 50])['ok'], 'следующий этап принимает только доступный объём');
ok(str_contains($T(['action' => 'jobTechCardAssign', 'job_id' => $routedJob['id'], 'version_id' => $version1])['error'], 'После начала'), 'снимок нельзя заменить после старта');

$cardBody['id'] = $draft['techCard']['cardId'];
$cardBody['operations'][0]['normSeconds'] = 240;
$draft2 = $T(['action' => 'techCardSave', 'card' => $cardBody]);
ok($draft2['ok'] && $draft2['techCard']['version'] === 2, 'изменение создаёт новую черновую версию');
ok(str_contains($T(['action' => 'jobTechCardAssign', 'job_id' => $routedJob['id'], 'version_id' => $draft2['techCard']['id']])['error'], 'После начала'), 'начатое задание сохраняет старую версию');
$routed = array_values(array_filter($D(['action' => 'jobs'])['jobs'], fn($j) => $j['id'] === $routedJob['id']))[0];
ok($routed['techCard']['version'] === 1 && $routed['techCard']['operations'][0]['normSeconds'] === 210, 'новая версия не переписала снимок задания');

// Производство: этапы только своей роли; кто начал, кто закончил, сколько минут.
$job = $D(['action' => 'jobSave', 'job' => ['title' => 'КП-12 Подвязы 600', 'client' => 'ООО Ромашка', 'quantity' => 600]]);
ok($O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'spec'])['error'] === 'Этот этап ведёт другой участок', 'ТЗ — не оператор');
$st = $O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'knit']);
ok($st['ok'], 'оператор начал вязание');
ok($O(['action' => 'stageStart', 'job_id' => $job['id'], 'stage' => 'knit'])['error'] === 'Этап уже идёт', 'дважды не начать');
$now += 95 * 60000;
$fin = $O(['action' => 'stageFinish', 'id' => $st['id'], 'quantity' => 600]);
ok($fin['ok'] && $fin['minutes'] === 95, 'вязание: 95 минут');
$savedJob = array_values(array_filter($D(['action' => 'jobs'])['jobs'], fn($j) => $j['id'] === $job['id']))[0];
ok($savedJob['stages'][0]['startedBy'] === 'Олег' && $savedJob['stages'][0]['quantity'] === 600, 'кто и сколько');
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

// Заказы МойСклад: список, карточка со связанными документами, правка позиций, печать.
$ordId = 'a1b2c3d4-0000-0000-0000-000000000001';
$list = $D(['action' => 'msOrders', 'search' => 'Ромашка']);
ok($list['orders'][0]['name'] === '00042' && $list['orders'][0]['sum'] === 94800.0 && $list['orders'][0]['payed'] === 50000.0, 'список заказов');
$card = $D(['action' => 'msOrder', 'id' => $ordId]);
ok($card['positions'][0]['price'] === 158.0 && $card['positions'][0]['assortmentId'] === 'pr000001-aaaa', 'позиции заказа');
ok(count($card['related']) === 2 && $card['related'][0]['type'] === 'demand' && $card['related'][1]['sum'] === 50000.0, 'связанные документы');
$saved = $D(['action' => 'msOrderSave', 'id' => $ordId, 'description' => 'Срочно', 'stateId' => 'st000002-bbbb',
    'positions' => [['id' => 'ps000001-aaaa', 'assortmentId' => 'pr000001-aaaa', 'assortmentType' => 'product', 'quantity' => 650, 'price' => 155.5, 'discount' => 5]]]);
$put = array_values(array_filter($msCalls, fn($c) => $c[0] === 'PUT' && $c[1] === "/entity/customerorder/$ordId"))[0][2];
ok($saved['ok'] && $put['positions'][0]['price'] === 15550 && $put['positions'][0]['quantity'] === 650.0 && str_ends_with($put['state']['meta']['href'], 'st000002-bbbb'), 'заказ сохранён в МойСклад');
ok(str_contains($D(['action' => 'msOrderSave', 'id' => $ordId, 'positions' => [['assortmentId' => 'pr000001-aaaa', 'quantity' => 0]]])['error'], 'больше нуля'), 'нулевое количество нельзя');
ok($D(['action' => 'msOrder', 'id' => '../etc'])['error'] === 'Неверный документ', 'чужие пути в МойСклад нельзя');
ok($D(['action' => 'msTemplates', 'type' => 'customerorder'])['templates'][0]['name'] === 'Заказ покупателя', 'печатные формы');
$pdf = $D(['action' => 'msPrint', 'type' => 'customerorder', 'id' => $ordId, 'template' => 'tp000001-aaaa', 'fileName' => 'Заказ 00042']);
ok(base64_decode($pdf['pdf']) === '%PDF-1.4 test' && str_ends_with($pdf['name'], '.pdf'), 'PDF заказа');
ok($call(['action' => 'msOrders', 'token' => $hwLogin['token']])['error'] === 'Нет доступа', 'ручная работа заказы не видит');

// Задачи: ставит любой, исполнитель — «в работе»/«сделано», автор — «принято»; повтор; комментарии с @; файлы; уведомления.
$igorTok = $call(['action' => 'login', 'key' => $op2['key']])['token'];
$G = fn(array $r) => $call($r + ['token' => $igorTok]);
$hwTok = $hwLogin['token'];
$H = fn(array $r) => $call($r + ['token' => $hwTok]);
$tk = $G(['action' => 'taskSave', 'task' => ['title' => 'Упаковать 40 шт', 'assigneeId' => $hw['employee']['id'], 'due' => $now + 2 * 3600000,
    'repeat' => 'week', 'checklist' => [['text' => 'Коробки'], ['text' => 'Этикетки']]]]);
ok($tk['task']['assignee'] === 'Мария' && $tk['task']['status'] === 'new' && count($tk['task']['checklist']) === 2, 'задача коллеге');
$tid = $tk['task']['id'];
$n = $H(['action' => 'notifications'])['notifications'];
ok($n[0]['title'] === 'Новая задача: Упаковать 40 шт' && $n[0]['ref'] === (string)$tid, 'уведомление исполнителю');
$H(['action' => 'notificationsRead', 'ids' => [$n[0]['id']]]);
ok($H(['action' => 'notifications'])['notifications'] === [], 'прочитанное не повторяется');
ok(str_contains($G(['action' => 'taskStatus', 'id' => $tid, 'status' => 'done'])['error'], 'исполнитель'), 'сделано — только исполнитель');
ok($H(['action' => 'taskStatus', 'id' => $tid, 'status' => 'work'])['task']['status'] === 'work', 'в работе');
ok($H(['action' => 'taskCheck', 'id' => $tid, 'index' => 0, 'done' => true])['task']['checklist'][0]['done'] === true, 'пункт чек-листа');
$cm = $H(['action' => 'taskComment', 'id' => $tid, 'text' => '@Игорь коробок нет, @Басил закажите']);
ok(count($cm['comments']) === 1 && $D(['action' => 'notifications'])['notifications'][0]['kind'] === 'comment', 'упоминание @ — уведомление');
$up = $H(['action' => 'fileUpload', 'taskId' => $tid, 'name' => 'фото.jpg', 'mime' => 'image/jpeg', 'data' => base64_encode('JPEGDATA')]);
ok($up['files'][0]['name'] === 'фото.jpg' && base64_decode($G(['action' => 'fileGet', 'id' => $up['id']])['data']) === 'JPEGDATA', 'файл к задаче');
ok($call(['action' => 'fileGet', 'id' => $up['id'], 'token' => $D(['action' => 'me'])['ok'] ? $dir['token'] : ''])['name'] === 'фото.jpg', 'руководство открывает файл');
ok($H(['action' => 'taskStatus', 'id' => $tid, 'status' => 'done'])['task']['status'] === 'done', 'сделано');
$acc = $G(['action' => 'taskStatus', 'id' => $tid, 'status' => 'accepted']);
ok($acc['task']['status'] === 'accepted', 'принято автором');
$next = $H(['action' => 'tasks'])['tasks'];
ok(count($next) === 1 && $next[0]['id'] !== $tid && $next[0]['due'] === $now + 2 * 3600000 + 7 * 86400000 && $next[0]['checklist'][0]['done'] === false, 'повтор через неделю');
ok($G(['action' => 'tasks', 'scope' => 'all'])['error'] === 'Нет доступа', 'все задачи — только руководство');
ok(count($D(['action' => 'tasks', 'scope' => 'all'])['tasks']) === 1, 'руководство видит все');
// Просрочка: напоминание и уведомление автору.
$late = $G(['action' => 'taskSave', 'task' => ['title' => 'Отчёт', 'assigneeId' => $hw['employee']['id'], 'due' => $now + 600000]]);
$now += 3600000;
$hn = $H(['action' => 'notifications'])['notifications'];
ok(in_array('Просрочено: Отчёт', array_column($hn, 'title'), true), 'просрочка исполнителю');
ok(in_array('Просрочена задача: Отчёт', array_column($G(['action' => 'notifications'])['notifications'], 'title'), true), 'просрочка автору');
$stranger = $D(['action' => 'employeeSave', 'employee' => ['name' => 'Пётр', 'role' => 'operator']]);
ok($call(['action' => 'task', 'id' => $late['task']['id'], 'token' => $call(['action' => 'login', 'key' => $stranger['key']])['token']])['error'] === 'Нет доступа', 'чужую задачу не открыть');

// Профиль: сотрудник заполняет сам, директор подтверждает; сканы видят только кадры; соглашения; выплаты.
ok($H(['action' => 'me'])['legalOk'] === false, 'соглашения ещё не приняты');
ok(count($H(['action' => 'legal'])['docs']) === 3, 'три документа');
ok($H(['action' => 'legalAccept'])['allAccepted'] === true && $H(['action' => 'me'])['legalOk'] === true, 'соглашения приняты');
ok(str_contains($H(['action' => 'profileSave', 'fields' => ['inn' => '123']])['error'], '12 цифр'), 'проверка ИНН');
$pr = $H(['action' => 'profileSave', 'fields' => ['birthday' => '01.02.1990', 'inn' => '500100732259', 'passportNumber' => '123456', 'card' => '2202 0000 0000 0000']]);
ok($pr['profile']['fields']['passportNumber'] === '123456' && $pr['profile']['confirmed'] === false, 'профиль заполнен');
$rawP = $db->one('SELECT data_enc FROM profiles WHERE employee_id = ?', [$hw['employee']['id']])['data_enc'];
ok(!str_contains((string)$rawP, '123456'), 'паспорт в базе зашифрован');
ok($D(['action' => 'profileConfirm', 'id' => $hw['employee']['id']])['profile']['confirmed'] === true, 'директор подтвердил');
$dc = $H(['action' => 'profileDoc', 'kind' => 'passport', 'name' => 'паспорт.jpg', 'data' => base64_encode('SCAN')]);
$docFile = $dc['docs'][0]['fileId'];
ok($G(['action' => 'fileGet', 'id' => $docFile])['error'] === 'Нет доступа', 'скан паспорта коллега не видит');
ok($G(['action' => 'profile', 'id' => $hw['employee']['id']])['error'] === 'Нет доступа', 'чужой профиль не открыть');
ok(base64_decode($D(['action' => 'fileGet', 'id' => $docFile])['data']) === 'SCAN', 'директор видит скан');
ok($D(['action' => 'audit', 'from' => 0])['audit'][0]['action'] === 'docView', 'просмотр скана — в журнале');
ok(str_contains($H(['action' => 'payroll'])['error'], 'директор'), 'выплаты закрыты, пока директор не откроет');
$D(['action' => 'payrollSave', 'id' => $hw['employee']['id'], 'month' => '2026-10', 'kind' => 'salary', 'amount' => 40000]);
$D(['action' => 'payrollSave', 'id' => $hw['employee']['id'], 'month' => '2026-10', 'kind' => 'fine', 'amount' => 1000]);
$pay = $D(['action' => 'payrollSave', 'id' => $hw['employee']['id'], 'month' => '2026-10', 'kind' => 'advance', 'amount' => 15000]);
ok($pay['accrued'] === 39000.0 && $pay['paid'] === 15000.0 && $pay['balance'] === 24000.0, 'расчёт за месяц');
$D(['action' => 'payVisible', 'id' => $hw['employee']['id'], 'visible' => true]);
ok($H(['action' => 'payroll', 'month' => '2026-10'])['balance'] === 24000.0, 'сотрудник видит свои выплаты');
ok($H(['action' => 'payrollSave', 'id' => $hw['employee']['id'], 'month' => '2026-10', 'kind' => 'bonus', 'amount' => 99999])['error'] === 'Нет доступа', 'себе начислить нельзя');

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
ok($call(['action' => 'setup', 'setup_key' => 'нет'], '10.8.8.8')['error'] === 'Неверный ключ установки', 'восстановление — только с ключом');

echo "Сервер: все проверки пройдены ($checks)\n";
