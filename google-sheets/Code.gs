/**
 * Фабрика "KS" — связь приложения с этой Google Таблицей и папкой на Google Диске.
 *
 * Установка (один раз, с компьютера):
 *  1. В таблице: Расширения → Apps Script. Удалите пример кода и вставьте этот файл целиком. Сохраните.
 *  2. Развернуть → Новое развертывание → Тип: «Веб-приложение».
 *     Выполнять от имени: «Я». У кого есть доступ: «Все». Нажмите «Развернуть» и разрешите доступ к таблице и Диску.
 *  3. Скопируйте URL веб-приложения (…/exec) и вставьте его в приложении:
 *     Настройки → «Google Таблица». Там же введите «Ключ доступа» из листа «Настройки».
 *
 * После изменения кода: Развернуть → Управление развертываниями → ✎ → Версия «Новая версия».
 */

var SHEETS = {
  products: 'Изделия',
  parameters: 'Параметры',
  volume: 'Объём',
  settings: 'Настройки',
  quotes: 'КП',
  items: 'Позиции КП',
  costs: 'Себестоимость',
  yarns: 'Пряжа',
  clients: 'Клиенты',
};
var KEY_SETTING = 'Ключ доступа';
var START_NUMBER_SETTING = 'Начальный номер КП';
var FOLDER_SETTINGS = { logo: 'Папка: логотип (ID)', photo: 'Папка: фото (ID)', pdf: 'Папка: КП (ID)' };
// Колонки листа «КП» (с 1).
var Q = {
  number: 1, date: 2, client: 3, contact: 4, email: 5, subtotal: 6, vat: 7, total: 8, delivery: 9,
  lines: 10, author: 11, id: 12, data: 13, status: 14, statusDate: 15, cost: 16, profit: 17,
  validUntil: 18, phone: 19, inn: 20, pdf: 21,
};
var STATUSES = ['Отправлено', 'Согласовано', 'В работе', 'Оплачено', 'Отказ'];

function doGet(e) {
  return handle_((e && e.parameter) || {});
}

function doPost(e) {
  var body;
  try {
    body = JSON.parse(e.postData.contents);
  } catch (err) {
    return json_({ ok: false, error: 'Некорректный запрос' });
  }
  return handle_(body);
}

function handle_(req) {
  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var key = String(settings_(ss)[KEY_SETTING] || '').trim();
    if (key && String(req.key || '').trim() !== key) {
      return json_({ ok: false, error: 'Неверный ключ доступа' });
    }
    switch (req.action) {
      case 'ping':
        return json_({ ok: true, name: ss.getName(), url: ss.getUrl() });
      case 'catalog':
        return json_({
          ok: true,
          name: ss.getName(),
          url: ss.getUrl(),
          sheets: {
            products: rows_(ss, SHEETS.products),
            parameters: rows_(ss, SHEETS.parameters),
            volume: rows_(ss, SHEETS.volume),
            settings: rows_(ss, SHEETS.settings),
            costs: optionalRows_(ss, SHEETS.costs),
            yarns: optionalRows_(ss, SHEETS.yarns),
            clients: optionalRows_(ss, SHEETS.clients),
          },
          logo: logo_(ss, String(req.logoVersion || '')),
        });
      case 'quotes':
        return json_({ ok: true, quotes: listQuotes_(ss, Number(req.limit) || 50) });
      case 'saveQuote':
        return json_({ ok: true, number: saveQuote_(ss, req.quote || {}) });
      case 'uploadFile':
        return json_({ ok: true, file: uploadFile_(ss, req) });
      case 'getFile':
        return json_({ ok: true, data: getFile_(ss, String(req.fileId || '')) });
      case 'setStatus':
        setStatus_(ss, String(req.id || ''), String(req.status || ''));
        return json_({ ok: true });
      default:
        return json_({ ok: false, error: 'Неизвестное действие: ' + req.action });
    }
  } catch (err) {
    return json_({ ok: false, error: String((err && err.message) || err) });
  }
}

/** Значения листа так, как они видны в таблице (строки). */
function rows_(ss, name) {
  var sheet = ss.getSheetByName(name);
  if (!sheet) throw new Error('Нет листа «' + name + '»');
  return sheet.getDataRange().getDisplayValues();
}

/** Необязательный лист: если его нет — пустой список. */
function optionalRows_(ss, name) {
  var sheet = ss.getSheetByName(name);
  return sheet ? sheet.getDataRange().getDisplayValues() : [];
}

