// Проверка логики Code.gs на имитации Google Таблицы (node google-sheets/test/run.js).
const fs = require('fs');
const path = require('path');
const assert = require('assert');

function makeSheet(name, rows) {
  const data = rows.map((r) => r.slice());
  return {
    name,
    data,
    getDataRange() {
      return {
        getValues: () => data.map((r) => r.slice()),
        getDisplayValues: () => data.map((r) => r.map((v) => (v instanceof Date ? v.toISOString() : String(v)))),
      };
    },
    getRange(row, col, nr = 1, nc = 1) {
      return {
        setValues(values) {
          for (let i = 0; i < nr; i++) {
            while (data.length < row + i) data.push([]);
            for (let j = 0; j < nc; j++) data[row - 1 + i][col - 1 + j] = values[i][j];
          }
        },
        setValue(v) { this.setValues([[v]]); },
        getValue() { return (data[row - 1] || [])[col - 1]; },
      };
    },
    appendRow(r) { data.push(r.map((v) => (typeof v === 'string' && v.startsWith("'") ? v.slice(1) : v))); },
    deleteRow(i) { data.splice(i - 1, 1); },
    setFrozenRows() {},
    getLastRow() { return data.length; },
    maxColumns: 21,
    getMaxColumns() { return this.maxColumns; },
    insertColumnsAfter(after, n) { this.maxColumns = after + n; },
  };
}

const sheets = {
  'Настройки': makeSheet('Настройки', [['Параметр', 'Значение'], ['Ключ доступа', 'secret'], ['Начальный номер КП', '100']]),
  'Изделия': makeSheet('Изделия', [['Код'], ['PODV', 'Подвяз', 'шт', '170']]),
  'Параметры': makeSheet('Параметры', [['Код изделия']]),
  'Объём': makeSheet('Объём', [['Код изделия']]),
  'КП': makeSheet('КП', [['№']]),
  'Позиции КП': makeSheet('Позиции КП', [['№ КП']]),
  'Клиенты': makeSheet('Клиенты', [['Компания'], ['ООО Старый', '', '', '', '', '']]),
};
const ss = {
  getSheetByName: (n) => sheets[n] || null,
  getName: () => 'Тест',
  getUrl: () => 'https://docs.google.com/test',
  getSpreadsheetTimeZone: () => 'Europe/Moscow',
  insertSheet: (n) => (sheets[n] = makeSheet(n, [])),
};

const context = {
  SpreadsheetApp: { getActiveSpreadsheet: () => ss },
  LockService: { getScriptLock: () => ({ waitLock() {}, releaseLock() {} }) },
  Utilities: {
    formatDate: (d, tz, f) => (f === 'yyyy-MM' ? d.toISOString().slice(0, 7) : d.toISOString()),
    base64Encode: (b) => Buffer.from(b).toString('base64'),
    base64Decode: (s) => Buffer.from(s, 'base64'),
    DigestAlgorithm: { SHA_256: 'sha256' },
    computeDigest: (alg, text) => require('crypto').createHash(alg).update(String(text)).digest(),
    newBlob: (bytes, mime, name) => ({
      bytes, mime, name,
      getBytes: () => (typeof bytes === 'string' ? Buffer.from(bytes, 'utf8') : Buffer.from(bytes)),
      getDataAsString: () => (typeof bytes === 'string' ? bytes : Buffer.from(bytes).toString('utf8')),
    }),
    gzip: (blob) => context.Utilities.newBlob(require('zlib').gzipSync(blob.getBytes()), 'application/x-gzip'),
    ungzip: (blob) => context.Utilities.newBlob(require('zlib').gunzipSync(blob.getBytes())),
  },
  // Кэш скрипта: по умолчанию «пустой» (проверки видят правки листов сразу); в конце — проверка с настоящим кэшем.
  CacheService: {
    store: null,
    getScriptCache() {
      const self = this;
      return {
        get: (k) => (self.store && k in self.store ? self.store[k] : null),
        getAll: (keys) => Object.fromEntries(keys.filter((k) => self.store && k in self.store).map((k) => [k, self.store[k]])),
        put: (k, v) => { if (self.store) self.store[k] = String(v); },
        putAll: (o) => { if (self.store) Object.assign(self.store, o); },
        remove: (k) => { if (self.store) delete self.store[k]; },
        removeAll: (ks) => { if (self.store) ks.forEach((k) => delete self.store[k]); },
      };
    },
  },
  ContentService: { createTextOutput: (t) => ({ text: t, setMimeType() { return this; } }), MimeType: { JSON: 'json' } },
  DriveApp: {
    getFolderById: () => ({
      getFilesByName: () => ({ hasNext: () => false }),
      createFile: (blob) => ({ getId: () => 'f1', getUrl: () => 'https://drive/f1', blob }),
    }),
    getFileById: (id) => ({ getBlob: () => ({ id }) }),
  },
  PropertiesService: {
    props: {},
    getScriptProperties() {
      const props = this.props;
      return { getProperty: (k) => props[k] || null, setProperty: (k, v) => { props[k] = v; }, deleteProperty: (k) => { delete props[k]; } };
    },
  },
  UrlFetchApp: { fetch: () => { throw new Error('МойСклад не подключён в тесте'); } },
  MailApp: {
    quota: 100,
    sent: [],
    getRemainingDailyQuota() { return this.quota; },
    sendEmail(to, subject, body, options) { this.sent.push({ to, subject, body, options }); },
  },
};
const code = fs.readFileSync(path.join(__dirname, '..', 'Code.gs'), 'utf8');
const fn = new Function(...Object.keys(context), code + '\nreturn { doGet, doPost, onEdit, backupNow_ };');
const api = fn(...Object.values(context));

