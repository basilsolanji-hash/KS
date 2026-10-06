<?php
declare(strict_types=1);

// Точка входа API «ФАБРИКА» (staff.fabrika-ks.ru). Код и config.php лежат выше папки сайта — снаружи их не открыть.
ini_set('display_errors', '0');
$base = is_dir(__DIR__ . '/../ks') ? __DIR__ . '/../ks' : __DIR__ . '/..';
foreach (['Db', 'Crypto', 'Rules', 'Legal', 'ApiPeople', 'ApiWork', 'ApiOrders', 'ApiTasks', 'ApiProfile', 'ApiServer'] as $f) require_once "$base/src/$f.php";

header('Content-Type: application/json; charset=utf-8');
header('X-Content-Type-Options: nosniff');
header('Cache-Control: no-store');
header('Strict-Transport-Security: max-age=31536000');

$reply = function (array $data, int $code = 200): void {
    http_response_code($code);
    echo json_encode($data, JSON_UNESCAPED_UNICODE);
    exit;
};

$https = ($_SERVER['HTTPS'] ?? '') === 'on' || ($_SERVER['HTTP_X_FORWARDED_PROTO'] ?? '') === 'https';
if (!$https) $reply(['ok' => false, 'error' => 'Только HTTPS'], 403);
if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') $reply(['ok' => true, 'service' => 'ФАБРИКА']);

$raw = file_get_contents('php://input', false, null, 0, 1_000_001);
if ($raw === false || strlen($raw) > 1_000_000) $reply(['ok' => false, 'error' => 'Слишком большой запрос'], 413);
$req = json_decode($raw, true);
if (!is_array($req)) $reply(['ok' => false, 'error' => 'Нужен JSON'], 400);

if (!is_file("$base/config.php")) $reply(['ok' => false, 'error' => 'Сервер не настроен'], 500);
$config = require "$base/config.php";
$config['files_dir'] = "$base/files"; // файлы задач — выше папки сайта, зашифрованы
// Ключ шифрования базы: из секрета или создаётся один раз и хранится выше папки сайта (только владельцу).
// Создание — атомарное (x): два первых запроса одновременно не получат разные ключи.
if (empty($config['app_key'])) {
    $keyFile = "$base/app.key";
    if (!is_file($keyFile)) {
        $old = umask(0077);
        $h = @fopen($keyFile, 'x');
        if ($h) {
            fwrite($h, base64_encode(random_bytes(32)));
            fclose($h);
        }
        umask($old);
        clearstatcache();
    }
    $config['app_key'] = trim((string)@file_get_contents($keyFile));
    if (strlen(base64_decode($config['app_key'], true) ?: '') !== 32) $reply(['ok' => false, 'error' => 'Сервер не настроен'], 500);
}

// Адрес телефона: за прокси beget (адрес из внутренней сети) — из заголовка прокси; иначе — прямой адрес.
// Заголовки от внешних адресов не принимаются (их можно подделать).
$ip = (string)($_SERVER['REMOTE_ADDR'] ?? '');
if (!filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE)) {
    // X-Real-IP ставит сам прокси; в X-Forwarded-For надёжен только последний адрес (добавлен прокси).
    $chain = explode(',', (string)($_SERVER['HTTP_X_FORWARDED_FOR'] ?? ''));
    $fwd = trim((string)($_SERVER['HTTP_X_REAL_IP'] ?? end($chain)));
    if (filter_var($fwd, FILTER_VALIDATE_IP)) $ip = $fwd;
}
try {
    $db = Ks\Db::fromConfig($config);
    $db->migrate();
    $api = new Ks\Api($db, new Ks\Crypto($config['app_key']), $config, $ip);
    $reply($api->handle($req));
} catch (Throwable $e) {
    error_log('ks: ' . $e->getMessage());
    $reply(['ok' => false, 'error' => 'Сервер недоступен'], 500);
}
