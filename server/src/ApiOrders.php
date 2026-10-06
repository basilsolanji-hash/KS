<?php
declare(strict_types=1);

namespace Ks;

/**
 * Заказы покупателей МойСклад: список, полная карточка, правка, связанные документы, печатные формы (PDF).
 * Смотрят офис и руководство; меняют директор, помощник и менеджер.
 */
trait ApiOrders
{
    /** Документы, которые открываются из заказа. */
    private static array $DOC_TYPES = ['customerorder', 'demand', 'invoiceout', 'paymentin', 'cashin', 'factureout', 'salesreturn'];

    private static function msId(string $id): string
    {
        if (!preg_match('/^[\w-]{8,64}$/', $id)) throw new ApiError('Неверный документ');
        return $id;
    }

    private static function docType(string $t): string
    {
        if (!in_array($t, self::$DOC_TYPES, true)) throw new ApiError('Неверный тип документа');
        return $t;
    }

    /** id из ссылки МойСклад (…/entity/demand/<id>). */
    private static function hrefId(array $meta): string
    {
        $href = (string)($meta['href'] ?? '');
        return (string)preg_replace('~^.*/~', '', strtok($href, '?') ?: '');
    }

    private function orderRead(): void
    {
        $this->need('assistant', 'accountant', 'manager', 'merch');
    }

    private function orderWrite(): void
    {
        $this->need('assistant', 'manager');
    }

    private static function money(mixed $kop): float
    {
        return round(((float)$kop) / 100, 2);
    }

    /** Список заказов: номер, дата, контрагент, организация, сумма, оплачено, отгружено, статус. Поиск по номеру и контрагенту. */
    private function aMsOrders(array $req): array
    {
        $this->orderRead();
        $limit = max(1, min(100, (int)($req['limit'] ?? 50)));
        $offset = max(0, (int)($req['offset'] ?? 0));
        $q = '/entity/customerorder?limit=' . $limit . '&offset=' . $offset . '&order=moment,desc&expand=agent,organization,state';
        $search = trim((string)($req['search'] ?? ''));
        if ($search !== '') $q .= '&search=' . rawurlencode(mb_substr($search, 0, 60));
        $data = ($this->ms)('GET', $q, null);
        return [
            'orders' => array_map(fn($o) => [
                'id' => (string)$o['id'], 'name' => (string)($o['name'] ?? ''), 'moment' => (string)($o['moment'] ?? ''),
                'agent' => (string)($o['agent']['name'] ?? ''), 'organization' => (string)($o['organization']['name'] ?? ''),
                'sum' => self::money($o['sum'] ?? 0), 'payed' => self::money($o['payedSum'] ?? 0), 'shipped' => self::money($o['shippedSum'] ?? 0),
                'state' => (string)($o['state']['name'] ?? ''), 'stateColor' => (int)($o['state']['color'] ?? 0),
            ], (array)($data['rows'] ?? [])),
            'total' => (int)($data['meta']['size'] ?? 0),
        ];
    }

    /** Позиции документа (до 1000). */
    private function positions(string $type, string $id): array
    {
        $rows = (($this->ms)('GET', "/entity/$type/$id/positions?limit=1000&expand=assortment", null))['rows'] ?? [];
        return array_map(fn($p) => [
            'id' => (string)($p['id'] ?? ''), 'name' => (string)($p['assortment']['name'] ?? ''),
            'assortmentId' => self::hrefId((array)($p['assortment']['meta'] ?? [])), 'assortmentType' => (string)($p['assortment']['meta']['type'] ?? ''),
            'code' => (string)($p['assortment']['article'] ?? $p['assortment']['code'] ?? ''),
            'quantity' => (float)($p['quantity'] ?? 0), 'price' => self::money($p['price'] ?? 0),
            'discount' => (float)($p['discount'] ?? 0), 'vat' => (int)($p['vat'] ?? 0),
            'shipped' => (float)($p['shipped'] ?? 0),
        ], (array)$rows);
    }