const call = (body) => JSON.parse(api.doPost({ postData: { contents: JSON.stringify(Object.assign({ key: 'secret' }, body)) } }).text);
const get = (params) => JSON.parse(api.doGet({ parameter: Object.assign({ key: 'secret' }, params) }).text);

// Ключ доступа.
assert.strictEqual(get({ action: 'ping', key: 'wrong' }).ok, false);
assert.strictEqual(get({ action: 'ping' }).ok, true);

// Справочники: необязательные листы возвращаются пустыми.
const cat = get({ action: 'catalog' });
assert.strictEqual(cat.ok, true);
assert.deepStrictEqual(cat.sheets.costs, []);
assert.strictEqual(cat.sheets.products[1][0], 'PODV');

// Новое КП получает номер с «Начальный номер КП».
const quote = {
  id: 'q1', client: 'ООО Ромашка', contact: '+7 999', email: 'a@b.ru', phone: '+7 985 000-79-92', inn: '123',
  subtotal: 100, vat: 22, total: 122, profit: 10, cost: 90, validUntil: Date.UTC(2026, 9, 9),
  lines: [{ code: 'PODV', product: 'Подвяз', params: 'Размер: 115×14', qty: 10, unit: 'шт', price: 12.2, sum: 122, discount: 0 }],
  data: '{}',
};
assert.strictEqual(call({ action: 'saveQuote', quote }).number, 100);
// Повторное сохранение того же КП — тот же номер, позиции заменяются, статус сохраняется.
call({ action: 'setStatus', id: 'q1', status: 'Согласовано' });
assert.strictEqual(call({ action: 'saveQuote', quote }).number, 100);
assert.strictEqual(sheets['Позиции КП'].data.length, 2);
assert.strictEqual(sheets['КП'].data[1][13], 'Согласовано');
// Следующее КП — следующий номер.
assert.strictEqual(call({ action: 'saveQuote', quote: Object.assign({}, quote, { id: 'q2', client: 'ООО Старый' }) }).number, 101);
// «+7…» не превращается в формулу.
assert.strictEqual(sheets['КП'].data[1][18], "'+7 985 000-79-92");
// Новый клиент добавлен один раз, существующий — не дублируется.
assert.strictEqual(sheets['Клиенты'].data.length, 3);
// Неизвестный статус — ошибка.
assert.strictEqual(call({ action: 'setStatus', id: 'q1', status: 'Что-то' }).ok, false);

// История: новые сверху, со статусом, месяцем и суммами по изделиям.
const list = get({ action: 'quotes' }).quotes;
assert.strictEqual(list[0].number, 101);
assert.strictEqual(list[1].status, 'Согласовано');
assert.strictEqual(list[1].products['Подвяз'], 122);
assert.ok(/^\d{4}-\d{2}$/.test(list[1].month));
assert.ok(list[1].validUntil > 0);

// Письмо клиенту: ответ и копия — на e-mail фабрики, PDF во вложении, отметка в листе «КП».
sheets['Настройки'].data.push(['E-mail', 'Sale@fabrika-ks.ru'], ['Название для КП', 'Фабрика "KS"'], ['Папка: КП (ID)', 'pdfFolder']);
// Только на e-mail клиента этого КП: чужой адрес и несохранённое КП — отказ.
assert.ok(/только на e-mail клиента/.test(call({ action: 'sendEmail', quoteId: 'q1', to: 'spam@example.ru', data: '' }).error));
assert.ok(/Сначала сохраните КП/.test(call({ action: 'sendEmail', quoteId: 'нет', to: 'a@b.ru', data: '' }).error));
const mail = call({ action: 'sendEmail', quoteId: 'q1', to: 'A@B.ru', subject: 'КП № 100', body: 'Текст', name: 'KP_100.pdf', data: 'JVBERg==' });
assert.strictEqual(mail.ok, true, mail.error);
const sent = context.MailApp.sent[0];
assert.strictEqual(sent.to, 'A@B.ru');
assert.strictEqual(sent.options.replyTo, 'Sale@fabrika-ks.ru');
assert.strictEqual(sent.options.cc, 'Sale@fabrika-ks.ru');
assert.strictEqual(sent.options.name, 'Фабрика "KS"');
assert.strictEqual(sent.options.attachments.length, 1);
assert.ok(String(sheets['КП'].data[1][21]).endsWith('→ A@B.ru'));
assert.strictEqual(sheets['КП'].data[1][20], 'https://drive/f1');
assert.strictEqual(sheets['КП'].getMaxColumns(), 22);
// Плохой адрес и исчерпанный лимит — понятная ошибка, письмо не уходит.
assert.strictEqual(call({ action: 'sendEmail', to: 'не адрес', data: '' }).ok, false);
context.MailApp.quota = 0;
assert.ok(/Лимит/.test(call({ action: 'sendEmail', quoteId: 'q1', to: 'a@b.ru', data: '' }).error));
assert.strictEqual(context.MailApp.sent.length, 1);

