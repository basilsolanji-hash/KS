/**
 * Фабрика "KS" — связь приложения с этой Google Таблицей и папкой на Google Диске.
 *
 * Установка (один раз, с компьютера):
 *  1. В таблице: Расширения → Apps Script. Удалите пример кода и вставьте этот файл целиком. Сохраните.
 *  2. Развернуть → Новое развертывание → Тип: «Веб-приложение».
 *     Выполнять от имени: «Я». У кого есть доступ: «Все». Нажмите «Развернуть» и разрешите доступ к таблице,
 *     Диску и отправке почты (письма клиентам уходят с вашего аккаунта Google).
 *  3. Скопируйте URL веб-приложения (…/exec) и вставьте его в приложении:
 *     Настройки → «Google Таблица». Там же введите «Ключ доступа» из листа «Настройки».
 *
 * После изменения кода: Развернуть → Управление развертываниями → ✎ → Версия «Новая версия».
 * Если Google снова спросит разрешения (например, на отправку почты) — разрешите.
 *
 * МойСклад: в таблице появится меню «Фабрика KS → Подключить МойСклад…». Вставьте токен
 * (МойСклад → Настройки → Обмен данными → Токены, у пользователя «Приложение KS»). Токен хранится
 * в свойствах скрипта, а не в листах — на телефоны он не попадает.
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
  invoices: 'Счета',
  payments: 'Оплаты',
  orders: 'Заказы',
  yarnMoves: 'Движение пряжи',
  contract: 'Договор',
  shifts: 'Рабочее время',
};
// Листы учёта создаются сами при первой записи.
var HEADERS = {
  invoices: ['№ счёта', 'Дата', '№ КП', 'ID КП', 'Клиент', 'Сумма, ₽', 'Назначение', 'PDF'],
  payments: ['Дата', '№ КП', 'ID КП', 'Клиент', 'Сумма, ₽', 'Комментарий', 'Менеджер', 'ID', 'МойСклад'],
  orders: ['№ КП', 'ID КП', 'Клиент', 'Создан', 'Срок отгрузки', 'Этап', 'Дата этапа', 'Изделия', 'Комментарий',
    'Пряжа (не изменять)', 'Пряжа списана'],
  yarnMoves: ['Дата', 'Пряжа', 'Кг (+ приход, − расход)', 'Основание', 'Менеджер', 'ID'],
  shifts: ['ID', 'Сотрудник', 'Начало', 'Конец', 'Часов', 'Исправил', 'Получено сервером'],
};
var START_INVOICE_SETTING = 'Начальный номер счёта';
var KEY_SETTING = 'Ключ доступа';
var START_NUMBER_SETTING = 'Начальный номер КП';
var FOLDER_SETTINGS = { logo: 'Папка: логотип (ID)', photo: 'Папка: фото (ID)', pdf: 'Папка: КП (ID)', doc: 'Папка: документы (ID)' };
// Колонки листа «КП» (с 1).
var Q = {
  number: 1, date: 2, client: 3, contact: 4, email: 5, subtotal: 6, vat: 7, total: 8, delivery: 9,
  lines: 10, author: 11, id: 12, data: 13, status: 14, statusDate: 15, cost: 16, profit: 17,
  validUntil: 18, phone: 19, inn: 20, pdf: 21, mail: 22, ms: 23,
};
var BRAND_SETTING = 'Название для КП';
var EMAIL_SETTING = 'E-mail';
var MANAGERS_SHEET = 'Менеджеры'; // Ключ | Имя | Роль (менеджер / директор) | Активен (да / нет)
var MAIL_LIMIT_SETTING = 'Писем в день с одного ключа';
var DIRECTOR_ONLY = ['deletePayment', 'finance', 'msUpdateProduct', 'setSetting'];
// Настройки, которые директор меняет из приложения.
var AI_SETTING = 'ИИ для менеджеров';
var APP_SETTINGS = [AI_SETTING];
// Строки «Настроек», которые менеджеру не нужны и не должны попадать на его телефон.
var PRIVATE_SETTINGS = ['Ключ доступа', 'PIN директора', 'Постоянные расходы в месяц, ₽', 'План выпуска, шт/мес',
  'Комиссия, %', 'Целевая рентабельность, %', 'Папка: логотип (ID)', 'Папка: фото (ID)', 'Папка: КП (ID)',
  'Папка: документы (ID)', 'Начальный номер КП', 'Начальный номер счёта',
  'Остаток денег, ₽ (если нет МойСклад)', 'Оклад менеджера, ₽', 'Процент менеджера от оплат, %'];

/**
 * Кто обращается: ключ из «Настроек» — владелец (директор); ключи из листа «Менеджеры» — свои у каждого,
 * с ролью и отметкой «Активен». Ни одного ключа не задано — доступ открыт (как в первой версии).
 */
function auth_(ss, key) {
  var main = String(settings_(ss)[KEY_SETTING] || '').trim();
  if (main && key === main) return { role: 'director', name: '', key: key };
  var rows = cachedRows_(ss, MANAGERS_SHEET, true).slice(1);
  for (var i = 0; i < rows.length; i++) {
    if (String(rows[i][0]).trim() !== '' && String(rows[i][0]).trim() === key) {
      if (/^(нет|no|false|0)$/i.test(String(rows[i][3]).trim())) throw new Error('Доступ отключён — обратитесь к директору');
      return { role: /директор/i.test(String(rows[i][2])) ? 'director' : 'manager', name: String(rows[i][1] || ''), key: key };
    }
  }
  if (!main && !rows.some(function (r) { return String(r[0]).trim() !== ''; })) return { role: 'director', name: '', key: key };
  throw new Error('Неверный ключ доступа');
}

/** «Настройки» без закрытых строк (для менеджера). */
function publicSettings_(ss) {
  return rows_(ss, SHEETS.settings).filter(function (r, i) {
    return i === 0 || PRIVATE_SETTINGS.indexOf(String(r[0]).trim()) < 0;
  });
}
var MS_WRITE_ACTIONS = ['saveQuote', 'setStatus', 'addInvoice', 'addPayment', 'deletePayment'];
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
  gzipOut_ = !!req.gz;
  lastOk_ = false;
  var out = route_(req);
  // Журнал действий: кто, когда и что изменил (только успешные изменения).
  if (lastOk_ && JOURNAL[req.action] && who_) {
    try { journal_(SpreadsheetApp.getActiveSpreadsheet(), who_, req); } catch (e) {}
  }
  return out;
}

var who_ = null;
var lastOk_ = false;

function route_(req) {
  who_ = null;
  try {
    msCache_ = {};
    settingsMemo_ = null;
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var who = auth_(ss, String(req.key || '').trim());
    who_ = who;
    var director = who.role === 'director';
    // Изменения заказов и оплат — оплаченные суммы МойСклад читаются заново.
    if (MS_WRITE_ACTIONS.indexOf(req.action) >= 0) {
      try { CacheService.getScriptCache().remove('ms:orders'); } catch (e) {}
    }
    if (req.action === 'msBarcode') {
      try { CacheService.getScriptCache().removeAll(['ms:d', 'ms:m']); } catch (e) {}
    }
    // Удалять оплаты и видеть себестоимость может только директор.
    if (!director && DIRECTOR_ONLY.indexOf(req.action) >= 0) {
      return json_({ ok: false, error: 'Нужны права директора' });
    }
    switch (req.action) {
      case 'ping':
        return json_({ ok: true, name: ss.getName(), url: ss.getUrl() });
      case 'catalog':
        return json_({
          ok: true,
          name: ss.getName(),
          url: ss.getUrl(),
          role: who.role,
          manager: who.name,
          sheets: {
            products: rows_(ss, SHEETS.products),
            parameters: rows_(ss, SHEETS.parameters),
            volume: rows_(ss, SHEETS.volume),
            settings: director ? rows_(ss, SHEETS.settings) : publicSettings_(ss),
            // Себестоимость и цены пряжи — только директору.
            costs: director ? optionalRows_(ss, SHEETS.costs) : [],
            yarns: director ? optionalRows_(ss, SHEETS.yarns) : [],
            clients: optionalRows_(ss, SHEETS.clients),
            contract: optionalRows_(ss, SHEETS.contract),
          },
          logo: logo_(ss, String(req.logoVersion || '')),
        });
      case 'quotes':
        return json_({ ok: true, quotes: listQuotes_(ss, Number(req.limit) || 50, String(req.month || ''), !!req.light, director) });
      case 'saveQuote': {
        var saved = saveQuote_(ss, req.quote || {});
        return json_({ ok: true, number: saved, ms: msTry_(function () { return msSaveOrder_(ss, req.quote || {}, saved); }) });
      }
      case 'msCatalog': {
        // Каталог МойСклад (тысячи модификаций) собирается долго — 15 минут из кэша; «Обновить» на телефоне — заново.
        var msKey = 'ms:' + (director ? 'd' : 'm');
        if (req.fresh) {
          try { CacheService.getScriptCache().remove(msKey); } catch (e) {}
        }
        return json_({ ok: true, ms: msEnabled_() ? cached_(msKey, 900, function () { return msCatalog_(ss, director); }) : msCatalog_(ss, director) });
      }
      case 'salary':
        return json_({ ok: true, salary: salary_(ss, who, String(req.month || '')) });
      case 'finance': {
        // Отчёты МойСклад считаются долго — 5 минут из кэша (сброс при правке таблицы); свайп на телефоне — заново.
        var finKey = 'fin:' + cacheGen_();
        if (req.fresh) {
          try { CacheService.getScriptCache().remove(finKey); } catch (e) {}
        }
        return json_({ ok: true, finance: cached_(finKey, 300, function () { return finance_(ss); }) });
      }
      case 'setSetting':
        return json_({ ok: true, value: setSetting_(ss, String(req.name || ''), String(req.value == null ? '' : req.value)) });
      case 'shiftSave':
        return json_({ ok: true, shift: shiftSave_(ss, who, req.shift || {}) });
      case 'shifts':
        return json_({ ok: true, shifts: shifts_(ss, who, String(req.month || '')) });
      case 'innLookup':
        return json_({ ok: true, party: dadataParty_(String(req.inn || '')) });
      case 'msUpdateProduct': {
        var updated = msUpdateProduct_(String(req.msType || ''), String(req.msId || ''), req.changes || {});
        ['ms:d', 'ms:m'].forEach(function (k) { try { CacheService.getScriptCache().remove(k); } catch (e) {} });
        return json_({ ok: true, product: updated });
      }
      case 'msShipList':
        return json_({ ok: true, orders: msShipList_() });
      case 'msShipOrder':
        return json_({ ok: true, positions: msShipOrder_(String(req.orderId || '')) });
      case 'msShip':
        return json_({ ok: true, demand: msShip_(ss, String(req.orderId || ''), req.items || []) });
      case 'msReceiveList':
        return json_({ ok: true, docs: msReceiveList_() });
      case 'msReceiveDoc':
        return json_({ ok: true, positions: msReceiveDoc_(String(req.docType || ''), String(req.docId || '')) });
      case 'msReceive':
        return json_({ ok: true, supply: msReceive_(ss, String(req.docType || ''), String(req.docId || ''), req.items || []) });
      case 'msInventory':
        return json_({ ok: true, inventory: msInventory_(ss, req.items || []) });
      case 'msImage':
        return json_({ ok: true, image: msImage_(String(req.msType || ''), String(req.msId || '')) });
      case 'msBarcode':
        return json_({ ok: true, barcode: msCreateBarcode_(String(req.msType || ''), String(req.msId || '')) });
      case 'uploadFile':
        return json_({ ok: true, file: uploadFile_(ss, req) });
      case 'getFile':
        return json_({ ok: true, data: getFile_(ss, String(req.fileId || '')) });
      case 'sendEmail':
        return json_({ ok: true, mail: sendEmail_(ss, req, who) });
      case 'ops': {
        var o = ops_(ss);
        o.ms = msTry_(function () { return { orders: cached_('ms:orders', 60, msOrders_) }; });
        return json_({ ok: true, ops: o });
      }
      case 'addInvoice': {
        // С МойСклад номер счёта выдаёт МойСклад («Счёт покупателю»), иначе — лист «Счета».
        var inv = req.invoice || {};
        var msInv = msTry_(function () { return msInvoice_(ss, inv); });
        var forced = msInv && msInv.number ? msInv.number : 0;
        return json_({ ok: true, number: addInvoice_(ss, inv, forced), ms: msInv });
      }
      case 'addPayment': {
        var pay = req.payment || {};
        var msPay = msTry_(function () { return msPayment_(ss, pay); });
        addPayment_(ss, pay, msPay && msPay.id);
        return json_({ ok: true, ms: msPay });
      }
      case 'deletePayment': {
        var msId = paymentMsId_(ss, String(req.id || ''));
        var msDel = msId ? msTry_(function () { ms_('delete', '/entity/paymentin/' + msId); return { deleted: true }; }) : null;
        deleteById_(ss, 'payments', 8, String(req.id || ''));
        return json_({ ok: true, ms: msDel });
      }
      case 'saveOrder':
        saveOrder_(ss, req.order || {});
        return json_({ ok: true });
      case 'addYarnMoves':
        addYarnMoves_(ss, req.moves || []);
        return json_({ ok: true });
      case 'setStatus':
        setStatus_(ss, String(req.id || ''), String(req.status || ''));
        return json_({ ok: true, ms: msTry_(function () { return msSetState_(String(req.id || ''), String(req.status || '')); }) });
      default:
        return json_({ ok: false, error: 'Неизвестное действие: ' + req.action });
    }
  } catch (err) {
    return json_({ ok: false, error: String((err && err.message) || err) });
  }
}