function settings_(ss) {
  var sheet = ss.getSheetByName(SHEETS.settings);
  var result = {};
  if (!sheet) return result;
  sheet.getDataRange().getDisplayValues().slice(1).forEach(function (r) {
    if (r[0]) result[String(r[0]).trim()] = r[1];
  });
  return result;
}

/**
 * Сохраняет КП. Новое КП получает следующий номер (под блокировкой — номера не повторяются
 * даже при одновременной работе с нескольких телефонов). Повторное сохранение того же КП
 * (по ID) обновляет строку и сохраняет номер.
 */
function saveQuote_(ss, q) {
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  try {
    var sheet = ss.getSheetByName(SHEETS.quotes);
    var data = sheet.getDataRange().getValues();
    var rowIndex = -1;
    var maxNumber = 0;
    for (var i = 1; i < data.length; i++) {
      var n = Number(data[i][0]) || 0;
      if (n > maxNumber) maxNumber = n;
      if (q.id && String(data[i][Q.id - 1]) === String(q.id)) rowIndex = i + 1;
    }
    var number;
    var date = new Date();
    var status = STATUSES[0];
    var statusDate = date;
    if (rowIndex > 0) {
      var old = data[rowIndex - 1];
      number = Number(old[0]);
      date = old[Q.date - 1] || date;
      status = old[Q.status - 1] || status;
      statusDate = old[Q.statusDate - 1] || statusDate;
    } else {
      var start = Number(settings_(ss)[START_NUMBER_SETTING]) || 1;
      number = Math.max(maxNumber + 1, start);
    }
    var lines = q.lines || [];
    var validUntil = q.validUntil ? new Date(Number(q.validUntil)) : '';
    var row = [
      number, date, text_(q.client), text_(q.contact), text_(q.email),
      Number(q.subtotal) || 0, Number(q.vat) || 0, Number(q.total) || 0,
      text_(q.delivery), lines.length, text_(q.author), text_(q.id), text_(q.data),
      status, statusDate, num_(q.cost), num_(q.profit), validUntil, text_(q.phone), text_(q.inn),
    ];
    if (rowIndex > 0) {
      sheet.getRange(rowIndex, 1, 1, row.length).setValues([row]);
    } else {
      sheet.appendRow(row);
    }

    var items = ss.getSheetByName(SHEETS.items);
    var existing = items.getDataRange().getValues();
    for (var r = existing.length; r >= 2; r--) {
      if (Number(existing[r - 1][0]) === number) items.deleteRow(r);
    }
    var itemRows = lines.map(function (l) {
      return [number, date, text_(q.client), text_(l.code), text_(l.product), text_(l.params),
        Number(l.qty) || 0, text_(l.unit), Number(l.price) || 0, Number(l.sum) || 0, Number(l.discount) || 0];
    });
    if (itemRows.length) {
      items.getRange(items.getLastRow() + 1, 1, itemRows.length, itemRows[0].length).setValues(itemRows);
    }
    addClient_(ss, q);
    return number;
  } finally {
    lock.releaseLock();
  }
}

/** Новый клиент из КП попадает в лист «Клиенты» (если такой компании там ещё нет). */
function addClient_(ss, q) {
  var company = String(q.client || '').trim();
  var sheet = ss.getSheetByName(SHEETS.clients);
  if (!company || !sheet) return;
  var rows = sheet.getDataRange().getValues();
  for (var i = 1; i < rows.length; i++) {
    if (String(rows[i][0]).trim().toLowerCase() === company.toLowerCase()) return;
  }
  sheet.appendRow([text_(company), text_(q.contact), text_(q.email), text_(q.phone), text_(q.inn), new Date()]);
}

function setStatus_(ss, id, status) {
  if (STATUSES.indexOf(status) < 0) throw new Error('Неизвестный статус: ' + status);
  var sheet = ss.getSheetByName(SHEETS.quotes);
  var data = sheet.getDataRange().getValues();
  for (var i = 1; i < data.length; i++) {
    if (String(data[i][Q.id - 1]) === id) {
      sheet.getRange(i + 1, Q.status, 1, 2).setValues([[status, new Date()]]);
      return;
    }
  }
  throw new Error('КП не найдено в таблице');
}