// ---- Учёт: счета, оплаты, заказы, склад пряжи (листы создаются сами).
sheets['Настройки'].data.push(['Начальный номер счёта', '50'], ['Папка: документы (ID)', 'docs']);
assert.strictEqual(call({ action: 'addInvoice', invoice: { quoteId: 'q1', quoteNumber: 100, client: 'ООО Ромашка', amount: 61, purpose: 'Предоплата 50 %' } }).number, 50);
assert.strictEqual(call({ action: 'addInvoice', invoice: { quoteId: 'q1', quoteNumber: 100, client: 'ООО Ромашка', amount: 61, purpose: 'Остаток' } }).number, 51);
assert.strictEqual(sheets['Счета'].data[0][0], '№ счёта');
// PDF счёта — в «Документы», ссылка — в строку счёта.
call({ action: 'uploadFile', kind: 'doc', name: 'Счёт_50.pdf', data: 'JVBERg==', mime: 'application/pdf', invoiceNumber: 50 });
assert.strictEqual(sheets['Счета'].data[1][7], 'https://drive/f1');

const pay = { id: 'p1', quoteId: 'q1', quoteNumber: 100, client: 'ООО Ромашка', amount: 61, date: Date.UTC(2026, 9, 5) };
assert.strictEqual(call({ action: 'addPayment', payment: pay }).ok, true);
call({ action: 'addPayment', payment: pay }); // повтор — без дубля
assert.strictEqual(sheets['Оплаты'].data.length, 2);
assert.strictEqual(call({ action: 'addPayment', payment: Object.assign({}, pay, { id: 'p2', amount: 0 }) }).ok, false);

const order = { quoteId: 'q1', quoteNumber: 100, client: 'ООО Ромашка', created: Date.UTC(2026, 9, 5), due: Date.UTC(2026, 9, 26), stage: 'Новый', items: 'Подвяз — 10 шт', yarn: '[]' };
call({ action: 'saveOrder', order });
call({ action: 'saveOrder', order: Object.assign({}, order, { stage: 'В вязке', yarnWrittenOff: true }) });
assert.strictEqual(sheets['Заказы'].data.length, 2);
assert.strictEqual(sheets['Заказы'].data[1][5], 'В вязке');

call({ action: 'addYarnMoves', moves: [{ id: 'm1', yarn: 'Полиэстер', kg: 25, reason: 'Приход' }, { id: 'm2', yarn: 'Полиэстер', kg: -5, reason: 'Заказ № 100' }] });
call({ action: 'addYarnMoves', moves: [{ id: 'm1', yarn: 'Полиэстер', kg: 25, reason: 'Приход' }] });
assert.strictEqual(sheets['Движение пряжи'].data.length, 3);

const ops = get({ action: 'ops' }).ops;
assert.strictEqual(ops.invoices.length, 2);
assert.strictEqual(ops.payments[0].amount, 61);
assert.strictEqual(ops.payments[0].date, Date.UTC(2026, 9, 5));
assert.strictEqual(ops.orders[0].stage, 'В вязке');
assert.strictEqual(ops.orders[0].yarnWrittenOff, true);
assert.strictEqual(ops.moves.reduce((a, m) => a + m.kg, 0), 20);
// Ошибочную оплату можно удалить.
assert.strictEqual(call({ action: 'deletePayment', id: 'p1' }).ok, true);
assert.strictEqual(get({ action: 'ops' }).ops.payments.length, 0);

// Письмо со счётом — без отметки в листе «КП».
context.MailApp.quota = 100;
const before = sheets['КП'].data[1][21];
assert.strictEqual(call({ action: 'sendEmail', kind: 'invoice', invoiceNumber: 50, quoteId: 'q1', to: 'a@b.ru', subject: 'Счёт № 50', body: '', name: 'Счёт_50.pdf', data: 'JVBERg==' }).ok, true);
assert.strictEqual(sheets['КП'].data[1][21], before);