/** Значения листа так, как они видны в таблице (строки). */
function rows_(ss, name) {
  if (!ss.getSheetByName(name)) throw new Error('Нет листа «' + name + '»');
  return cachedRows_(ss, name, true);
}

/** Необязательный лист: если его нет — пустой список. */
function optionalRows_(ss, name) {
  return cachedRows_(ss, name, true);
}

var settingsMemo_ = null;

function settings_(ss) {
  if (settingsMemo_) return settingsMemo_;
  var result = {};
  cachedRows_(ss, SHEETS.settings, true).slice(1).forEach(function (r) {
    if (r[0]) result[String(r[0]).trim()] = r[1];
  });
  settingsMemo_ = result;
  return result;
}

// ---------------------------------------------------------------- Скорость: кэш и сжатие

/**
 * Справочные листы (Настройки, Менеджеры, Изделия, Параметры…) читаются из кэша скрипта (до 10 минут) —
 * чтение листа занимает до секунды, а нужны они почти в каждом запросе. Правка таблицы вручную (onEdit)
 * и запись скриптом сбрасывают кэш: новое «поколение» ключей.
 */
var CACHE_TTL = 600;

function cacheGen_() {
  var c = CacheService.getScriptCache();
  var gen = c.get('gen');
  if (!gen) {
    gen = newGen_();
    c.put('gen', gen, 21600);
  }
  return gen;
}

/** Новое «поколение» кэша: время + случайная часть (два сброса в одну миллисекунду всё равно различаются). */
function newGen_() {
  return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

/** Сбросить кэш таблицы (после записи в листы-справочники или правки вручную). */
function cacheBump_() {
  try {
    CacheService.getScriptCache().put('gen', newGen_(), 21600);
  } catch (e) {}
}

// ---------------------------------------------------------------- Финансы: платёжный календарь

var REGULAR_SHEET = 'Регулярные платежи';
var PLAN_SETTING = 'План продаж в месяц, ₽';
var BALANCE_SETTING = 'Остаток денег, ₽ (если нет МойСклад)';

function money_(v) {
  var n = Number(String(v == null ? '' : v).replace(/\s/g, '').replace(',', '.'));
  return isFinite(n) ? n : 0;
}

/** Регулярные платежи из листа: активные строки с суммой и днём месяца. */
function regularPayments_(ss) {
  var sheet = ss.getSheetByName(REGULAR_SHEET);
  if (!sheet) return [];
  return sheet.getDataRange().getValues().slice(1).filter(function (r) {
    return String(r[0]).trim() && money_(r[1]) > 0 && !/^(нет|no|false|0)$/i.test(String(r[4]).trim());
  }).map(function (r) {
    return { name: String(r[0]).trim(), amount: money_(r[1]), day: Math.min(31, Math.max(1, Math.round(money_(r[2])) || 1)), category: String(r[3] || '').trim() };
  });
}

function msMoment_(date) {
  return Utilities.formatDate(date, 'Europe/Moscow', 'yyyy-MM-dd HH:mm:ss');
}

function msTime_(moment) {
  return moment ? new Date(String(moment).replace(' ', 'T').substring(0, 19) + '+03:00').getTime() : 0;
}

/**
 * Данные платёжного календаря: остаток денег (МойСклад или «Настройки»), регулярные платежи,
 * неоплаченные счета поставщиков (МойСклад) и фактические поступления/выплаты за 6 месяцев.
 */
function finance_(ss) {
  var s = settings_(ss);
  var out = {
    plan: money_(s[PLAN_SETTING]) || 0,
    regular: regularPayments_(ss),
    balance: String(s[BALANCE_SETTING] || '').trim() ? money_(s[BALANCE_SETTING]) : null,
    balanceSource: String(s[BALANCE_SETTING] || '').trim() ? 'sheet' : '',
    supplier: [],
    actualIn: [],
    actualOut: [],
  };
  var ms = msTry_(function () {
    var report = ms_('get', '/report/money/byaccount');
    var rows = report.rows || [];
    if (rows.length) {
      out.balance = rows.reduce(function (a, r) { return a + (Number(r.balance) || 0); }, 0) / 100;
      out.balanceSource = 'ms';
    }
    var since = msMoment_(new Date(Date.now() - 120 * 86400000));
    out.supplier = msAll_('/entity/invoicein?filter=' + encodeURIComponent('paymentPlannedMoment>=' + since)).map(function (d) {
      return { name: 'Счёт поставщика № ' + d.name, amount: ((Number(d.sum) || 0) - (Number(d.payedSum) || 0)) / 100, due: msTime_(d.paymentPlannedMoment || d.moment) };
    }).filter(function (x) { return x.amount > 0.009; });
    // Факт за 6 месяцев: графики «Приход и расход» на главном экране директора.
    // Два года: периоды «по месяцам» сравниваются с прошлым годом.
    var weeks8 = msMoment_(new Date(Date.now() - 731 * 86400000));
    out.actualIn = msAll_('/entity/paymentin?filter=' + encodeURIComponent('moment>=' + weeks8)).map(function (d) {
      return { date: msTime_(d.moment), amount: (Number(d.sum) || 0) / 100 };
    });
    // Расход — исходящие платежи и расходные ордера со статьёй расходов («Аренда», «Пряжа»…).
    var items = {};
    msAll_('/entity/expenseitem').forEach(function (e) { items[e.id] = e.name; });
    var expense = function (d) {
      return {
        date: msTime_(d.moment), amount: (Number(d.sum) || 0) / 100,
        category: (d.expenseItem && items[msIdOf_(d.expenseItem)]) || 'Без статьи',
      };
    };
    out.actualOut = msAll_('/entity/paymentout?filter=' + encodeURIComponent('moment>=' + weeks8)).map(expense)
      .concat(msAll_('/entity/cashout?filter=' + encodeURIComponent('moment>=' + weeks8)).map(expense));
    // Отгрузки по месяцам (₽ и шт) — один запрос к отчёту «Показатели продаж».
    // Отгрузки (₽ и шт) из отчёта «Показатели продаж»: по месяцам за 5 лет, по дням за 62 дня, по часам за 2 дня.
    out.shipments = [];
    out.shipDay = [];
    out.shipHour = [];
    try {
      var plot = function (interval, from) {
        var series = ms_('get', '/report/sales/plotseries?interval=' + interval + '&momentFrom=' + encodeURIComponent(msMoment_(from)) +
          '&momentTo=' + encodeURIComponent(msMoment_(new Date()))).series || [];
        return series.map(function (r) {
          return { date: msTime_(r.date), sum: (Number(r.sum) || 0) / 100, qty: Number(r.quantity) || 0 };
        });
      };
      var from = new Date(); from.setDate(1); from.setMonth(0); from.setFullYear(from.getFullYear() - 4);
      out.shipments = plot('month', from);
      out.shipDay = plot('day', new Date(Date.now() - 62 * 86400000));
      out.shipHour = plot('hour', new Date(Date.now() - 2 * 86400000));
    } catch (err) {
      out.shipError = String((err && err.message) || err);
    }
    return {};
  });
  if (ms && ms.error) out.msError = ms.error;
  return out;
}

// ---------------------------------------------------------------- Зарплата менеджеров

var SALARY_BASE_SETTING = 'Оклад менеджера, ₽';
var SALARY_PERCENT_SETTING = 'Процент менеджера от оплат, %';

/**
 * Зарплата за месяц (yyyy-MM): оклад + % от оплат, поступивших в этом месяце по КП менеджера.
 * Оплаты — из МойСклад (входящие платежи, привязанные к заказу «КП-N»), иначе — лист «Оплаты».
 * Директор видит всех, менеджер — только себя.
 */
function salary_(ss, who, month) {
  var s = settings_(ss);
  var tz = ss.getSpreadsheetTimeZone();
  if (!/^\d{4}-\d{2}$/.test(month)) month = Utilities.formatDate(new Date(), tz, 'yyyy-MM');
  var base = String(s[SALARY_BASE_SETTING] || '').trim() ? money_(s[SALARY_BASE_SETTING]) : 60000;
  var percent = String(s[SALARY_PERCENT_SETTING] || '').trim() ? money_(s[SALARY_PERCENT_SETTING]) : 3;
  var monthOf = function (d) { return Utilities.formatDate(d instanceof Date ? d : new Date(d), tz, 'yyyy-MM'); };
  var person = function (author) { return String(author || '').split(' / ')[0].trim(); };

  var authorById = {};
  ss.getSheetByName(SHEETS.quotes).getDataRange().getValues().slice(1).forEach(function (r) {
    if (r[Q.id - 1]) authorById[String(r[Q.id - 1])] = person(r[Q.author - 1]);
  });
  var paid = {};
  var add = function (quoteId, amount) {
    var name = authorById[String(quoteId)];
    if (!name) return;
    paid[name] = (paid[name] || 0) + amount;
  };
  var fromMs = false;
  if (msEnabled_()) {
    var ms = msTry_(function () {
      var orderQuote = {};
      msAll_('/entity/customerorder?filter=name~' + encodeURIComponent(MS_ORDER_PREFIX)).forEach(function (o) {
        if (o.externalCode) orderQuote[o.id] = o.externalCode;
      });
      var start = month + '-01 00:00:00';
      msAll_('/entity/paymentin?filter=' + encodeURIComponent('moment>=' + start)).forEach(function (p) {
        if (monthOf(msTime_(p.moment)) !== month) return;
        (p.operations || []).forEach(function (op) {
          var q = orderQuote[msIdOf_(op)];
          if (q) add(q, (Number(op.linkedSum != null ? op.linkedSum : p.sum) || 0) / 100);
        });
      });
      return {};
    });
    fromMs = !(ms && ms.error);
  }
  if (!fromMs) {
    var pays = ss.getSheetByName(SHEETS.payments);
    (pays ? pays.getDataRange().getValues().slice(1) : []).forEach(function (r) {
      if (r[0] && monthOf(r[0]) === month) add(r[2], money_(r[4]));
    });
  }
  // Все активные менеджеры — даже без оплат (оклад есть всегда).
  cachedRows_(ss, MANAGERS_SHEET, true).slice(1).forEach(function (r) {
    var name = String(r[1] || '').trim();
    if (name && !/^(нет|no|false|0)$/i.test(String(r[3]).trim()) && !/директор/i.test(String(r[2])) && !(name in paid)) paid[name] = 0;
  });
  var rows = Object.keys(paid).sort().map(function (name) {
    var bonus = Math.round(paid[name] * percent) / 100;
    return { name: name, paid: Math.round(paid[name] * 100) / 100, bonus: bonus, total: Math.round((base + bonus) * 100) / 100 };
  });
  if (who.role !== 'director') rows = rows.filter(function (r) { return r.name === person(who.name); });
  return { month: month, base: base, percent: percent, source: fromMs ? 'ms' : 'sheet', rows: rows };
}

// ---------------------------------------------------------------- Журнал действий

var JOURNAL_SHEET = 'Журнал';
var JOURNAL = {
  saveQuote: 'КП сохранено',
  setStatus: 'Статус КП',
  addInvoice: 'Счёт',
  addPayment: 'Оплата',
  deletePayment: 'Оплата удалена',
  saveOrder: 'Заказ на производство',
  addYarnMoves: 'Склад пряжи',
  sendEmail: 'Письмо клиенту',
  msBarcode: 'Штрихкод МойСклад',
};

function journalDetails_(ss, req) {
  var rub = function (v) { return (Number(v) || 0).toFixed(2).replace('.', ',') + ' ₽'; };
  var number = function (id) { var r = quoteRow_(ss, String(id || '')); return r ? '№ ' + r[0] : ''; };
  switch (req.action) {
    case 'saveQuote': { var q = req.quote || {}; return [number(q.id) || 'новое', q.client, rub(q.total)].filter(String).join(' · '); }
    case 'setStatus': return [number(req.id), '→ ' + req.status].filter(String).join(' ');
    case 'addInvoice': { var i = req.invoice || {}; return 'КП № ' + i.quoteNumber + ' · ' + rub(i.amount) + ' · ' + (i.purpose || ''); }
    case 'addPayment': { var p = req.payment || {}; return 'КП № ' + p.quoteNumber + ' · ' + (p.client || '') + ' · ' + rub(p.amount); }
    case 'deletePayment': return 'ID ' + req.id;
    case 'saveOrder': { var o = req.order || {}; return 'КП № ' + o.quoteNumber + ' · ' + (o.stage || ''); }
    case 'addYarnMoves': return (req.moves || []).map(function (m) { return m.yarn + ' ' + m.kg + ' кг'; }).join(', ');
    case 'sendEmail': return (req.kind || 'КП') + ' → ' + (req.to || '');
    case 'msBarcode': return req.msType + ' ' + req.msId;
  }
  return '';
}

function journal_(ss, who, req) {
  var sheet = ss.getSheetByName(JOURNAL_SHEET);
  if (!sheet) {
    sheet = ss.insertSheet(JOURNAL_SHEET);
    sheet.appendRow(['Дата', 'Кто', 'Действие', 'Подробности']);
  }
  var person = who.name || (who.role === 'director' ? 'Директор' : 'Менеджер');
  sheet.appendRow([new Date(), person, JOURNAL[req.action], journalDetails_(ss, req)]);
}

// ---------------------------------------------------------------- Резервная копия

var BACKUP_FOLDER = 'Фабрика KS — резервные копии';
var BACKUP_KEEP = 14;

function backupFolder_() {
  var props = PropertiesService.getScriptProperties();
  var id = props.getProperty('BACKUP_FOLDER');
  if (id) {
    try { return DriveApp.getFolderById(id); } catch (e) {}
  }
  var found = DriveApp.getFoldersByName(BACKUP_FOLDER);
  var folder = found.hasNext() ? found.next() : DriveApp.createFolder(BACKUP_FOLDER);
  props.setProperty('BACKUP_FOLDER', folder.getId());
  return folder;
}

/** Копия таблицы в папку «Фабрика KS — резервные копии»; хранятся последние 14 копий. */
function backupNow_() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var folder = backupFolder_();
  var name = ss.getName() + ' — копия ' + Utilities.formatDate(new Date(), ss.getSpreadsheetTimeZone(), 'yyyy-MM-dd HH:mm');
  DriveApp.getFileById(ss.getId()).makeCopy(name, folder);
  var files = [];
  var it = folder.getFiles();
  while (it.hasNext()) files.push(it.next());
  files.sort(function (a, b) { return b.getDateCreated().getTime() - a.getDateCreated().getTime(); });
  files.slice(BACKUP_KEEP).forEach(function (f) { f.setTrashed(true); });
  return name;
}