    /** Связанные документы заказа: отгрузки, счета, платежи, счета-фактуры, возвраты. */
    private function related(array $o): array
    {
        $out = [];
        foreach (['demands' => 'demand', 'invoicesOut' => 'invoiceout', 'payments' => null, 'factureOut' => 'factureout', 'returns' => 'salesreturn'] as $field => $type) {
            $items = $o[$field] ?? [];
            if (isset($items['meta'])) $items = [$items];
            foreach ((array)$items as $item) {
                $meta = (array)($item['meta'] ?? []);
                $t = $type ?? (string)($meta['type'] ?? '');
                if (!in_array($t, self::$DOC_TYPES, true)) continue;
                $id = self::hrefId($meta);
                if ($id === '') continue;
                $doc = ($this->ms)('GET', "/entity/$t/$id", null);
                $out[] = [
                    'type' => $t, 'id' => $id, 'name' => (string)($doc['name'] ?? ''), 'moment' => (string)($doc['moment'] ?? ''),
                    'sum' => self::money($doc['sum'] ?? 0), 'applicable' => (bool)($doc['applicable'] ?? true),
                ];
            }
        }
        return $out;
    }

    /** Полная карточка заказа: шапка, позиции, статусы для выбора, связанные документы. */
    private function aMsOrder(array $req): array
    {
        $this->orderRead();
        $id = self::msId((string)($req['id'] ?? ''));
        $o = ($this->ms)('GET', "/entity/customerorder/$id?expand=agent,organization,state,store,project", null);
        $states = (($this->ms)('GET', '/entity/customerorder/metadata', null))['states'] ?? [];
        return [
            'order' => [
                'id' => $id, 'name' => (string)($o['name'] ?? ''), 'moment' => (string)($o['moment'] ?? ''),
                'agent' => (string)($o['agent']['name'] ?? ''), 'agentInn' => (string)($o['agent']['inn'] ?? ''),
                'agentPhone' => (string)($o['agent']['phone'] ?? ''), 'agentEmail' => (string)($o['agent']['email'] ?? ''),
                'organization' => (string)($o['organization']['name'] ?? ''), 'store' => (string)($o['store']['name'] ?? ''),
                'state' => (string)($o['state']['name'] ?? ''), 'stateId' => self::hrefId((array)($o['state']['meta'] ?? [])),
                'description' => (string)($o['description'] ?? ''), 'shipmentAddress' => (string)($o['shipmentAddress'] ?? ''),
                'deliveryPlannedMoment' => (string)($o['deliveryPlannedMoment'] ?? ''),
                'sum' => self::money($o['sum'] ?? 0), 'vatSum' => self::money($o['vatSum'] ?? 0),
                'payed' => self::money($o['payedSum'] ?? 0), 'shipped' => self::money($o['shippedSum'] ?? 0),
                'applicable' => (bool)($o['applicable'] ?? true),
            ],
            'positions' => $this->positions('customerorder', $id),
            'states' => array_map(fn($s) => ['id' => (string)$s['id'], 'name' => (string)$s['name'], 'color' => (int)($s['color'] ?? 0)], (array)$states),
            'related' => $this->related($o),
        ];
    }