// ---- МойСклад (имитация JSON API 1.2).
const MS = 'https://api.moysklad.ru/api/remap/1.2';
const msDb = {
  organization: [{ id: 'org1', name: 'ООО "СОЛВЕР"', inn: '' }],
  store: [{ id: 'st1', name: 'Электросталь' }],
  counterparty: [{ id: 'c1', name: 'ООО Старый', inn: '7700000001' }],
  service: [],
  customerorder: [],
  invoiceout: [],
  paymentin: [],
  paymentout: [{ id: 'po1', moment: '2099-01-02 10:00:00.000', sum: 3000000 }, { id: 'po0', moment: '2000-01-01 10:00:00.000', sum: 1 }],
  invoicein: [
    { id: 'ii1', name: '00012', sum: 12000000, payedSum: 2000000, paymentPlannedMoment: '2099-01-10 00:00:00.000' },
    { id: 'ii2', name: '00013', sum: 500000, payedSum: 500000, paymentPlannedMoment: '2099-01-11 00:00:00.000' },
    { id: 'ii0', name: '00001', sum: 100, payedSum: 0, paymentPlannedMoment: '2000-01-01 00:00:00.000' },
  ],
  states: [{ id: 'sw', name: 'В работе', meta: { href: MS + '/entity/customerorder/metadata/states/sw' } }],
  product: [
    { id: 'p1', name: 'Подвяз 1×1 белый 14×100', article: '11-001', pathName: 'Подвязы', weight: 84.9, barcodes: [{ ean13: '2000000000015' }], buyPrice: { value: 14395 }, minPrice: { value: 14395 },
      salePrices: [['1 штук', 20153], ['10 штук', 19433], ['20 штук', 18713], ['50 штук', 17274], ['от 100 штук', 16554], ['от 500 штук', 15834], ['Премиум', 28790]]
        .map(([n, v]) => ({ value: v, priceType: { name: n } })) },
    { id: 'p2', name: 'Без цены', salePrices: [{ value: 0, priceType: { name: '1 штук' } }] },
    // Товар с модификациями: в каталог попадают модификации, сам товар — нет.
    { id: 'p3', name: 'Подвяз двуслойный 1х1', article: 'P3', pathName: 'Подвязы', buyPrice: { value: 9000 },
      attributes: [{ name: 'Популярный в категории', value: true }],
      salePrices: [{ value: 15000, priceType: { name: '1 штук' } }, { value: 12000, priceType: { name: 'от 500 штук' } }] },
  ],
  variant: [
    { id: 'v1', product: { meta: { href: MS + '/entity/product/p3' } }, salePrices: [],
      characteristics: [{ name: 'Цвет', value: 'бордовый / белый' }, { name: 'Размер', value: '115х14 см' }, { name: 'Тип резинки', value: '1х1' },
        { name: 'Артикул', value: '10252211693' }, { name: 'Уход (рекомендации)', value: 'Стирка 30' }, { name: 'Метка 1', value: 'Топ-продажа' },
        { name: 'Состав / материала', value: 'хлопок 95% резинка 5%' }] },
    { id: 'v2', product: { meta: { href: MS + '/entity/product/p3' } }, salePrices: [{ value: 16000, priceType: { name: '1 штук' } }],
      barcodes: [{ code128: 'X1' }, { ean13: '2900000000018' }],
      characteristics: [{ name: 'Цвет', value: 'чёрный' }, { name: 'Размер', value: '115х16 см' }] },
  ],
};
const msCalls = [];
let msSeq = 0;
function msRespond(code, body) { return { getResponseCode: () => code, getContentText: () => (body === undefined ? '' : JSON.stringify(body)) }; }
context.UrlFetchApp.fetch = (url, options) => {
  const method = options.method;
  const body = options.payload ? JSON.parse(options.payload) : undefined;
  if (url.indexOf('https://suggestions.dadata.ru/') === 0) {
    if (options.headers.Authorization !== 'Token dd-key') return msRespond(403, {});
    if (body.query === '9705239429') {
      return msRespond(200, { suggestions: [{ value: 'ООО "СОЛВЕР"', data: {
        inn: '9705239429', kpp: '772301001', ogrn: '1257700099832', state: { status: 'ACTIVE' },
        name: { short_with_opf: 'ООО "СОЛВЕР"', full_with_opf: 'ОБЩЕСТВО С ОГРАНИЧЕННОЙ ОТВЕТСТВЕННОСТЬЮ "СОЛВЕР"' },
        management: { name: 'Соланджи Басил', post: 'ГЕНЕРАЛЬНЫЙ ДИРЕКТОР' },
        address: { value: 'г Москва, ул Чагинская, д 4', unrestricted_value: '109380, г Москва, ул Чагинская, д 4' } } }] });
    }
    return msRespond(200, { suggestions: [] });
  }
  msCalls.push({ method, url, body });
  assert.ok(options.headers.Authorization === 'Bearer tok' && options.headers['Accept-Encoding'] === 'gzip');
  const [path, query = ''] = url.replace(MS, '').split('?');
  const params = Object.fromEntries(query.split('&').filter(Boolean).map((kv) => kv.split('=').map(decodeURIComponent)).map(([k, ...v]) => [k, v.join('=')]));
  const filter = params.filter || '';
  const parts = path.split('/').filter(Boolean); // entity, type, id, sub
  const list = (rows) => msRespond(200, { rows: rows.slice(Number(params.offset || 0), Number(params.offset || 0) + Number(params.limit || 1000)) });
  const match = (rows) => {
    if (!filter) return rows;
    const m = filter.match(/^(\w+)(>=|=|~)(.*)$/);
    if (m[1] === 'archived') return rows;
    if (m[2] === '>=') return rows.filter((r) => String(r[m[1]] || '') >= m[3]);
    return rows.filter((r) => (m[2] === '=' ? String(r[m[1]] || '') === m[3] : String(r[m[1]] || '').includes(m[3])));
  };
  if (path === '/report/stock/bystore/current') return msRespond(200, [{ assortmentId: 'p1', storeId: 'st1', stock: 120 }]);
  if (path === '/report/money/byaccount') return msRespond(200, { rows: [{ balance: 50000000 }, { balance: 2500000 }] });
  if (path === '/entity/assortment') {
    const code = filter.replace(/^barcode=/, '');
    return list(msDb.product.concat(msDb.variant).filter((r) => (r.barcodes || []).some((b) => Object.values(b).includes(code))));
  }
  if (path === '/entity/customerorder/metadata') return msRespond(200, { states: msDb.states });
  if (path === '/entity/customerorder/metadata/states' && method === 'post') {
    const st = Object.assign({ id: 'state' + ++msSeq }, body);
    st.meta = { href: MS + '/entity/customerorder/metadata/states/' + st.id };
    msDb.states.push(st);
    return msRespond(200, st);
  }
  const type = parts[1];
  const rows = msDb[type];
  if (parts.length === 2 && method === 'get') return list(match(rows));
  if (parts.length === 2 && method === 'post') {
    const id = type + ++msSeq;
    const doc = Object.assign({ id, name: body.name || String(1000 + msSeq), meta: { href: MS + '/entity/' + type + '/' + id, type } }, body);
    if (type === 'customerorder') doc.sum = body.positions.reduce((a, p) => a + p.quantity * p.price, 0);
    if (type === 'customerorder') doc.payedSum = 0;
    rows.push(doc);
    return msRespond(200, doc);
  }
  const doc = rows.find((r) => r.id === parts[2]);
  if (!doc) return msRespond(404, { errors: [{ error: 'Не найдено' }] });
  if (parts[3] === 'positions') return list(doc.positions);
  if (method === 'put') { Object.assign(doc, body); if (body.positions) doc.sum = body.positions.reduce((a, p) => a + p.quantity * p.price, 0); return msRespond(200, doc); }
  if (method === 'delete') { rows.splice(rows.indexOf(doc), 1); return msRespond(200); }
  return msRespond(200, doc);
};

