<?php
declare(strict_types=1);

// Точка входа API «ФАБРИКА» (staff.fabrika-ks.ru). Код и config.php лежат выше папки сайта — снаружи их не открыть.
$base = is_dir(__DIR__ . '/../ks') ? __DIR__ . '/../ks' : __DIR__ . '/..';
foreach (['Db', 'Crypto', 'Rules', 'Api'] as $f) require_once "$base/src/$f.php";

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

$config = require "$base/config.php";
// Ключ шифрования базы: из секрета или создаётся один раз и хранится выше папки сайта (только для чтения владельцем).
if (empty($config['app_key'])) {
    $keyFile = "$base/app.key";
    if (!is_file($keyFile)) {
        file_put_contents($keyFile, base64_encode(random_bytes(32)), LOCK_EX);
        @chmod($keyFile, 0600);
    }
    $config['app_key'] = trim((string)file_get_contents($keyFile));
}
try {
    $db = Ks\Db::fromConfig($config);
    $db->migrate();
    $api = new Ks\Api($db, new Ks\Crypto($config['app_key']), $config, (string)($_SERVER['REMOTE_ADDR'] ?? ''));
    $reply($api->handle($req));
} catch (Throwable $e) {
    error_log('ks: ' . $e->getMessage());
    $reply(['ok' => false, 'error' => 'Сервер недоступен'], 500);
}