    /**
     * Сохранить заказ: комментарий, адрес, дата отгрузки, статус и позиции целиком (количество, цена, скидка; новые — из каталога).
     * Позиции, которых нет в списке, удаляются (как в МойСклад при сохранении документа).
     */
    private function aMsOrderSave(array $req): array
    {
        $this->orderWrite();
        $id = self::msId((string)($req['id'] ?? ''));
        $body = [];
        foreach (['description' => 4000, 'shipmentAddress' => 255] as $k => $max) {
            if (array_key_exists($k, $req)) $body[$k] = mb_substr(trim((string)$req[$k]), 0, $max);
        }
        if (!empty($req['deliveryPlannedMoment'])) {
            $d = (string)$req['deliveryPlannedMoment'];
            if (!preg_match('/^\d{4}-\d{2}-\d{2}( \d{2}:\d{2}(:\d{2})?)?$/', $d)) throw new ApiError('Неверная дата отгрузки');
            $body['deliveryPlannedMoment'] = strlen($d) === 10 ? "$d 00:00:00" : $d;
        }
        if (!empty($req['stateId'])) {
            $body['state'] = ['meta' => ['href' => 'https://api.moysklad.ru/api/remap/1.2/entity/customerorder/metadata/states/' . self::msId((string)$req['stateId']),
                'type' => 'state', 'mediaType' => 'application/json']];
        }
        if (isset($req['positions']) && is_array($req['positions'])) {
            $pos = [];
            foreach ($req['positions'] as $p) {
                $qty = (float)($p['quantity'] ?? 0);
                if ($qty <= 0) throw new ApiError('Количество должно быть больше нуля');
                $price = (float)($p['price'] ?? 0);
                if ($price < 0) throw new ApiError('Цена не может быть отрицательной');
                $disc = (float)($p['discount'] ?? 0);
                if ($disc < 0 || $disc > 100) throw new ApiError('Скидка — от 0 до 100 %');
                $type = (string)($p['assortmentType'] ?? 'product');
                if (!in_array($type, ['product', 'variant', 'service', 'bundle'], true)) throw new ApiError('Неверный товар');
                $row = [
                    'quantity' => $qty, 'price' => (int)round($price * 100), 'discount' => $disc,
                    'assortment' => ['meta' => ['href' => 'https://api.moysklad.ru/api/remap/1.2/entity/' . $type . '/' . self::msId((string)($p['assortmentId'] ?? '')),
                        'type' => $type, 'mediaType' => 'application/json']],
                ];
                if (!empty($p['vat'])) $row['vat'] = max(0, min(30, (int)$p['vat']));
                if (!empty($p['id'])) $row['meta'] = ['href' => "https://api.moysklad.ru/api/remap/1.2/entity/customerorder/$id/positions/" . self::msId((string)$p['id']),
                    'type' => 'customerorderposition', 'mediaType' => 'application/json'];
                $pos[] = $row;
            }
            if (!$pos) throw new ApiError('В заказе должна остаться хотя бы одна позиция');
            $body['positions'] = $pos;
        }
        if (!$body) throw new ApiError('Нет изменений');
        ($this->ms)('PUT', "/entity/customerorder/$id", $body);
        $this->log('msOrderSave', $id . ' ' . implode(',', array_keys($body)));
        $this->eventLog('order', 'Заказ изменён');
        return $this->aMsOrder(['id' => $id]);
    }

    /** Связанный документ целиком: шапка и позиции. */
    private function aMsDoc(array $req): array
    {
        $this->orderRead();
        $type = self::docType((string)($req['type'] ?? ''));
        $id = self::msId((string)($req['id'] ?? ''));
        $d = ($this->ms)('GET', "/entity/$type/$id?expand=agent,organization,state", null);
        $hasPositions = !in_array($type, ['paymentin', 'cashin'], true);
        return ['doc' => [
            'type' => $type, 'id' => $id, 'name' => (string)($d['name'] ?? ''), 'moment' => (string)($d['moment'] ?? ''),
            'agent' => (string)($d['agent']['name'] ?? ''), 'organization' => (string)($d['organization']['name'] ?? ''),
            'state' => (string)($d['state']['name'] ?? ''), 'description' => (string)($d['description'] ?? ''),
            'sum' => self::money($d['sum'] ?? 0), 'applicable' => (bool)($d['applicable'] ?? true),
            'paymentPurpose' => (string)($d['paymentPurpose'] ?? ''),
        ], 'positions' => $hasPositions ? $this->positions($type, $id) : []];
    }