/** Триггер: каждый день в 3:00. */
function backupDaily() {
  backupNow_();
}

/** Меню: копия прямо сейчас. */
function backupMenu() {
  var name = backupNow_();
  SpreadsheetApp.getUi().alert('Резервная копия создана: «' + name + '»\nПапка на Диске: «' + BACKUP_FOLDER + '» (хранятся последние ' + BACKUP_KEEP + ').');
}

/** Меню: включить ежедневную копию в 3:00 (повторное включение не создаёт второй триггер). */
function backupEnable() {
  ScriptApp.getProjectTriggers().forEach(function (t) {
    if (t.getHandlerFunction() === 'backupDaily') ScriptApp.deleteTrigger(t);
  });
  ScriptApp.newTrigger('backupDaily').timeBased().everyDays(1).atHour(3).create();
  backupNow_();
  SpreadsheetApp.getUi().alert('Ежедневная резервная копия включена (около 3:00). Первая копия уже создана в папке «' + BACKUP_FOLDER + '».');
}

/** Простой триггер: любая правка таблицы вручную — справочники перечитываются. */
function onEdit(e) {
  cacheBump_();
}

/** Значение из кэша (сжатое, частями по 90 КБ); `null` — нет. */
function cacheGet_(key) {
  var c = CacheService.getScriptCache();
  var head = c.get(key);
  if (!head) return null;
  var n = Number(head);
  var names = [];
  for (var i = 0; i < n; i++) names.push(key + '#' + i);
  var parts = c.getAll(names);
  var text = '';
  for (var j = 0; j < n; j++) {
    if (parts[names[j]] == null) return null;
    text += parts[names[j]];
  }
  var blob = Utilities.ungzip(Utilities.newBlob(Utilities.base64Decode(text), 'application/x-gzip'));
  return JSON.parse(blob.getDataAsString());
}

function cachePut_(key, value, ttl) {
  var packed = Utilities.base64Encode(Utilities.gzip(Utilities.newBlob(JSON.stringify(value), 'application/json')).getBytes());
  var size = 90000;
  var n = Math.ceil(packed.length / size) || 1;
  if (n > 50) return; // больше ~4,5 МБ — не кэшируем
  var all = {};
  for (var i = 0; i < n; i++) all[key + '#' + i] = packed.substring(i * size, (i + 1) * size);
  all[key] = String(n);
  CacheService.getScriptCache().putAll(all, ttl);
}

/** Результат [fn] из кэша или вычисленный и сохранённый; ошибки кэша не мешают работе. */
function cached_(key, ttl, fn) {
  var value = null;
  try {
    value = cacheGet_(key);
  } catch (e) {
    value = null;
  }
  if (value !== null) return value;
  value = fn();
  try {
    cachePut_(key, value, ttl);
  } catch (e) {}
  return value;
}

/** Строки листа (отображаемые значения) через кэш; нет листа — пустой список. */
function cachedRows_(ss, name, display) {
  var key;
  try {
    key = 'rows:' + cacheGen_() + ':' + name;
  } catch (e) {
    key = null;
  }
  var read = function () {
    var sheet = ss.getSheetByName(name);
    if (!sheet) return [];
    var range = sheet.getDataRange();
    return display ? range.getDisplayValues() : range.getValues();
  };
  return key ? cached_(key, CACHE_TTL, read) : read();
}