// Без токена МойСклад не трогается.
assert.strictEqual(get({ action: 'msCatalog' }).ms.enabled, false);
assert.strictEqual(msCalls.length, 0);
context.PropertiesService.props.MS_TOKEN = 'tok';

// Каталог: цены по тиражам из типов цен, «Премиум» игнорируется, остатки склада «Электросталь».
const msCat = get({ action: 'msCatalog' }).ms;
assert.strictEqual(msCat.store, 'Электросталь');
assert.deepStrictEqual(msCat.products.map((p) => p.id), ['p1', 'v1', 'v2']);
assert.deepStrictEqual(msCat.products[0].tiers.map((t) => [t.from, t.price]), [[1, 201.53], [10, 194.33], [20, 187.13], [50, 172.74], [100, 165.54], [500, 158.34]]);
assert.strictEqual(msCat.products[0].stock, 120);
assert.strictEqual(msCat.products[0].buyPrice, 143.95);
// Модификация: цены товара (своих нет), только нужные характеристики, значки «Топ-продажа» и «Популярный».
const v1 = msCat.products[1];
assert.strictEqual(v1.type, 'variant');
assert.strictEqual(v1.name, 'Подвяз двуслойный 1х1');
assert.deepStrictEqual(v1.tiers.map((t) => [t.from, t.price]), [[1, 150], [500, 120]]);
assert.deepStrictEqual(v1.chars, { 'Цвет': 'бордовый / белый', 'Размер': '115х14 см', 'Тип резинки': '1х1', 'Артикул': '10252211693', 'Состав / материала': 'хлопок 95% резинка 5%' });
assert.deepStrictEqual(v1.badges, ['Топ-продажа', 'Популярный']);
assert.strictEqual(v1.article, '10252211693');
assert.strictEqual(v1.buyPrice, 90);
// Своя цена модификации важнее цены товара.
assert.deepStrictEqual(msCat.products[2].tiers.map((t) => [t.from, t.price]), [[1, 160]]);
assert.deepStrictEqual(msCat.filters, ['Артикул', 'Цвет', 'Тип резинки', 'Тип', 'Артикул производитель']);
assert.strictEqual(msCat.clients[0].inn, '7700000001');

// КП → «Заказ покупателя»: новый клиент по ИНН, статус «Отправлено» создаётся, позиция под заказ — услугой.
const msQuote = Object.assign({}, quote, {
  id: 'q-ms', client: 'ООО Новый', inn: '7711111111', comment: 'Срочно',
  lines: [{ msId: 'p1', product: 'Подвяз', qty: 600, unit: 'шт', price: 158.34 }, { product: 'Воротник поло', params: '40×9', qty: 150, unit: 'шт', price: 120 }],
});
const savedMs = call({ action: 'saveQuote', quote: msQuote });
assert.strictEqual(savedMs.ok, true);
assert.strictEqual(savedMs.ms.name, 'КП-' + savedMs.number);
const msOrder = msDb.customerorder[0];
assert.strictEqual(msOrder.externalCode, 'q-ms');
assert.strictEqual(msDb.counterparty.find((c) => c.inn === '7711111111').name, 'ООО Новый');
assert.ok(msOrder.state.meta.href.endsWith('/metadata/states/' + msDb.states.find((x) => x.name === 'Отправлено').id));
assert.strictEqual(msOrder.positions[0].price, 15834);
assert.strictEqual(msOrder.positions[1].assortment.meta.type, 'service');
assert.ok(msOrder.description.includes('Воротник поло, 40×9 — 150 шт'));
assert.strictEqual(msDb.service[0].name, 'Трикотажные изделия по ТЗ');
assert.strictEqual(sheets['КП'].data.find((r) => r[11] === 'q-ms')[22], 'КП-' + savedMs.number);
// Повторное сохранение обновляет тот же заказ.
call({ action: 'saveQuote', quote: Object.assign({}, msQuote, { lines: [msQuote.lines[0]] }) });
assert.strictEqual(msDb.customerorder.length, 1);
assert.strictEqual(msDb.customerorder[0].positions.length, 1);

// Статус → в МойСклад (существующий «В работе» не дублируется).
const st = call({ action: 'setStatus', id: 'q-ms', status: 'В работе' });
assert.strictEqual(st.ms.state, 'В работе');
assert.ok(msDb.customerorder[0].state.meta.href.endsWith('/sw'));
assert.strictEqual(msDb.states.filter((x) => x.name === 'В работе').length, 1);