    /** Правка связанного документа: комментарий, назначение платежа, проведён/не проведён. */
    private function aMsDocSave(array $req): array
    {
        $this->orderWrite();
        $type = self::docType((string)($req['type'] ?? ''));
        $id = self::msId((string)($req['id'] ?? ''));
        $body = [];
        if (array_key_exists('description', $req)) $body['description'] = mb_substr(trim((string)$req['description']), 0, 4000);
        if (array_key_exists('paymentPurpose', $req) && in_array($type, ['paymentin', 'cashin'], true)) {
            $body['paymentPurpose'] = mb_substr(trim((string)$req['paymentPurpose']), 0, 1000);
        }
        if (array_key_exists('applicable', $req)) {
            if (!Rules::full($this->me['role'])) throw new ApiError('Проводит документы руководство');
            $body['applicable'] = (bool)$req['applicable'];
        }
        if (!$body) throw new ApiError('Нет изменений');
        ($this->ms)('PUT', "/entity/$type/$id", $body);
        $this->log('msDocSave', "$type $id " . implode(',', array_keys($body)));
        return $this->aMsDoc(['type' => $type, 'id' => $id]);
    }

    /** Печатные формы документа (шаблоны МойСклад). */
    private function aMsTemplates(array $req): array
    {
        $this->orderRead();
        $type = self::docType((string)($req['type'] ?? ''));
        $rows = [];
        foreach (['embeddedtemplate', 'customtemplate'] as $kind) {
            foreach ((array)((($this->ms)('GET', "/entity/$type/metadata/$kind", null))['rows'] ?? []) as $t) {
                $rows[] = ['kind' => $kind, 'id' => (string)($t['id'] ?? ''), 'name' => (string)($t['name'] ?? '')];
            }
        }
        return ['templates' => $rows];
    }

    /** PDF печатной формы (base64) — для печати и отправки с телефона. */
    private function aMsPrint(array $req): array
    {
        $this->orderRead();
        $type = self::docType((string)($req['type'] ?? ''));
        $id = self::msId((string)($req['id'] ?? ''));
        $kind = (string)($req['kind'] ?? 'embeddedtemplate');
        if (!in_array($kind, ['embeddedtemplate', 'customtemplate'], true)) throw new ApiError('Неверный шаблон');
        $tpl = self::msId((string)($req['template'] ?? ''));
        $r = ($this->ms)('POST', "/entity/$type/$id/export", [
            'template' => ['meta' => ['href' => "https://api.moysklad.ru/api/remap/1.2/entity/$type/metadata/$kind/$tpl", 'type' => $kind, 'mediaType' => 'application/json']],
            'extension' => 'pdf',
        ]);
        $url = (string)($r['_location'] ?? '');
        if ($url === '') throw new ApiError('МойСклад не выдал файл');
        $file = ($this->ms)('DOWNLOAD', $url, null);
        $bytes = (string)($file['_bytes'] ?? '');
        if ($bytes === '' || strlen($bytes) > 8_000_000) throw new ApiError('Файл не получен');
        $this->log('msPrint', "$type $id");
        return ['pdf' => base64_encode($bytes), 'name' => preg_replace('/[^\w.-]+/u', '_', (string)($req['fileName'] ?? "$type-$id")) . '.pdf'];
    }

    /** Поиск товара для новой позиции заказа. */
    private function aMsAssortment(array $req): array
    {
        $this->orderWrite();
        $search = trim((string)($req['search'] ?? ''));
        if (mb_strlen($search) < 2) return ['rows' => []];
        $rows = (($this->ms)('GET', '/entity/assortment?limit=30&search=' . rawurlencode(mb_substr($search, 0, 60)), null))['rows'] ?? [];
        return ['rows' => array_values(array_map(fn($a) => [
            'id' => (string)$a['id'], 'type' => (string)($a['meta']['type'] ?? 'product'), 'name' => (string)($a['name'] ?? ''),
            'code' => (string)($a['article'] ?? $a['code'] ?? ''), 'price' => self::money($a['salePrices'][0]['value'] ?? 0),
            'stock' => isset($a['stock']) ? (float)$a['stock'] : null,
        ], array_filter((array)$rows, fn($a) => in_array($a['meta']['type'] ?? '', ['product', 'variant', 'service', 'bundle'], true))))];
    }

    /** Действие в журнал активности от имени сервера (заказы, задачи…). */
    private function eventLog(string $kind, string $detail): void
    {
        $this->db->insert('events', ['employee_id' => $this->me['id'], 'kind' => $kind, 'detail' => mb_substr($detail, 0, 200), 'created_at' => $this->now()]);
    }
}