/** Ответ сжимается (gzip + base64), если телефон это поддерживает и ответ большой — в 5–10 раз меньше трафика. */
var gzipOut_ = false;

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
    var old = null;
    if (rowIndex > 0) {
      old = data[rowIndex - 1];
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
    // Пересохранение: автор остаётся первым (зарплата менеджера), себестоимость не стирается,
    // если её прислал телефон без доступа к затратам.
    if (old) {
      if (String(old[Q.author - 1] || '').trim()) row[Q.author - 1] = old[Q.author - 1];
      if (String(q.cost == null ? '' : q.cost).trim() === '') row[Q.cost - 1] = old[Q.cost - 1];
      if (String(q.profit == null ? '' : q.profit).trim() === '') row[Q.profit - 1] = old[Q.profit - 1];
    }
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
  cacheBump_();
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

function listQuotes_(ss, limit, month, light, director) {
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
  // month — все КП месяца (для отчёта); light — все КП без данных черновика (для долгов).
  var max = month || light ? Infinity : limit;
  var result = [];
  for (var i = data.length - 1; i >= 1 && result.length < max; i--) {
    var r = data[i];
    if (!r[0]) continue;
    var date = r[Q.date - 1];
    var valid = r[Q.validUntil - 1];
    var m = date instanceof Date ? Utilities.formatDate(date, tz, 'yyyy-MM') : '';
    if (month && m !== month) continue;
    result.push({
      number: Number(r[0]),
      date: date instanceof Date ? Utilities.formatDate(date, tz, 'dd.MM.yyyy HH:mm') : String(date),
      month: date instanceof Date ? Utilities.formatDate(date, tz, 'yyyy-MM') : '',
      client: String(r[Q.client - 1] || ''),
      total: Number(r[Q.total - 1]) || 0,
      author: String(r[Q.author - 1] || ''),
      id: String(r[Q.id - 1] || ''),
      data: light ? '' : String(r[Q.data - 1] || ''),
      status: String(r[Q.status - 1] || STATUSES[0]),
      // Прибыль — только директору.
      profit: !director || r[Q.profit - 1] === '' ? null : Number(r[Q.profit - 1]),
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
  // Поиск файла в папке Диска — медленный: результат держим в кэше 30 минут.
  var found = cached_('logo:' + cacheGen_() + ':' + id, 1800, function () {
    var files = DriveApp.getFolderById(id).getFiles();
    var best = null;
    while (files.hasNext()) {
      var f = files.next();
      if (f.getMimeType().indexOf('image/') !== 0) continue;
      if (!best || f.getLastUpdated() > best.getLastUpdated()) best = f;
    }
    return best ? { id: best.getId(), version: best.getId() + ':' + best.getLastUpdated().getTime() } : { id: '', version: '' };
  });
  if (!found.id) return { version: '' };
  var version = found.version;
  if (version === knownVersion) return { version: version };
  var newest = DriveApp.getFileById(found.id);
  return {
    version: version,
    mime: newest.getMimeType(),
    data: Utilities.base64Encode(newest.getBlob().getBytes()),
  };
}

/** Фото образца или PDF КП → папка на Диске. Файл с тем же именем заменяется. */
function uploadFile_(ss, req) {
  var kind = req.kind === 'pdf' || req.kind === 'doc' ? req.kind : 'photo';
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
  if (kind === 'doc' && req.invoiceNumber) {
    var inv = sheet_(ss, 'invoices');
    var rows = inv.getDataRange().getValues();
    for (var r = 1; r < rows.length; r++) {
      if (Number(rows[r][0]) === Number(req.invoiceNumber)) {
        inv.getRange(r + 1, 8).setValue(file.getUrl());
        break;
      }
    }
  }
  return { id: file.getId(), url: file.getUrl() };
}

// ---------------------------------------------------------------- Учёт: счета, оплаты, заказы, склад пряжи

/** Лист учёта; если его нет — создаётся с заголовками. */
function sheet_(ss, kind) {
  var sheet = ss.getSheetByName(SHEETS[kind]);
  if (!sheet) {
    sheet = ss.insertSheet(SHEETS[kind]);
    sheet.appendRow(HEADERS[kind]);
    sheet.setFrozenRows(1);
  }
  return sheet;
}

/** Строка «Настроек» из приложения (только разрешённые [APP_SETTINGS]). */
function setSetting_(ss, name, value) {
  if (APP_SETTINGS.indexOf(name) < 0) throw new Error('Эту настройку меняют в таблице');
  value = value.substring(0, 500);
  var sheet = ss.getSheetByName(SHEETS.settings);
  var rows = sheet.getDataRange().getValues();
  var done = false;
  for (var i = 1; i < rows.length && !done; i++) {
    if (String(rows[i][0]).trim() === name) {
      sheet.getRange(i + 1, 2).setValue(value);
      done = true;
    }
  }
  if (!done) sheet.appendRow([name, value]);
  cacheBump_();
  return value;
}

// ---------------------------------------------------------------- Рабочее время

function person_(who) {
  return String((who && who.name) || '').split(' / ')[0].trim() || 'Директор';
}

/**
 * Смена: начало при входе в приложение, конец — «Закрыть смену». Сотрудник пишет только свои смены;
 * директор может исправить любую (например, незакрытую) — это видно в колонке «Исправил».
 */
function shiftSave_(ss, who, s) {
  var id = String(s.id || '').trim();
  if (!/^[\w-]{6,64}$/.test(id)) throw new Error('Неверная смена');
  var start = Number(s.start) || 0;
  var end = s.end ? Number(s.end) : null;
  if (!start || (end && end < start)) throw new Error('Неверное время смены');
  var director = who.role === 'director';
  var sheet = sheet_(ss, 'shifts');
  var rows = sheet.getDataRange().getValues();
  var me = person_(who);
  for (var i = 1; i < rows.length; i++) {
    if (String(rows[i][0]) !== id) continue;
    var owner = String(rows[i][1]);
    if (owner !== me && !director) throw new Error('Это смена другого сотрудника');
    var fixedBy = owner !== me ? me : String(rows[i][5] || '');
    sheet.getRange(i + 1, 3, 1, 4).setValues([[new Date(start), end ? new Date(end) : '', end ? Math.round((end - start) / 36000) / 100 : '', fixedBy]]);
    // Время сервера при закрытии — для сверки с часами телефона.
    if (end && !fixedBy) sheet.getRange(i + 1, 8).setValue(new Date());
    return { id: id, person: owner, start: start, end: end };
  }
  var person = director && s.person ? String(s.person) : me;
  sheet.appendRow([id, person, new Date(start), end ? new Date(end) : '', end ? Math.round((end - start) / 36000) / 100 : '', '', new Date(), end ? new Date() : '']);
  return { id: id, person: person, start: start, end: end };
}

/** Смены месяца «2026-10»: директору — все, сотруднику — свои. */
function shifts_(ss, who, month) {
  var tz = ss.getSpreadsheetTimeZone();
  if (!/^\d{4}-\d{2}$/.test(month)) month = Utilities.formatDate(new Date(), tz, 'yyyy-MM');
  var sheet = ss.getSheetByName(SHEETS.shifts);
  if (!sheet) return [];
  var me = person_(who);
  return sheet.getDataRange().getValues().slice(1).filter(function (r) {
    return r[0] && r[2] instanceof Date && Utilities.formatDate(r[2], tz, 'yyyy-MM') === month &&
      (who.role === 'director' || String(r[1]) === me);
  }).map(function (r) {
    // Расхождение часов телефона с сервером больше 15 минут — директор видит предупреждение.
    var drift = function (phone, server) { return phone && server instanceof Date && Math.abs(phone - server.getTime()) > 15 * 60000; };
    return {
      id: String(r[0]), person: String(r[1]), start: r[2].getTime(), end: r[3] instanceof Date ? r[3].getTime() : null, fixedBy: String(r[5] || ''),
      suspicious: !!(drift(r[2].getTime(), r[6]) || drift(r[3] instanceof Date ? r[3].getTime() : 0, r[7])),
    };
  });
}

function millis_(v) {
  return v instanceof Date ? v.getTime() : (Number(v) || 0);
}

function date_(ms) {
  return ms ? new Date(Number(ms)) : new Date();
}

function ops_(ss) {
  function rows(kind) {
    var sheet = ss.getSheetByName(SHEETS[kind]);
    return sheet ? sheet.getDataRange().getValues().slice(1).filter(function (r) { return r.join('') !== ''; }) : [];
  }
  return {
    invoices: rows('invoices').map(function (r) {
      return { number: Number(r[0]), date: millis_(r[1]), quoteNumber: Number(r[2]) || 0, quoteId: String(r[3]),
        client: String(r[4]), amount: Number(r[5]) || 0, purpose: String(r[6]), pdf: String(r[7] || '') };
    }),
    payments: rows('payments').map(function (r) {
      return { date: millis_(r[0]), quoteNumber: Number(r[1]) || 0, quoteId: String(r[2]), client: String(r[3]),
        amount: Number(r[4]) || 0, note: String(r[5] || ''), author: String(r[6] || ''), id: String(r[7] || '') };
    }),
    orders: rows('orders').map(function (r) {
      return { quoteNumber: Number(r[0]) || 0, quoteId: String(r[1]), client: String(r[2]), created: millis_(r[3]),
        due: millis_(r[4]), stage: String(r[5] || ''), stageDate: millis_(r[6]), items: String(r[7] || ''),
        comment: String(r[8] || ''), yarn: String(r[9] || ''), yarnWrittenOff: r[10] === true || String(r[10]).toLowerCase() === 'да' };
    }),
    moves: rows('yarnMoves').map(function (r) {
      return { date: millis_(r[0]), yarn: String(r[1]), kg: Number(r[2]) || 0, reason: String(r[3] || ''),
        author: String(r[4] || ''), id: String(r[5] || '') };
    }),
  };
}

/** Новый счёт: номер — следующий по листу «Счета» (под блокировкой). */
function addInvoice_(ss, inv, forcedNumber) {
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  try {
    var sheet = sheet_(ss, 'invoices');
    var data = sheet.getDataRange().getValues();
    var max = 0;
    for (var i = 1; i < data.length; i++) max = Math.max(max, Number(data[i][0]) || 0);
    var start = Number(settings_(ss)[START_INVOICE_SETTING]) || 1;
    var number = forcedNumber || Math.max(max + 1, start);
    sheet.appendRow([number, date_(inv.date), Number(inv.quoteNumber) || '', text_(inv.quoteId), text_(inv.client),
      Number(inv.amount) || 0, text_(inv.purpose), '']);
    return number;
  } finally {
    lock.releaseLock();
  }
}

/** Оплата; повтор с тем же ID не дублируется. */
function addPayment_(ss, p, msId) {
  if (!(Number(p.amount) > 0)) throw new Error('Сумма оплаты должна быть больше нуля');
  var sheet = sheet_(ss, 'payments');
  var data = sheet.getDataRange().getValues();
  for (var i = 1; i < data.length; i++) if (p.id && String(data[i][7]) === String(p.id)) return;
  sheet.appendRow([date_(p.date), Number(p.quoteNumber) || '', text_(p.quoteId), text_(p.client), Number(p.amount),
    text_(p.note), text_(p.author), text_(p.id), text_(msId || '')]);
}

/** ID входящего платежа МойСклад для оплаты из листа «Оплаты» (столбец I). */
function paymentMsId_(ss, id) {
  var sheet = ss.getSheetByName(SHEETS.payments);
  if (!sheet || !id) return '';
  var data = sheet.getDataRange().getValues();
  for (var i = 1; i < data.length; i++) if (String(data[i][7]) === id) return String(data[i][8] || '');
  return '';
}

function deleteById_(ss, kind, column, id) {
  if (!id) throw new Error('Не указан ID');
  var sheet = sheet_(ss, kind);
  var data = sheet.getDataRange().getValues();
  for (var i = data.length - 1; i >= 1; i--) {
    if (String(data[i][column - 1]) === id) {
      sheet.deleteRow(i + 1);
      return;
    }
  }
  throw new Error('Запись не найдена');
}

/** Заказ на производство: одна строка на КП (по ID КП). */
function saveOrder_(ss, o) {
  if (!o.quoteId) throw new Error('Не указано КП заказа');
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  try {
    var sheet = sheet_(ss, 'orders');
    var data = sheet.getDataRange().getValues();
    var row = [Number(o.quoteNumber) || '', text_(o.quoteId), text_(o.client), date_(o.created), date_(o.due),
      text_(o.stage), date_(o.stageDate), text_(o.items), text_(o.comment), text_(o.yarn), o.yarnWrittenOff ? 'да' : 'нет'];
    for (var i = 1; i < data.length; i++) {
      if (String(data[i][1]) === String(o.quoteId)) {
        sheet.getRange(i + 1, 1, 1, row.length).setValues([row]);
        return;
      }
    }
    sheet.appendRow(row);
  } finally {
    lock.releaseLock();
  }
}

/** Движения пряжи (приход «+», расход «−»); повтор с тем же ID не дублируется. */
function addYarnMoves_(ss, moves) {
  var sheet = sheet_(ss, 'yarnMoves');
  var data = sheet.getDataRange().getValues();
  var known = {};
  for (var i = 1; i < data.length; i++) known[String(data[i][5])] = true;
  moves.forEach(function (m) {
    if (!String(m.yarn || '').trim() || !Number(m.kg)) return;
    if (m.id && known[String(m.id)]) return;
    sheet.appendRow([date_(m.date), text_(m.yarn), Number(m.kg), text_(m.reason), text_(m.author), text_(m.id)]);
    known[String(m.id)] = true;
  });
}

/**
 * Письмо клиенту с PDF КП — с аккаунта владельца таблицы. Ответ клиента придёт на «E-mail» из «Настроек»,
 * туда же — копия. PDF сохраняется в папку «КП (PDF)», в листе «КП» отмечается, когда и кому отправлено.
 */
function sendEmail_(ss, req, who) {
  var to = String(req.to || '').trim();
  if (!/^[^\s@,;<>]+@[^\s@,;<>]+\.[^\s@,;<>]+$/.test(to)) throw new Error('Некорректный e-mail клиента: ' + to);
  // Письмо — только на e-mail клиента из этого КП (сохранённого в листе «КП»).
  var row = quoteRow_(ss, String(req.quoteId || ''));
  if (!row) throw new Error('Сначала сохраните КП — письмо уходит только клиенту сохранённого КП');
  var allowed = String(row[Q.email - 1] || '').trim().toLowerCase();
  if (!allowed || allowed !== to.toLowerCase()) {
    throw new Error('Письмо можно отправить только на e-mail клиента из КП № ' + row[0] + (allowed ? ' (' + allowed + ')' : ' — он не указан'));
  }
  mailLimit_(ss, who);
  var s = settings_(ss);
  var own = String(s[EMAIL_SETTING] || '').trim();
  var copy = own && own.toLowerCase() !== to.toLowerCase();
  if (MailApp.getRemainingDailyQuota() < (copy ? 2 : 1)) {
    throw new Error('Лимит писем Google на сегодня исчерпан — отправьте КП из почты телефона');
  }
  var name = String(req.name || 'КП.pdf');
  // Счёт и договор — в папку «Документы», КП — в «КП (PDF)» со ссылкой в листе «КП».
  var isQuote = !req.kind || req.kind === 'quote';
  var blob;
  try {
    var upload = isQuote
      ? { kind: 'pdf', name: name, data: req.data, mime: 'application/pdf', quoteId: req.quoteId }
      : { kind: 'doc', name: name, data: req.data, mime: 'application/pdf', invoiceNumber: req.invoiceNumber };
    blob = DriveApp.getFileById(uploadFile_(ss, upload).id).getBlob();
  } catch (err) {
    // Папка не настроена — письмо всё равно уходит, PDF берём из запроса.
    blob = Utilities.newBlob(Utilities.base64Decode(String(req.data || '')), 'application/pdf', name);
  }
  var options = { name: String(s[BRAND_SETTING] || 'Фабрика "KS"'), attachments: [blob] };
  if (own) options.replyTo = own;
  if (copy) options.cc = own;
  MailApp.sendEmail(to, String(req.subject || 'Коммерческое предложение'), String(req.body || ''), options);

  var sent = Utilities.formatDate(new Date(), ss.getSpreadsheetTimeZone(), 'dd.MM.yyyy HH:mm') + ' → ' + to;
  if (isQuote && req.quoteId) {
    var sheet = ss.getSheetByName(SHEETS.quotes);
    // В старых таблицах колонки «Письмо клиенту» ещё нет.
    if (sheet.getMaxColumns() < Q.mail) sheet.insertColumnsAfter(sheet.getMaxColumns(), Q.mail - sheet.getMaxColumns());
    var data = sheet.getDataRange().getValues();
    for (var i = 1; i < data.length; i++) {
      if (String(data[i][Q.id - 1]) === String(req.quoteId)) {
        sheet.getRange(i + 1, Q.mail).setValue(text_(sent));
        break;
      }
    }
  }
  return { to: to, sent: sent };
}

function quoteRow_(ss, quoteId) {
  if (!quoteId) return null;
  var data = ss.getSheetByName(SHEETS.quotes).getDataRange().getValues();
  for (var i = 1; i < data.length; i++) if (String(data[i][Q.id - 1]) === quoteId) return data[i];
  return null;
}

/** Не больше N писем в день с одного ключа (защита почты фабрики при утечке ключа). */
function mailLimit_(ss, who) {
  var limit = Number(settings_(ss)[MAIL_LIMIT_SETTING]) || 30;
  var tz = ss.getSpreadsheetTimeZone();
  var bytes = Utilities.computeDigest(Utilities.DigestAlgorithm.SHA_256, String(who.key || ''));
  var id = Utilities.base64Encode(bytes).substring(0, 12);
  var prop = 'mail:' + Utilities.formatDate(new Date(), tz, 'yyyyMMdd') + ':' + id;
  var props = PropertiesService.getScriptProperties();
  var sent = Number(props.getProperty(prop)) || 0;
  if (sent >= limit) throw new Error('Лимит писем на сегодня (' + limit + ') исчерпан — отправьте из почты телефона');
  props.setProperty(prop, String(sent + 1));
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
  lastOk_ = !!(obj && obj.ok);
  var text = JSON.stringify(obj);
  if (gzipOut_ && text.length > 20000) {
    var packed = Utilities.base64Encode(Utilities.gzip(Utilities.newBlob(text, 'application/json')).getBytes());
    text = JSON.stringify({ ok: obj.ok, gz: packed });
  }
  return ContentService.createTextOutput(text).setMimeType(ContentService.MimeType.JSON);
}


// ---------------------------------------------------------------- МойСклад

var MS_API = 'https://api.moysklad.ru/api/remap/1.2';
var MS_TOKEN_PROPERTY = 'MS_TOKEN';
var MS_STORE_SETTING = 'МойСклад: склад';
var MS_SERVICE_SETTING = 'МойСклад: услуга под заказ';
var MS_ORDER_PREFIX = 'КП-';
// Типы цен МойСклад → тираж (порог в штуках). «Премиум / Средняя / Эконом» не используются.
var MS_TIERS = [['1 штук', 1], ['10 штук', 10], ['20 штук', 20], ['50 штук', 50], ['от 100 штук', 100], ['от 500 штук', 500]];
var MS_STATE_COLOR = 3200456; // #30D5C8

function onOpen() {
  SpreadsheetApp.getUi().createMenu('Фабрика KS')
    .addItem('QR для подключения телефона…', 'qrConnect')
    .addSeparator()
    .addItem('Резервная копия сейчас', 'backupMenu')
    .addItem('Ежедневная резервная копия (включить)', 'backupEnable')
    .addSeparator()
    .addItem('Подключить МойСклад…', 'msConnect')
    .addItem('Отключить МойСклад', 'msDisconnect')
    .addToUi();
}

/** Кому можно выдать QR: директор (главный ключ) и активные менеджеры. */
function qrPeople_(ss) {
  var people = [];
  var main = String(settings_(ss)[KEY_SETTING] || '').trim();
  if (main) people.push({ title: 'Директор (главный ключ)', key: main, name: '' });
  cachedRows_(ss, MANAGERS_SHEET, true).slice(1).forEach(function (r) {
    var key = String(r[0] || '').trim();
    if (!key || /^(нет|no|false|0)$/i.test(String(r[3]).trim())) return;
    people.push({ title: String(r[1] || key) + ' (' + (/директор/i.test(String(r[2])) ? 'директор' : 'менеджер') + ')', key: key, name: String(r[1] || '') });
  });
  return people;
}

/**
 * Меню: QR-код для подключения телефона — адрес веб-приложения, ключ и имя. Код рисуется в браузере
 * (данные никуда не отправляются); телефон: Настройки → «Подключить по QR-коду».
 */
function qrConnect() {
  var ss = SpreadsheetApp.getActiveSpreadsheet();
  var people = qrPeople_(ss);
  var url = '';
  try { url = ScriptApp.getService().getUrl() || ''; } catch (e) {}
  var data = JSON.stringify({ url: url, people: people }).replace(/</g, '\\u003c');
  var html = '<!doctype html><html><head><meta charset="utf-8">' +
    '<script src="https://cdnjs.cloudflare.com/ajax/libs/qrcode-generator/1.4.4/qrcode.min.js"></script>' +
    '<style>body{font-family:Arial,sans-serif;font-size:14px;margin:8px}select,input{width:100%;margin:4px 0 10px;padding:6px;box-sizing:border-box}' +
    '#qr{text-align:center}#qr svg{width:300px;height:300px}.warn{color:#b00020;font-size:12px}</style></head><body>' +
    '<label>Кому</label><select id="who"></select>' +
    '<label>Адрес веб-приложения (…/exec)</label><input id="url">' +
    '<div id="qr"></div>' +
    '<p>Телефон: Настройки → Подключение → «Подключить по QR-коду».</p>' +
    '<p class="warn">В коде — ключ доступа. Не показывайте и не пересылайте его посторонним.</p>' +
    '<script>var D=' + data + ';var who=document.getElementById("who"),url=document.getElementById("url");' +
    'D.people.forEach(function(p,i){var o=document.createElement("option");o.value=i;o.textContent=p.title;who.appendChild(o);});' +
    'url.value=D.url.replace(/\\/dev$/,"/exec");' +
    'function draw(){var p=D.people[who.value];if(!p){document.getElementById("qr").textContent="Нет ключей: заполните «Ключ доступа» в Настройках или лист «Менеджеры».";return;}' +
    'qrcode.stringToBytes=qrcode.stringToBytesFuncs["UTF-8"];var q=qrcode(0,"M");' +
    'q.addData(JSON.stringify({ks:1,u:url.value.trim(),k:p.key,n:p.name}));q.make();' +
    'document.getElementById("qr").innerHTML=q.createSvgTag({cellSize:6,margin:4});}' +
    'who.onchange=draw;url.oninput=draw;draw();</script></body></html>';
  SpreadsheetApp.getUi().showModalDialog(HtmlService.createHtmlOutput(html).setWidth(420).setHeight(600), 'QR для подключения телефона');
}

/** Меню: сохранить токен МойСклад (проверяется запросом к МойСклад). */
function msConnect() {
  var ui = SpreadsheetApp.getUi();
  var answer = ui.prompt('МойСклад', 'Токен пользователя «Приложение KS» (МойСклад → Настройки → Обмен данными → Токены):', ui.ButtonSet.OK_CANCEL);
  if (answer.getSelectedButton() !== ui.Button.OK) return;
  var token = String(answer.getResponseText() || '').trim();
  if (!token) return;
  PropertiesService.getScriptProperties().setProperty(MS_TOKEN_PROPERTY, token);
  try {
    var org = ms_('get', '/entity/organization?limit=1');
    ui.alert('МойСклад подключён: ' + ((org.rows && org.rows[0] && org.rows[0].name) || 'OK'));
  } catch (err) {
    PropertiesService.getScriptProperties().deleteProperty(MS_TOKEN_PROPERTY);
    ui.alert('Не удалось подключиться: ' + err.message);
  }
}

function msDisconnect() {
  PropertiesService.getScriptProperties().deleteProperty(MS_TOKEN_PROPERTY);
  SpreadsheetApp.getUi().alert('МойСклад отключён');
}

function msToken_() {
  return String(PropertiesService.getScriptProperties().getProperty(MS_TOKEN_PROPERTY) || '');
}

function msEnabled_() {
  return msToken_() !== '';
}

/**
 * Действие с МойСклад без падения основного: `null` — МойСклад не подключён,
 * `{error}` — ошибка (КП в таблице всё равно сохранено), иначе результат.
 */
function msTry_(fn) {
  if (!msEnabled_()) return null;
  try {
    return fn() || {};
  } catch (err) {
    return { error: String((err && err.message) || err) };
  }
}

/** Запрос к JSON API МойСклад 1.2. */
function ms_(method, path, body) {
  var options = {
    method: method,
    headers: { Authorization: 'Bearer ' + msToken_(), 'Accept-Encoding': 'gzip', Accept: 'application/json;charset=utf-8' },
    contentType: 'application/json;charset=utf-8',
    muteHttpExceptions: true,
  };
  if (body !== undefined) options.payload = JSON.stringify(body);
  var res = UrlFetchApp.fetch(path.indexOf('https://') === 0 ? path : MS_API + path, options);
  var code = res.getResponseCode();
  var text = res.getContentText();
  var data = text ? JSON.parse(text) : {};
  if (code >= 400) {
    var e = data && data.errors && data.errors[0];
    if (code === 401) throw new Error('МойСклад: неверный токен');
    if (code === 403) throw new Error('МойСклад: у пользователя нет прав' + (e ? ' — ' + e.error : ''));
    throw new Error('МойСклад: ' + (e ? e.error : 'ошибка ' + code));
  }
  return data;
}

/** Все строки списка МойСклад (по 1000 за запрос). */
function msAll_(path) {
  var rows = [];
  var sep = path.indexOf('?') < 0 ? '?' : '&';
  for (var offset = 0; offset < 50000; offset += 1000) {
    var page = ms_('get', path + sep + 'limit=1000&offset=' + offset);
    var part = page.rows || [];
    rows = rows.concat(part);
    if (part.length < 1000) break;
  }
  return rows;
}

function msMeta_(type, id) {
  return { meta: { href: MS_API + '/entity/' + type + '/' + id, type: type, mediaType: 'application/json' } };
}

function msIdOf_(entity) {
  var href = (entity && entity.meta && entity.meta.href) || '';
  return href.substring(href.lastIndexOf('/') + 1).split('?')[0];
}

/**
 * Реквизиты организации по ИНН (DaData). Ключ — в свойствах скрипта DADATA_KEY (не в листах и не на телефонах).
 */
function dadataParty_(inn) {
  inn = inn.replace(/\D/g, '');
  if (inn.length !== 10 && inn.length !== 12) throw new Error('ИНН — 10 или 12 цифр');
  var key = PropertiesService.getScriptProperties().getProperty('DADATA_KEY');
  if (!key) throw new Error('Ключ DaData не задан: Apps Script → Настройки проекта → Свойства скрипта → DADATA_KEY');
  var res = UrlFetchApp.fetch('https://suggestions.dadata.ru/suggestions/api/4_1/rs/findById/party', {
    method: 'post',
    contentType: 'application/json',
    headers: { Authorization: 'Token ' + key, Accept: 'application/json' },
    payload: JSON.stringify({ query: inn, branch_type: 'MAIN' }),
    muteHttpExceptions: true,
  });
  var code = res.getResponseCode();
  if (code === 401 || code === 403) throw new Error('DaData: неверный ключ');
  if (code === 429) throw new Error('DaData: превышен лимит запросов, попробуйте позже');
  if (code >= 400) throw new Error('DaData: ошибка ' + code);
  var list = (JSON.parse(res.getContentText() || '{}').suggestions) || [];
  if (!list.length) throw new Error('Организация с ИНН ' + inn + ' не найдена');
  var s = list[0];
  var d = s.data || {};
  var name = d.name || {};
  var mgmt = d.management || {};
  return {
    inn: d.inn || inn,
    name: name.short_with_opf || s.value || '',
    fullName: name.full_with_opf || '',
    kpp: d.kpp || '',
    ogrn: d.ogrn || '',
    address: (d.address && (d.address.unrestricted_value || d.address.value)) || '',
    director: mgmt.name || '',
    post: mgmt.post || '',
    status: (d.state && d.state.status) || '',
  };
}

/** Первый EAN-13 из штрихкодов карточки МойСклад. */
function msEan13_(entity) {
  var codes = (entity && entity.barcodes) || [];
  for (var i = 0; i < codes.length; i++) {
    if (codes[i].ean13 && ean13Valid_(String(codes[i].ean13))) return String(codes[i].ean13);
  }
  return '';
}

function ean13Check_(first12) {
  var sum = 0;
  for (var i = 0; i < 12; i++) sum += Number(first12.charAt(i)) * (i % 2 === 0 ? 1 : 3);
  return String((10 - sum % 10) % 10);
}

function ean13Valid_(code) {
  return /^\d{13}$/.test(code) && ean13Check_(code.substring(0, 12)) === code.charAt(12);
}

/**
 * Внутренний EAN-13 (префикс 2 — для своих товаров) для товара или модификации без штрихкода.
 * Номер — по счётчику скрипта, с проверкой, что такого кода в МойСклад ещё нет. Уже есть EAN-13 — возвращается он.
 */
function msCreateBarcode_(type, id) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  if ((type !== 'product' && type !== 'variant') || !/^[\w-]{1,64}$/.test(id)) throw new Error('Неверный товар');
  var lock = LockService.getScriptLock();
  lock.waitLock(20000);
  try {
    var entity = ms_('get', '/entity/' + type + '/' + id);
    var existing = msEan13_(entity);
    if (existing) return existing;
    var props = PropertiesService.getScriptProperties();
    var next = Number(props.getProperty('EAN_COUNTER') || '0');
    for (var attempt = 0; attempt < 20; attempt++) {
      next += 1;
      var body = '2' + ('00000000000' + (90000000000 + next)).slice(-11);
      var code = body + ean13Check_(body);
      var taken = ms_('get', '/entity/assortment?filter=barcode=' + code).rows || [];
      if (taken.length) continue;
      props.setProperty('EAN_COUNTER', String(next));
      var barcodes = (entity.barcodes || []).concat([{ ean13: code }]);
      ms_('put', '/entity/' + type + '/' + id, { barcodes: barcodes });
      return code;
    }
    throw new Error('Не удалось подобрать свободный штрихкод');
  } finally {
    lock.releaseLock();
  }
}

var msCache_ = {};

function msFirst_(path) {
  if (!(path in msCache_)) msCache_[path] = (ms_('get', path).rows || [])[0] || null;
  return msCache_[path];
}

function msStore_(ss) {
  var name = String(settings_(ss)[MS_STORE_SETTING] || 'Электросталь').trim();
  return msFirst_('/entity/store?filter=name=' + encodeURIComponent(name));
}

/** Наше юрлицо в МойСклад — по ИНН из «Настроек». */
function msOrganization_(ss) {
  if (msCache_.org) return msCache_.org;
  var inn = String(settings_(ss)['ИНН'] || '').trim();
  var rows = ms_('get', '/entity/organization').rows || [];
  var org = rows.filter(function (o) { return inn && String(o.inn) === inn; })[0] || rows[0];
  if (!org) throw new Error('МойСклад: нет юрлица');
  msCache_.org = org;
  return org;
}

/** Клиент: по ИНН, затем по названию; нет — создаётся. */
function msCounterparty_(client, inn, email, phone, kpp, address) {
  inn = String(inn || '').replace(/\D/g, '');
  client = String(client || '').trim();
  var found = inn ? msFirst_('/entity/counterparty?filter=inn=' + inn) : null;
  if (!found && client) found = msFirst_('/entity/counterparty?filter=name=' + encodeURIComponent(client));
  if (found) return found;
  var body = { name: client || ('Клиент ' + inn) };
  if (inn) {
    body.inn = inn;
    body.companyType = inn.length === 12 ? 'entrepreneur' : 'legal';
  }
  if (email) body.email = String(email);
  if (phone) body.phone = String(phone);
  if (kpp && inn.length === 10) body.kpp = String(kpp);
  if (address) body.legalAddress = String(address);
  return ms_('post', '/entity/counterparty', body);
}

/** Статус «Заказа покупателя» по нашему названию; нет — создаётся. */
function msState_(status) {
  var meta = msCache_.orderMeta || (msCache_.orderMeta = ms_('get', '/entity/customerorder/metadata'));
  var state = (meta.states || []).filter(function (st) { return st.name === status; })[0];
  if (state) return state;
  state = ms_('post', '/entity/customerorder/metadata/states', {
    name: status, color: MS_STATE_COLOR, stateType: status === 'Отказ' ? 'Unsuccessful' : 'Regular',
  });
  meta.states = (meta.states || []).concat([state]);
  return state;
}

/** Услуга для позиций «под заказ» (калькулятор) и частичных счетов. */
function msService_(ss) {
  var name = String(settings_(ss)[MS_SERVICE_SETTING] || 'Трикотажные изделия по ТЗ').trim();
  var found = msFirst_('/entity/service?filter=name=' + encodeURIComponent(name));
  return found || ms_('post', '/entity/service', { name: name });
}

function msStateMeta_(state) {
  return { meta: { href: MS_API + '/entity/customerorder/metadata/states/' + msIdOf_(state), type: 'state', mediaType: 'application/json' } };
}

function msOrderFor_(quoteId) {
  if (!quoteId) return null;
  return msFirst_('/entity/customerorder?filter=externalCode=' + encodeURIComponent(quoteId));
}

function msVat_(ss) {
  return Number(String(settings_(ss)['Ставка НДС, %'] || '22').replace(',', '.')) || 0;
}

function msVatIncluded_(ss) {
  var v = String(settings_(ss)['Цены с НДС'] || 'да').trim().toLowerCase();
  return ['нет', 'no', 'false', '0', 'ложь'].indexOf(v) < 0;
}

var MS_FILTERS_SETTING = 'МойСклад: фильтры';
var MS_CLIENT_CHARS_SETTING = 'МойСклад: характеристики для клиента';
var MS_DEFAULT_FILTERS = 'Артикул, Цвет, Тип резинки, Тип, Артикул производитель';
var MS_DEFAULT_CLIENT = 'Состав / материала, Цвет, Размер';
var MS_BADGE_CHARS = ['Метка 1', 'Метка 2'];

function listSetting_(ss, name, fallback) {
  return String(settings_(ss)[name] || fallback).split(',').map(function (x) { return x.trim(); }).filter(function (x) { return x; });
}

function msTiers_(salePrices) {
  var byName = {};
  (salePrices || []).forEach(function (sp) {
    if (sp.priceType && sp.priceType.name) byName[sp.priceType.name] = (Number(sp.value) || 0) / 100;
  });
  return MS_TIERS.map(function (t) { return { from: t[1], price: byName[t[0]] || 0 }; }).filter(function (t) { return t.price > 0; });
}

/**
 * Карточка товара МойСклад из приложения (только директор): название, артикул, описание, вес,
 * минимальная цена и цены по тиражам. У модификации название, артикул, описание и вес — товара;
 * цены — свои (если своих не было, берутся цены товара, чтобы не потерять остальные тиражи).
 */
function msUpdateProduct_(type, id, changes) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  if ((type !== 'product' && type !== 'variant') || !/^[\w-]+$/.test(id)) throw new Error('Неверный товар');
  var entity = ms_('get', '/entity/' + type + '/' + id);
  var parentId = type === 'variant' ? msIdOf_(entity.product) : id;
  var parent = type === 'variant' ? ms_('get', '/entity/product/' + parentId) : entity;
  var card = {};
  if (changes.name != null && String(changes.name).trim()) card.name = String(changes.name).trim().substring(0, 255);
  if (changes.article != null) card.article = String(changes.article).trim().substring(0, 255);
  if (changes.description != null) card.description = String(changes.description).substring(0, 4096);
  if (changes.weight != null && isFinite(Number(changes.weight))) card.weight = Number(changes.weight);
  var own = {};
  if (changes.minPrice != null && isFinite(Number(changes.minPrice))) {
    own.minPrice = { value: Math.round(Number(changes.minPrice) * 100), currency: (entity.minPrice || parent.minPrice || {}).currency };
    if (!own.minPrice.currency) delete own.minPrice.currency;
  }
  var prices = changes.prices || {};
  if (Object.keys(prices).length) {
    var hasOwn = (entity.salePrices || []).some(function (sp) { return Number(sp.value) > 0; });
    var source = hasOwn ? entity.salePrices : (parent.salePrices || []);
    var types = {};
    var list = ms_('get', '/context/companysettings/pricetype');
    (Array.isArray(list) ? list : list.rows || []).forEach(function (pt) { types[pt.name] = pt; });
    var byName = {};
    var out = (source || []).map(function (sp) {
      var copy = { value: sp.value, priceType: sp.priceType };
      if (sp.currency) copy.currency = sp.currency;
      if (sp.priceType && sp.priceType.name) byName[sp.priceType.name] = copy;
      return copy;
    });
    MS_TIERS.forEach(function (t) {
      var v = prices[String(t[1])];
      if (v == null || !isFinite(Number(v))) return;
      var cents = Math.round(Number(v) * 100);
      if (byName[t[0]]) byName[t[0]].value = cents;
      else if (types[t[0]]) out.push({ value: cents, priceType: { meta: types[t[0]].meta, name: t[0] } });
    });
    own.salePrices = out;
  }
  if (type === 'variant') {
    if (Object.keys(card).length) ms_('put', '/entity/product/' + parentId, card);
    if (Object.keys(own).length) ms_('put', '/entity/variant/' + id, own);
  } else {
    Object.keys(own).forEach(function (k) { card[k] = own[k]; });
    if (Object.keys(card).length) ms_('put', '/entity/product/' + id, card);
  }
  var fresh = ms_('get', '/entity/' + type + '/' + id);
  var tiers = msTiers_(fresh.salePrices);
  if (!tiers.length && type === 'variant') tiers = msTiers_(ms_('get', '/entity/product/' + parentId).salePrices);
  return { id: id, tiers: tiers };
}

// ---------------------------------------------------------------- Склад: отгрузка и инвентаризация по сканеру

function msCheckId_(id) {
  if (!/^[\w-]+$/.test(String(id || ''))) throw new Error('Неверный документ');
  return id;
}

/** Заказы покупателей, отгруженные не полностью (последние 100). */
function msShipList_() {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  var rows = ms_('get', '/entity/customerorder?limit=100&order=moment,desc&expand=agent&filter=' + encodeURIComponent('applicable=true')).rows || [];
  return rows.map(function (o) {
    return {
      id: o.id, name: o.name, client: (o.agent && o.agent.name) || '', moment: msTime_(o.moment),
      sum: (Number(o.sum) || 0) / 100, shipped: (Number(o.shippedSum) || 0) / 100, quoteId: String(o.externalCode || ''),
    };
  }).filter(function (o) { return o.sum > 0 && o.shipped + 0.009 < o.sum; });
}

/** Позиции заказа: сколько заказано и уже отгружено, штрихкод — для сверки при сканировании. */
function msShipOrder_(orderId) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  msCheckId_(orderId);
  var rows = ms_('get', '/entity/customerorder/' + orderId + '/positions?limit=100&expand=assortment').rows || [];
  return rows.filter(function (p) { return p.assortment && p.assortment.meta && p.assortment.meta.type !== 'service'; }).map(function (p) {
    var a = p.assortment;
    return {
      id: msIdOf_(a), type: a.meta.type, name: a.name || '', article: a.article || a.code || '',
      barcode: msEan13_(a), quantity: Number(p.quantity) || 0, shipped: Number(p.shipped) || 0,
    };
  });
}

/** Отгрузка по заказу: только отсканированные количества; склад — из «Настроек». */
function msShip_(ss, orderId, items) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  msCheckId_(orderId);
  var qty = {};
  (items || []).forEach(function (i) { if (Number(i.qty) > 0) qty[String(i.id)] = (qty[String(i.id)] || 0) + Number(i.qty); });
  if (!Object.keys(qty).length) throw new Error('Ничего не отсканировано');
  var template = ms_('put', '/entity/demand/new', { customerOrder: msMeta_('customerorder', orderId) });
  var positions = ((template.positions && template.positions.rows) || template.positions || []).map(function (p) {
    var id = msIdOf_(p.assortment);
    if (!qty[id]) return null;
    var out = { assortment: p.assortment, quantity: qty[id], price: p.price, vat: p.vat, discount: p.discount };
    delete qty[id];
    return out;
  }).filter(function (p) { return p; });
  if (Object.keys(qty).length) throw new Error('В заказе нет отсканированных товаров: ' + Object.keys(qty).length);
  var body = {};
  Object.keys(template).forEach(function (k) { if (k !== 'positions') body[k] = template[k]; });
  body.positions = positions;
  var store = msStore_(ss);
  if (store) body.store = msMeta_('store', store.id);
  var demand = ms_('post', '/entity/demand', body);
  return { id: demand.id, name: demand.name, positions: positions.length };
}

