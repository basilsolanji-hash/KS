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
    newBlob: (bytes, mime, name) => ({ bytes, mime, name }),
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
const fn = new Function(...Object.keys(context), code + '\nreturn { doGet, doPost };');
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
const mail = call({ action: 'sendEmail', quoteId: 'q1', to: 'client@example.ru', subject: 'КП № 100', body: 'Текст', name: 'KP_100.pdf', data: 'JVBERg==' });
assert.strictEqual(mail.ok, true, mail.error);
const sent = context.MailApp.sent[0];
assert.strictEqual(sent.to, 'client@example.ru');
assert.strictEqual(sent.options.replyTo, 'Sale@fabrika-ks.ru');
assert.strictEqual(sent.options.cc, 'Sale@fabrika-ks.ru');
assert.strictEqual(sent.options.name, 'Фабрика "KS"');
assert.strictEqual(sent.options.attachments.length, 1);
assert.ok(String(sheets['КП'].data[1][21]).endsWith('→ client@example.ru'));
assert.strictEqual(sheets['КП'].data[1][20], 'https://drive/f1');
assert.strictEqual(sheets['КП'].getMaxColumns(), 22);
// Плохой адрес и исчерпанный лимит — понятная ошибка, письмо не уходит.
assert.strictEqual(call({ action: 'sendEmail', to: 'не адрес', data: '' }).ok, false);
context.MailApp.quota = 0;
assert.ok(/Лимит/.test(call({ action: 'sendEmail', to: 'client@example.ru', data: '' }).error));
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
assert.strictEqual(call({ action: 'sendEmail', kind: 'invoice', invoiceNumber: 50, to: 'client@example.ru', subject: 'Счёт № 50', body: '', name: 'Счёт_50.pdf', data: 'JVBERg==' }).ok, true);
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
  states: [{ id: 'sw', name: 'В работе', meta: { href: MS + '/entity/customerorder/metadata/states/sw' } }],
  product: [
    { id: 'p1', name: 'Подвяз 1×1 белый 14×100', article: '11-001', pathName: 'Подвязы', weight: 84.9, buyPrice: { value: 14395 }, minPrice: { value: 14395 },
      salePrices: [['1 штук', 20153], ['10 штук', 19433], ['20 штук', 18713], ['50 штук', 17274], ['от 100 штук', 16554], ['от 500 штук', 15834], ['Премиум', 28790]]
        .map(([n, v]) => ({ value: v, priceType: { name: n } })) },
    { id: 'p2', name: 'Без цены', salePrices: [{ value: 0, priceType: { name: '1 штук' } }] },
  ],
};
const msCalls = [];
let msSeq = 0;
function msRespond(code, body) { return { getResponseCode: () => code, getContentText: () => (body === undefined ? '' : JSON.stringify(body)) }; }
context.UrlFetchApp.fetch = (url, options) => {
  const method = options.method;
  const body = options.payload ? JSON.parse(options.payload) : undefined;
  msCalls.push({ method, url, body });
  assert.ok(options.headers.Authorization === 'Bearer tok' && options.headers['Accept-Encoding'] === 'gzip');
  const [path, query = ''] = url.replace(MS, '').split('?');
  const params = Object.fromEntries(query.split('&').filter(Boolean).map((kv) => kv.split('=').map(decodeURIComponent)).map(([k, ...v]) => [k, v.join('=')]));
  const filter = params.filter || '';
  const parts = path.split('/').filter(Boolean); // entity, type, id, sub
  const list = (rows) => msRespond(200, { rows: rows.slice(Number(params.offset || 0), Number(params.offset || 0) + Number(params.limit || 1000)) });
  const match = (rows) => {
    if (!filter) return rows;
    const m = filter.match(/^(\w+)(=|~)(.*)$/);
    if (m[1] === 'archived') return rows;
    return rows.filter((r) => (m[2] === '=' ? String(r[m[1]] || '') === m[3] : String(r[m[1]] || '').includes(m[3])));
  };
  if (path === '/report/stock/bystore/current') return msRespond(200, [{ assortmentId: 'p1', storeId: 'st1', stock: 120 }]);
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
assert.strictEqual(msCat.products.length, 1);
assert.deepStrictEqual(msCat.products[0].tiers.map((t) => [t.from, t.price]), [[1, 201.53], [10, 194.33], [20, 187.13], [50, 172.74], [100, 165.54], [500, 158.34]]);
assert.strictEqual(msCat.products[0].stock, 120);
assert.strictEqual(msCat.products[0].buyPrice, 143.95);
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

console.log('Apps Script: все проверки пройдены');
