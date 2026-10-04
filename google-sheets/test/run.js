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

console.log('Apps Script: все проверки пройдены');