// ---------------------------------------------------------------- Приёмка: проведение по сканеру

/** Что принимать: непроведённые приёмки и заказы поставщикам, принятые не полностью (последние 100). */
function msReceiveList_() {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  var drafts = (ms_('get', '/entity/supply?limit=100&order=moment,desc&expand=agent&filter=' + encodeURIComponent('applicable=false')).rows || [])
    .map(function (d) {
      return { type: 'supply', id: d.id, name: d.name, supplier: (d.agent && d.agent.name) || '', moment: msTime_(d.moment), sum: (Number(d.sum) || 0) / 100 };
    });
  var orders = (ms_('get', '/entity/purchaseorder?limit=100&order=moment,desc&expand=agent&filter=' + encodeURIComponent('applicable=true')).rows || [])
    .filter(function (o) { return (Number(o.sum) || 0) > 0 && (Number(o.shippedSum) || 0) + 1 < (Number(o.sum) || 0); })
    .map(function (o) {
      return { type: 'purchaseorder', id: o.id, name: o.name, supplier: (o.agent && o.agent.name) || '', moment: msTime_(o.moment), sum: (Number(o.sum) || 0) / 100 };
    });
  return drafts.concat(orders);
}

function msReceiveType_(type) {
  if (type !== 'supply' && type !== 'purchaseorder') throw new Error('Неверный документ');
  return type;
}