// Счёт: предоплата — одной строкой (услуга), номер — из МойСклад.
const invMs = call({ action: 'addInvoice', invoice: { quoteId: 'q-ms', quoteNumber: savedMs.number, client: 'ООО Новый', amount: 47502, purpose: 'Предоплата 50 %' } });
const msInvDoc = msDb.invoiceout[0];
assert.strictEqual(invMs.number, parseInt(msInvDoc.name, 10));
assert.strictEqual(msInvDoc.positions.length, 1);
assert.strictEqual(msInvDoc.positions[0].price, 4750200);
assert.strictEqual(msInvDoc.customerOrder.meta.href, msDb.customerorder[0].meta.href);
// Вся сумма — позициями заказа.
call({ action: 'addInvoice', invoice: { quoteId: 'q-ms', amount: 95004, purpose: 'Оплата' } });
assert.strictEqual(msDb.invoiceout[1].positions[0].assortment.meta.href, MS + '/entity/product/p1');

// Оплата → входящий платёж, привязанный к заказу; удаление удаляет и платёж.
const payMs = call({ action: 'addPayment', payment: { id: 'pm1', quoteId: 'q-ms', quoteNumber: savedMs.number, client: 'ООО Новый', amount: 47502 } });
assert.strictEqual(msDb.paymentin[0].operations[0].linkedSum, 4750200);
assert.strictEqual(payMs.ms.id, msDb.paymentin[0].id);
assert.strictEqual(sheets['Оплаты'].data.find((r) => r[7] === 'pm1')[8], msDb.paymentin[0].id);
call({ action: 'deletePayment', id: 'pm1' });
assert.strictEqual(msDb.paymentin.length, 0);

// Долги — из заказов МойСклад.
msDb.customerorder[0].payedSum = 4750200;
const opsMs = get({ action: 'ops' }).ops.ms.orders;
assert.deepStrictEqual(opsMs.map((o) => [o.quoteId, o.sum, o.paid]), [['q-ms', 95004, 47502]]);

// Ошибка МойСклад не ломает сохранение КП в таблицу.
const realFetch = context.UrlFetchApp.fetch;
context.UrlFetchApp.fetch = () => msRespond(403, { errors: [{ error: 'Нет прав' }] });
const failed = call({ action: 'saveQuote', quote: Object.assign({}, msQuote, { id: 'q-ms2' }) });
assert.strictEqual(failed.ok, true);
assert.ok(failed.number > 0);
assert.ok(/нет прав/.test(failed.ms.error));
context.UrlFetchApp.fetch = realFetch;

// Штрихкоды EAN-13: из карточки; нет — создаётся внутренний (префикс 2), занятый код пропускается.
assert.deepStrictEqual(msCat.products.map((p) => p.barcode), ['2000000000015', '', '2900000000018']);
assert.strictEqual(call({ action: 'msBarcode', msType: 'product', msId: 'p1' }).barcode, '2000000000015');
const made = call({ action: 'msBarcode', msType: 'variant', msId: 'v1' });
assert.strictEqual(made.barcode, '2900000000025');
assert.deepStrictEqual(msDb.variant[0].barcodes, [{ ean13: '2900000000025' }]);
assert.strictEqual(call({ action: 'msBarcode', msType: 'variant', msId: 'v1' }).barcode, '2900000000025');
assert.ok(/Неверный товар/.test(call({ action: 'msBarcode', msType: 'counterparty', msId: 'c1' }).error));

// DaData: без ключа — понятная ошибка; с ключом — реквизиты по ИНН.
assert.ok(/DADATA_KEY/.test(call({ action: 'innLookup', inn: '9705239429' }).error));
context.PropertiesService.props.DADATA_KEY = 'dd-key';
const party = call({ action: 'innLookup', inn: '9705239429' }).party;
assert.strictEqual(party.name, 'ООО "СОЛВЕР"');
assert.strictEqual(party.kpp, '772301001');
assert.strictEqual(party.address, '109380, г Москва, ул Чагинская, д 4');
assert.strictEqual(party.director, 'Соланджи Басил');
assert.ok(/не найдена/.test(call({ action: 'innLookup', inn: '7726358110' }).error));
assert.ok(/10 или 12/.test(call({ action: 'innLookup', inn: '123' }).error));
context.PropertiesService.props.DADATA_KEY = 'wrong';
assert.ok(/неверный ключ/.test(call({ action: 'innLookup', inn: '9705239429' }).error));
context.PropertiesService.props.DADATA_KEY = 'dd-key';

// Позиция-модификация → в заказ как variant.
call({ action: 'saveQuote', quote: Object.assign({}, msQuote, { id: 'q-var', lines: [{ msId: 'v1', msType: 'variant', product: 'Подвяз', qty: 10, price: 150 }] }) });
assert.strictEqual(msDb.customerorder.find((o) => o.externalCode === 'q-var').positions[0].assortment.meta.href, MS + '/entity/variant/v1');

