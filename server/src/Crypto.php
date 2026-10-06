<?php
declare(strict_types=1);

namespace Ks;

/**
 * Шифрование данных в базе (телефоны, e-mail, суммы зарплат): XSalsa20-Poly1305 (libsodium),
 * ключ — только в config.php на сервере. Ключи входа и токены хранятся как HMAC — по базе их не восстановить.
 */
final class Crypto
{
    private string $key;

    public function __construct(string $base64Key)
    {
        $key = base64_decode($base64Key, true);
        if ($key === false || strlen($key) !== SODIUM_CRYPTO_SECRETBOX_KEYBYTES) {
            throw new \RuntimeException('APP_KEY: нужен ключ 32 байта в base64');
        }
        $this->key = $key;
    }

    public function encrypt(?string $plain): ?string
    {
        if ($plain === null || $plain === '') return null;
        $nonce = random_bytes(SODIUM_CRYPTO_SECRETBOX_NONCEBYTES);
        return base64_encode($nonce . sodium_crypto_secretbox($plain, $nonce, $this->key));
    }

    public function decrypt(?string $box): string
    {
        if ($box === null || $box === '') return '';
        $raw = base64_decode($box, true);
        if ($raw === false || strlen($raw) <= SODIUM_CRYPTO_SECRETBOX_NONCEBYTES) return '';
        $nonce = substr($raw, 0, SODIUM_CRYPTO_SECRETBOX_NONCEBYTES);
        $plain = sodium_crypto_secretbox_open(substr($raw, SODIUM_CRYPTO_SECRETBOX_NONCEBYTES), $nonce, $this->key);
        return $plain === false ? '' : $plain;
    }

    /** Отпечаток секрета (ключа входа, токена) для поиска в базе. */
    public function hash(string $secret): string
    {
        return hash_hmac('sha256', $secret, $this->key);
    }

    /** Новый секрет: 24 символа без похожих букв (удобно вводить вручную). */
    public static function newKey(int $length = 24): string
    {
        $alphabet = 'abcdefghjkmnpqrstuvwxyz23456789';
        $out = '';
        for ($i = 0; $i < $length; $i++) $out .= $alphabet[random_int(0, strlen($alphabet) - 1)];
        return $out;
    }
}