/** Позиции документа: сколько ждём, штрихкод — для сверки при сканировании. */
function msReceiveDoc_(type, id) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  msReceiveType_(type);
  msCheckId_(id);
  var rows = ms_('get', '/entity/' + type + '/' + id + '/positions?limit=100&expand=assortment').rows || [];
  return rows.filter(function (p) { return p.assortment && p.assortment.meta && p.assortment.meta.type !== 'service'; }).map(function (p) {
    var a = p.assortment;
    return {
      id: msIdOf_(a), type: a.meta.type, name: a.name || '', article: a.article || a.code || '', barcode: msEan13_(a),
      quantity: Number(p.quantity) || 0, shipped: type === 'purchaseorder' ? (Number(p.shipped) || 0) : 0, positionId: p.id,
    };
  });
}

/**
 * Провести приёмку с фактическим количеством.
 * Черновик приёмки: количество позиций — как отсканировали (не принятые — удаляются), затем «проведено».
 * Заказ поставщику: создаётся проведённая приёмка по отсканированному.
 */
function msReceive_(ss, type, id, items) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  msReceiveType_(type);
  msCheckId_(id);
  var qty = {};
  (items || []).forEach(function (i) { if (Number(i.qty) > 0) qty[String(i.id)] = (qty[String(i.id)] || 0) + Number(i.qty); });
  if (!Object.keys(qty).length) throw new Error('Ничего не отсканировано');
  if (type === 'supply') {
    var rows = ms_('get', '/entity/supply/' + id + '/positions?limit=1000').rows || [];
    var accepted = 0;
    rows.forEach(function (p) {
      var aid = msIdOf_(p.assortment);
      if (qty[aid]) {
        ms_('put', '/entity/supply/' + id + '/positions/' + p.id, { quantity: qty[aid] });
        delete qty[aid];
        accepted++;
      } else {
        ms_('delete', '/entity/supply/' + id + '/positions/' + p.id);
      }
    });
    if (Object.keys(qty).length) throw new Error('В приёмке нет отсканированных товаров: ' + Object.keys(qty).length);
    var done = ms_('put', '/entity/supply/' + id, { applicable: true });
    return { id: id, name: done.name, positions: accepted };
  }
  var template = ms_('put', '/entity/supply/new', { purchaseOrder: msMeta_('purchaseorder', id) });
  var positions = ((template.positions && template.positions.rows) || template.positions || []).map(function (p) {
    var aid = msIdOf_(p.assortment);
    if (!qty[aid]) return null;
    var out = { assortment: p.assortment, quantity: qty[aid], price: p.price, vat: p.vat };
    delete qty[aid];
    return out;
  }).filter(function (p) { return p; });
  if (Object.keys(qty).length) throw new Error('В заказе поставщику нет отсканированных товаров: ' + Object.keys(qty).length);
  var body = {};
  Object.keys(template).forEach(function (k) { if (k !== 'positions') body[k] = template[k]; });
  body.positions = positions;
  body.applicable = true;
  var store = msStore_(ss);
  if (store) body.store = msMeta_('store', store.id);
  var supply = ms_('post', '/entity/supply', body);
  return { id: supply.id, name: supply.name, positions: positions.length };
}