function listQuotes_(ss, limit) {
  var sheet = ss.getSheetByName(SHEETS.quotes);
  var data = sheet.getDataRange().getValues();
  var tz = ss.getSpreadsheetTimeZone();
  var products = {};
  var items = ss.getSheetByName(SHEETS.items).getDataRange().getValues();
  for (var k = 1; k < items.length; k++) {
    var no = Number(items[k][0]);
    if (!no) continue;
    var name = String(items[k][4] || '');
    products[no] = products[no] || {};
    products[no][name] = (products[no][name] || 0) + (Number(items[k][9]) || 0);
  }
  var result = [];
  for (var i = data.length - 1; i >= 1 && result.length < limit; i--) {
    var r = data[i];
    if (!r[0]) continue;
    var date = r[Q.date - 1];
    var valid = r[Q.validUntil - 1];
    result.push({
      number: Number(r[0]),
      date: date instanceof Date ? Utilities.formatDate(date, tz, 'dd.MM.yyyy HH:mm') : String(date),
      month: date instanceof Date ? Utilities.formatDate(date, tz, 'yyyy-MM') : '',
      client: String(r[Q.client - 1] || ''),
      total: Number(r[Q.total - 1]) || 0,
      author: String(r[Q.author - 1] || ''),
      id: String(r[Q.id - 1] || ''),
      data: String(r[Q.data - 1] || ''),
      status: String(r[Q.status - 1] || STATUSES[0]),
      profit: r[Q.profit - 1] === '' ? null : Number(r[Q.profit - 1]),
      validUntil: valid instanceof Date ? valid.getTime() : 0,
      products: products[Number(r[0])] || {},
    });
  }
  return result;
}

function folder_(ss, kind) {
  var id = String(settings_(ss)[FOLDER_SETTINGS[kind]] || '').trim();
  if (!id) throw new Error('В «Настройках» не указана ' + FOLDER_SETTINGS[kind]);
  return DriveApp.getFolderById(id);
}

/** Самый новый рисунок в папке «Логотип». Данные отдаются, только если версия изменилась. */
function logo_(ss, knownVersion) {
  var id = String(settings_(ss)[FOLDER_SETTINGS.logo] || '').trim();
  if (!id) return null;
  var files = DriveApp.getFolderById(id).getFiles();
  var newest = null;
  while (files.hasNext()) {
    var f = files.next();
    if (f.getMimeType().indexOf('image/') !== 0) continue;
    if (!newest || f.getLastUpdated() > newest.getLastUpdated()) newest = f;
  }
  if (!newest) return { version: '' };
  var version = newest.getId() + ':' + newest.getLastUpdated().getTime();
  if (version === knownVersion) return { version: version };
  return {
    version: version,
    mime: newest.getMimeType(),
    data: Utilities.base64Encode(newest.getBlob().getBytes()),
  };
}

/** Фото образца или PDF КП → папка на Диске. Файл с тем же именем заменяется. */
function uploadFile_(ss, req) {
  var kind = req.kind === 'pdf' ? 'pdf' : 'photo';
  var folder = folder_(ss, kind);
  var name = String(req.name || (kind + '-' + Date.now()));
  var old = folder.getFilesByName(name);
  while (old.hasNext()) old.next().setTrashed(true);
  var blob = Utilities.newBlob(Utilities.base64Decode(String(req.data || '')), String(req.mime || 'application/octet-stream'), name);
  var file = folder.createFile(blob);
  if (kind === 'pdf' && req.quoteId) {
    var sheet = ss.getSheetByName(SHEETS.quotes);
    var data = sheet.getDataRange().getValues();
    for (var i = 1; i < data.length; i++) {
      if (String(data[i][Q.id - 1]) === String(req.quoteId)) {
        sheet.getRange(i + 1, Q.pdf).setValue(file.getUrl());
        break;
      }
    }
  }
  return { id: file.getId(), url: file.getUrl() };
}

/** Фото из папки «Фото образцов» — для открытия КП на другом телефоне. */
function getFile_(ss, fileId) {
  var file = DriveApp.getFileById(fileId);
  var allowed = folder_(ss, 'photo').getId();
  var parents = file.getParents();
  var ok = false;
  while (parents.hasNext()) if (parents.next().getId() === allowed) ok = true;
  if (!ok) throw new Error('Файл не из папки фото');
  return Utilities.base64Encode(file.getBlob().getBytes());
}

function num_(value) {
  return value === null || value === undefined || value === '' ? '' : Number(value);
}

/** Текст как есть: значения, начинающиеся с = + - @ (например, «+7 985…»), не превращаются в формулы. */
function text_(value) {
  var s = value == null ? '' : String(value);
  return /^[=+\-@]/.test(s) ? "'" + s : s;
}

function json_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}