// ---- Роли: у менеджера свой ключ; себестоимость, PIN и закрытые настройки — только директору.
sheets['Настройки'].data.push(['PIN директора', '4321'], ['Постоянные расходы в месяц, ₽', '954000']);
sheets['Себестоимость'] = makeSheet('Себестоимость', [['Код'], ['PODV', '50']]);
sheets['Менеджеры'] = makeSheet('Менеджеры', [['Ключ', 'Имя', 'Роль', 'Активен'], ['m-key', 'Иван', 'менеджер', 'да'], ['old-key', 'Пётр', 'менеджер', 'нет']]);
const asManager = (body) => JSON.parse(api.doPost({ postData: { contents: JSON.stringify(Object.assign({ key: 'm-key' }, body)) } }).text);
const mCat = asManager({ action: 'catalog' });
assert.strictEqual(mCat.ok, true, mCat.error);
assert.strictEqual(mCat.role, 'manager');
assert.strictEqual(mCat.manager, 'Иван');
const mSettings = mCat.sheets.settings.map((r) => r[0]);
assert.ok(!mSettings.includes('PIN директора') && !mSettings.includes('Ключ доступа') && !mSettings.includes('Постоянные расходы в месяц, ₽'));
assert.ok(mSettings.includes('ИНН') || mSettings.includes('E-mail'));
assert.deepStrictEqual(mCat.sheets.costs, []);
assert.strictEqual(call({ action: 'catalog' }).role, 'director');
assert.ok(call({ action: 'catalog' }).sheets.costs.length > 0);
// Менеджеру: прибыль не видна, закупочных цен МойСклад нет, удалять оплаты нельзя.
assert.ok(asManager({ action: 'quotes' }).quotes.every((q) => q.profit === null));
assert.ok(asManager({ action: 'msCatalog' }).ms.products.every((p) => p.buyPrice === undefined));
assert.ok(/директора/.test(asManager({ action: 'deletePayment', id: 'x' }).error));
// Отключённый ключ и чужой ключ — отказ.
assert.ok(/отключён/.test(JSON.parse(api.doPost({ postData: { contents: JSON.stringify({ key: 'old-key', action: 'ping' }) } }).text).error));
assert.ok(/Неверный ключ/.test(JSON.parse(api.doPost({ postData: { contents: JSON.stringify({ key: 'zzz', action: 'ping' }) } }).text).error));

// ---- Отчёт за месяц — все КП месяца; долги — все КП без данных черновика.
const month = get({ action: 'quotes' }).quotes[0].month;
const byMonth = call({ action: 'quotes', month });
assert.ok(byMonth.quotes.length >= 2 && byMonth.quotes.every((q) => q.month === month));
assert.strictEqual(call({ action: 'quotes', month: '1999-01' }).quotes.length, 0);
const light = call({ action: 'quotes', light: true }).quotes;
assert.strictEqual(light.length, sheets['КП'].data.length - 1);
assert.ok(light.every((q) => q.data === ''));

// ---- Скорость: кэш справочников (сброс правкой таблицы) и сжатие больших ответов.
context.CacheService.store = {};
const reads = { n: 0 };
const settingsSheet = sheets['Настройки'];
const realRange = settingsSheet.getDataRange.bind(settingsSheet);
settingsSheet.getDataRange = () => { reads.n++; return realRange(); };
call({ action: 'ping' });
const afterFirst = reads.n;
call({ action: 'ping' });
call({ action: 'catalog' });
assert.strictEqual(reads.n, afterFirst, 'настройки берутся из кэша');
settingsSheet.data.push(['Сайт', 'new.example']);
assert.ok(!call({ action: 'catalog' }).sheets.settings.some((r) => r[1] === 'new.example'), 'до правки — кэш');
api.onEdit({});
assert.ok(call({ action: 'catalog' }).sheets.settings.some((r) => r[1] === 'new.example'), 'после правки — свежие данные');
// Сжатие: большой ответ приходит как gz (base64 gzip), маленький — как есть.
for (let i = 0; i < 400; i++) sheets['Клиенты'].data.push(['ООО Клиент ' + i, 'Контакт ' + i, 'c' + i + '@example.ru', '+7 900 000-00-' + i, '7700000000', '']);
api.onEdit({});
const big = call({ action: 'catalog', gz: true });
assert.ok(big.gz && !big.sheets);
const raw = JSON.stringify(call({ action: 'catalog' }));
assert.ok(big.gz.length * 3 < raw.length, 'сжатие в 3+ раза: ' + big.gz.length + ' из ' + raw.length);
const unpacked = JSON.parse(require('zlib').gunzipSync(Buffer.from(big.gz, 'base64')).toString('utf8'));
assert.strictEqual(unpacked.ok, true);
assert.ok(unpacked.sheets.clients.length > 400);
assert.strictEqual(call({ action: 'ping', gz: true }).ok, true);
// Каталог МойСклад — из кэша (без запросов к МойСклад), «Обновить» — заново.
call({ action: 'msCatalog' }); // первый запрос заполняет кэш
const msBefore = msCalls.length;
call({ action: 'msCatalog' });
assert.strictEqual(msCalls.length, msBefore);
call({ action: 'msCatalog', fresh: true });
assert.ok(msCalls.length > msBefore);