/** Инвентаризация склада из «Настроек»: отсканированные товары и посчитанное количество. */
function msInventory_(ss, items) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  var store = msStore_(ss);
  if (!store) throw new Error('МойСклад: склад из «Настроек» не найден');
  var positions = (items || []).filter(function (i) {
    return (i.type === 'product' || i.type === 'variant') && /^[\w-]+$/.test(String(i.id)) && Number(i.qty) >= 0;
  }).map(function (i) {
    return { assortment: msMeta_(i.type, String(i.id)), quantity: Number(i.qty) };
  });
  if (!positions.length) throw new Error('Ничего не отсканировано');
  var doc = ms_('post', '/entity/inventory', {
    organization: msMeta_('organization', msOrganization_(ss).id),
    store: msMeta_('store', store.id),
    positions: positions,
  });
  return { id: doc.id, name: doc.name, positions: positions.length };
}

/** Первое фото товара МойСклад (у модификации без фото — фото товара) в base64; `null` — фото нет. */
function msImage_(type, id) {
  if (!msEnabled_()) throw new Error('МойСклад не подключён');
  if ((type !== 'product' && type !== 'variant') || !/^[\w-]+$/.test(id)) throw new Error('Неверный товар');
  var rows = ms_('get', '/entity/' + type + '/' + id + '/images').rows || [];
  if (!rows.length && type === 'variant') {
    var v = ms_('get', '/entity/variant/' + id);
    rows = ms_('get', '/entity/product/' + msIdOf_(v.product) + '/images').rows || [];
  }
  if (!rows.length) return null;
  var img = rows[0];
  // Крупные фото (> 1,5 МБ) — уменьшенной копией, чтобы КП и PDF оставались лёгкими.
  var href = (img.size > 1500000 && img.miniature && img.miniature.downloadHref) || (img.meta && img.meta.downloadHref);
  if (!href) return null;
  var res = UrlFetchApp.fetch(href, { headers: { Authorization: 'Bearer ' + msToken_(), 'Accept-Encoding': 'gzip' }, muteHttpExceptions: true, followRedirects: true });
  if (res.getResponseCode() >= 400) throw new Error('МойСклад: фото не скачалось (' + res.getResponseCode() + ')');
  return Utilities.base64Encode(res.getBlob().getBytes());
}

