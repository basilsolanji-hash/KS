/**
 * Knit ERP — связь приложения «Калькулятор Knit ERP» с этой Google Таблицей.
 *
 * Установка (один раз, с компьютера):
 *  1. В таблице: Расширения → Apps Script. Удалите пример кода и вставьте этот файл целиком. Сохраните.
 *  2. Развернуть → Новое развертывание → Тип: «Веб-приложение».
 *     Выполнять от имени: «Я». У кого есть доступ: «Все». Нажмите «Развернуть» и разрешите доступ.
 *  3. Скопируйте URL веб-приложения (…/exec) и вставьте его в приложении:
 *     КП → шестерёнка → «Google Таблица». Там же введите «Ключ доступа» из листа «Настройки».
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
};
var KEY_SETTING = 'Ключ доступа';
var START_NUMBER_SETTING = 'Начальный номер КП';
var QUOTE_ID_COLUMN = 12; // L
var QUOTE_DATE_COLUMN = 2; // B

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
          },
        });
      case 'quotes':
        return json_({ ok: true, quotes: listQuotes_(ss, Number(req.limit) || 50) });
      case 'saveQuote':
        return json_({ ok: true, number: saveQuote_(ss, req.quote || {}) });
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
      if (q.id && String(data[i][QUOTE_ID_COLUMN - 1]) === String(q.id)) rowIndex = i + 1;
    }
    var number;
    var date = new Date();
    if (rowIndex > 0) {
      number = Number(data[rowIndex - 1][0]);
      date = data[rowIndex - 1][QUOTE_DATE_COLUMN - 1] || date;
    } else {
      var start = Number(settings_(ss)[START_NUMBER_SETTING]) || 1;
      number = Math.max(maxNumber + 1, start);
    }
    var lines = q.lines || [];
    var row = [
      number, date, q.client || '', q.contact || '', q.email || '',
      Number(q.subtotal) || 0, Number(q.vat) || 0, Number(q.total) || 0,
      q.delivery || '', lines.length, q.author || '', q.id || '', q.data || '',
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
      return [number, date, q.client || '', l.code || '', l.product || '', l.params || '',
        Number(l.qty) || 0, l.unit || '', Number(l.price) || 0, Number(l.sum) || 0];
    });
    if (itemRows.length) {
      items.getRange(items.getLastRow() + 1, 1, itemRows.length, itemRows[0].length).setValues(itemRows);
    }
    return number;
  } finally {
    lock.releaseLock();
  }
}

function listQuotes_(ss, limit) {
  var sheet = ss.getSheetByName(SHEETS.quotes);
  var data = sheet.getDataRange().getValues();
  var tz = ss.getSpreadsheetTimeZone();
  var result = [];
  for (var i = data.length - 1; i >= 1 && result.length < limit; i--) {
    var r = data[i];
    if (!r[0]) continue;
    result.push({
      number: Number(r[0]),
      date: r[1] instanceof Date ? Utilities.formatDate(r[1], tz, 'dd.MM.yyyy HH:mm') : String(r[1]),
      client: String(r[2] || ''),
      total: Number(r[7]) || 0,
      author: String(r[10] || ''),
      id: String(r[11] || ''),
      data: String(r[12] || ''),
    });
  }
  return result;
}

function json_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}