// ---- Финансы: только директору; регулярные платежи, остаток, счета поставщиков МойСклад.
sheets['Регулярные платежи'] = makeSheet('Регулярные платежи', [
  ['Название', 'Сумма, ₽', 'День месяца', 'Категория', 'Активен'],
  ['Аренда', '150 000', 5, 'Аренда', 'да'],
  ['Кредит', 50000, 20, 'Кредит', 'нет'],
  ['Пустая', '', 10, '', 'да'],
]);
sheets['Настройки'].data.push(['План продаж в месяц, ₽', '3500000']);
api.onEdit({});
assert.ok(/директора/.test(asManager({ action: 'finance' }).error));
const fin = call({ action: 'finance' }).finance;
assert.strictEqual(fin.plan, 3500000);
assert.deepStrictEqual(fin.regular, [{ name: 'Аренда', amount: 150000, day: 5, category: 'Аренда' }]);
assert.strictEqual(fin.balance, 525000);
assert.strictEqual(fin.balanceSource, 'ms');
assert.deepStrictEqual(fin.supplier.map((x) => [x.name, x.amount]), [['Счёт поставщика № 00012', 100000]]);
assert.ok(fin.supplier[0].due > Date.UTC(2098, 0, 1));
assert.deepStrictEqual(fin.actualOut.map((x) => x.amount), [30000]);

// ---- Зарплата менеджеров: оклад 60 000 + 3 % от оплат месяца по КП менеджера.
const ym = new Date().toISOString().slice(0, 7);
const qRow = new Array(23).fill('');
qRow[0] = 900; qRow[10] = 'Иван / Pixel 8'; qRow[11] = 'qS';
sheets['КП'].data.push(qRow);
msDb.customerorder.push({ id: 'coS', name: 'КП-900', externalCode: 'qS', meta: { href: MS + '/entity/customerorder/coS' } });
msDb.paymentin.push({ id: 'piS', moment: ym + '-05 10:00:00.000', sum: 10000000, operations: [{ meta: { href: MS + '/entity/customerorder/coS' }, linkedSum: 10000000 }] });
msDb.paymentin.push({ id: 'piOld', moment: '2000-01-05 10:00:00.000', sum: 999, operations: [{ meta: { href: MS + '/entity/customerorder/coS' } }] });
const sal = call({ action: 'salary', month: ym }).salary;
assert.strictEqual(sal.source, 'ms');
assert.deepStrictEqual([sal.base, sal.percent], [60000, 3]);
const ivan = sal.rows.find((r) => r.name === 'Иван');
assert.deepStrictEqual([ivan.paid, ivan.bonus, ivan.total], [100000, 3000, 63000]);
assert.ok(!sal.rows.some((r) => r.name === 'Пётр'), 'отключённый менеджер не считается');
const mine = asManager({ action: 'salary', month: ym }).salary;
assert.deepStrictEqual(mine.rows.map((r) => r.name), ['Иван']);
// Без МойСклад — по листу «Оплаты».
delete context.PropertiesService.props.MS_TOKEN;
sheets['Оплаты'].data.push([new Date(), 900, 'qS', 'ООО', '50 000', '', '', 'pS', '']);
const salSheet = call({ action: 'salary', month: ym }).salary;
assert.strictEqual(salSheet.source, 'sheet');
assert.strictEqual(salSheet.rows.find((r) => r.name === 'Иван').bonus, 1500);
context.PropertiesService.props.MS_TOKEN = 'tok';

// ---- Журнал действий: успешные изменения записываются, ошибки и чтение — нет.
const journal = sheets['Журнал'];
assert.ok(journal, 'лист «Журнал» создан');
assert.deepStrictEqual(journal.data[0], ['Дата', 'Кто', 'Действие', 'Подробности']);
const kinds = journal.data.slice(1).map((r) => r[2]);
assert.ok(kinds.includes('КП сохранено') && kinds.includes('Оплата') && kinds.includes('Статус КП'));
assert.ok(!kinds.includes(undefined));
const beforeLog = journal.data.length;
call({ action: 'quotes' });
call({ action: 'setStatus', id: 'нет-такого', status: 'Оплачено' });
assert.strictEqual(journal.data.length, beforeLog, 'чтение и ошибки не пишутся');
asManager({ action: 'addYarnMoves', moves: [{ id: 'jm1', date: 1, yarn: 'Хлопок', kg: 5 }] });
const lastLog = journal.data[journal.data.length - 1];
assert.deepStrictEqual([lastLog[1], lastLog[2], lastLog[3]], ['Иван', 'Склад пряжи', 'Хлопок 5 кг']);

// ---- Резервная копия: копия в папку, хранятся последние 14.
ss.getId = () => 'ss1';
const backupFiles = [];
const folder = { getId: () => 'bf', getFiles: () => { let i = 0; return { hasNext: () => i < backupFiles.length, next: () => backupFiles[i++] }; } };
context.DriveApp.getFoldersByName = () => ({ hasNext: () => false });
context.DriveApp.createFolder = () => folder;
const realGetFolder = context.DriveApp.getFolderById;
context.DriveApp.getFolderById = (id) => (id === 'bf' ? folder : realGetFolder(id));
context.DriveApp.getFileById = (id) => ({
  getBlob: () => ({ id }),
  makeCopy: (name, f) => {
    assert.strictEqual(f, folder);
    const file = { name, trashed: false, created: backupFiles.length, getDateCreated: () => new Date(file.created), setTrashed(v) { file.trashed = v; } };
    backupFiles.push(file);
    return file;
  },
});
for (let i = 0; i < 16; i++) api.backupNow_();
assert.strictEqual(backupFiles.length, 16);
assert.strictEqual(backupFiles.filter((f) => f.trashed).length, 2);
assert.ok(backupFiles[0].trashed && backupFiles[1].trashed && !backupFiles[15].trashed);
assert.ok(/^Тест — копия /.test(backupFiles[0].name));

console.log('Apps Script: все проверки пройдены');