function msMoney_(v) {
  return ((v && v.value) || 0) / 100;
}

/**
 * Товары и модификации с ценами по тиражам, остатки склада, характеристики и клиенты — для приложения.
 * Товар с модификациями отдаётся модификациями (у каждой свой цвет / размер / остаток); цены модификации —
 * свои, а если не заданы — товара. Закупочная цена — только директору.
 */
function msCatalog_(ss, director) {
  if (!msEnabled_()) return { enabled: false };
  var store = msStore_(ss);
  var stock = {};
  if (store) {
    var report = ms_('get', '/report/stock/bystore/current?filter=storeId=' + store.id);
    (report.rows || report || []).forEach(function (r) {
      stock[r.assortmentId] = (stock[r.assortmentId] || 0) + (Number(r.stock) || 0);
    });
  }
  var wanted = {};
  listSetting_(ss, MS_FILTERS_SETTING, MS_DEFAULT_FILTERS)
    .concat(listSetting_(ss, MS_CLIENT_CHARS_SETTING, MS_DEFAULT_CLIENT)).concat(MS_BADGE_CHARS)
    .forEach(function (n) { wanted[n] = true; });

  var products = msAll_('/entity/product?filter=archived=false');
  var byId = {};
  products.forEach(function (p) { byId[p.id] = p; });
  var variants = msAll_('/entity/variant?filter=archived=false');
  var withVariants = {};
  variants.forEach(function (v) { withVariants[msIdOf_(v.product)] = true; });

  function popular(p) {
    var a = (p.attributes || []).filter(function (x) { return x.name === 'Популярный в категории' && x.value === true; });
    return a.length > 0;
  }
  function item(id, type, parent, own, chars) {
    var tiers = msTiers_(own.salePrices);
    if (!tiers.length && own !== parent) tiers = msTiers_(parent.salePrices);
    if (!tiers.length) return null;
    var badges = MS_BADGE_CHARS.map(function (n) { return chars[n]; }).filter(function (b) { return b && b !== '-'; });
    if (popular(parent) && badges.indexOf('Популярный') < 0) badges.push('Популярный');
    MS_BADGE_CHARS.forEach(function (n) { delete chars[n]; });
    if (!chars['Артикул'] && (own.article || parent.article || own.code)) chars['Артикул'] = own.article || parent.article || own.code;
    var out = {
      id: id,
      type: type,
      name: parent.name,
      article: chars['Артикул'] || '',
      group: parent.pathName || '',
      weight: Number(own.weight || parent.weight) || 0,
      minPrice: msMoney_(own.minPrice) || msMoney_(parent.minPrice),
      tiers: tiers,
      stock: store ? (stock[id] || 0) : null,
      chars: chars,
      badges: badges,
      // EAN-13 для этикетки: у модификации — свой, у товара без модификаций — товара.
      barcode: msEan13_(own),
    };
    if (director) out.buyPrice = msMoney_(own.buyPrice) || msMoney_(parent.buyPrice);
    return out;
  }
  var items = [];
  products.forEach(function (p) {
    if (!withVariants[p.id]) {
      var it = item(p.id, 'product', p, p, {});
      if (it) items.push(it);
    }
  });
  variants.forEach(function (v) {
    var parent = byId[msIdOf_(v.product)];
    if (!parent) return;
    var chars = {};
    (v.characteristics || []).forEach(function (c) {
      if (wanted[c.name] && String(c.value || '').trim() !== '') chars[c.name] = String(c.value).trim();
    });
    var it = item(v.id, 'variant', parent, v, chars);
    if (it) items.push(it);
  });
  var clients = msAll_('/entity/counterparty?filter=archived=false').map(function (c) {
    return {
      name: c.name, inn: c.inn || '', kpp: c.kpp || '', email: c.email || '', phone: c.phone || '',
      address: c.legalAddress || c.actualAddress || '',
    };
  });
  return {
    enabled: true,
    store: store ? store.name : '',
    filters: listSetting_(ss, MS_FILTERS_SETTING, MS_DEFAULT_FILTERS),
    clientChars: listSetting_(ss, MS_CLIENT_CHARS_SETTING, MS_DEFAULT_CLIENT),
    products: items,
    clients: clients,
    loadedAt: Date.now(),
  };
}

/** КП → «Заказ покупателя» (создаётся или обновляется по ID КП). */
function msSaveOrder_(ss, q, number) {
  var service = null;
  var vat = msVat_(ss);
  var custom = [];
  var positions = (q.lines || []).map(function (l) {
    var assortment;
    if (l.msId) {
      assortment = msMeta_(l.msType === 'variant' ? 'variant' : 'product', l.msId);
    } else {
      service = service || msService_(ss);
      assortment = msMeta_('service', service.id);
      custom.push('• ' + [l.product, l.params].filter(function (x) { return x; }).join(', ') + ' — ' + l.qty + ' ' + (l.unit || 'шт'));
    }
    return { quantity: Number(l.qty) || 0, price: Math.round((Number(l.price) || 0) * 100), vat: vat, assortment: assortment };
  });
  var description = ['Коммерческое предложение № ' + number + ' (приложение Фабрика "KS")', q.comment || '']
    .concat(custom.length ? ['Позиции под заказ:'].concat(custom) : [])
    .filter(function (x) { return x; }).join('\n');
  var body = {
    name: MS_ORDER_PREFIX + number,
    externalCode: String(q.id || ''),
    description: description,
    vatEnabled: vat > 0,
    vatIncluded: msVatIncluded_(ss),
    positions: positions,
  };
  var existing = msOrderFor_(q.id);
  var saved;
  if (existing) {
    saved = ms_('put', '/entity/customerorder/' + existing.id, body);
  } else {
    body.organization = msMeta_('organization', msOrganization_(ss).id);
    body.agent = msMeta_('counterparty', msCounterparty_(q.client, q.inn, q.email, q.phone, q.kpp, q.address).id);
    var store = msStore_(ss);
    if (store) body.store = msMeta_('store', store.id);
    body.state = msStateMeta_(msState_(STATUSES[0]));
    saved = ms_('post', '/entity/customerorder', body);
  }
  markQuote_(ss, q.id, Q.ms, saved.name);
  return { id: saved.id, name: saved.name };
}

/** Отметка в строке КП (столбец [col]); колонка добавляется, если её нет. */
function markQuote_(ss, quoteId, col, value) {
  var sheet = ss.getSheetByName(SHEETS.quotes);
  if (!sheet || !quoteId) return;
  if (sheet.getMaxColumns() < col) sheet.insertColumnsAfter(sheet.getMaxColumns(), col - sheet.getMaxColumns());
  var data = sheet.getDataRange().getValues();
  for (var i = 1; i < data.length; i++) {
    if (String(data[i][Q.id - 1]) === String(quoteId)) {
      sheet.getRange(i + 1, col).setValue(text_(value));
      return;
    }
  }
}

function msSetState_(quoteId, status) {
  var order = msOrderFor_(quoteId);
  if (!order) return { skipped: 'Заказа в МойСклад ещё нет' };
  var state = msState_(status);
  ms_('put', '/entity/customerorder/' + order.id, { state: msStateMeta_(state) });
  return { name: order.name, state: status };
}

/** «Счёт покупателю»: вся сумма — позициями заказа, частичная (предоплата, остаток) — одной строкой. */
function msInvoice_(ss, inv) {
  var order = msOrderFor_(inv.quoteId);
  var vat = msVat_(ss);
  var amount = Math.round((Number(inv.amount) || 0) * 100);
  var positions;
  if (order && amount === Math.round(Number(order.sum) || 0)) {
    positions = (ms_('get', '/entity/customerorder/' + order.id + '/positions').rows || []).map(function (p) {
      return { quantity: p.quantity, price: p.price, vat: p.vat, discount: p.discount || 0, assortment: { meta: p.assortment.meta } };
    });
  } else {
    positions = [{ quantity: 1, price: amount, vat: vat, assortment: msMeta_('service', msService_(ss).id) }];
  }
  var body = {
    organization: msMeta_('organization', msOrganization_(ss).id),
    agent: order ? { meta: order.agent.meta } : msMeta_('counterparty', msCounterparty_(inv.client, inv.inn).id),
    description: String(inv.purpose || ''),
    vatEnabled: vat > 0,
    vatIncluded: msVatIncluded_(ss),
    positions: positions,
  };
  if (order) body.customerOrder = { meta: order.meta };
  var saved = ms_('post', '/entity/invoiceout', body);
  return { id: saved.id, name: saved.name, number: parseInt(String(saved.name).replace(/\D/g, ''), 10) || 0 };
}

/** Входящий платёж, привязанный к заказу. */
function msPayment_(ss, p) {
  var order = msOrderFor_(p.quoteId);
  var sum = Math.round((Number(p.amount) || 0) * 100);
  var body = {
    organization: msMeta_('organization', msOrganization_(ss).id),
    agent: order ? { meta: order.agent.meta } : msMeta_('counterparty', msCounterparty_(p.client, '').id),
    sum: sum,
    paymentPurpose: String(p.note || ('Оплата по КП № ' + (p.quoteNumber || ''))),
  };
  if (order) body.operations = [{ meta: order.meta, linkedSum: sum }];
  var saved = ms_('post', '/entity/paymentin', body);
  return { id: saved.id, name: saved.name };
}

/** Заказы из приложения (номер «КП-…»): сумма, оплачено, отгружено, статус. */
function msOrders_() {
  return msAll_('/entity/customerorder?filter=name~' + encodeURIComponent(MS_ORDER_PREFIX)).map(function (o) {
    return {
      quoteId: String(o.externalCode || ''),
      name: o.name,
      sum: (Number(o.sum) || 0) / 100,
      paid: (Number(o.payedSum) || 0) / 100,
      shipped: (Number(o.shippedSum) || 0) / 100,
    };
  }).filter(function (o) { return o.quoteId; });
}
