// knitERP: подписи столбцов и раскрытие строк таблиц на телефоне, горячие клавиши и фокус панели инструментов.
// Отдельным файлом, а не в разметке: политика безопасности (CSP) запрещает встроенные скрипты.
// Подписи столбцов для карточного вида таблиц на телефоне: data-label ячейки = текст заголовка её столбца.
// Blazor перерисовывает строки, поэтому подписи ставятся заново при каждом изменении страницы.
(function () {
    function label() {
        document.querySelectorAll('.scroll table:not(.matrix)').forEach(function (table) {
            var heads = Array.prototype.map.call(table.querySelectorAll('thead th'), function (th) { return th.textContent.trim(); });
            if (heads.length === 0) return;
            table.querySelectorAll('tbody tr').forEach(function (tr) {
                var col = 0;
                Array.prototype.forEach.call(tr.children, function (td) {
                    var span = td.colSpan || 1;
                    var text = span === 1 ? heads[col] : '';
                    if (text && td.getAttribute('data-label') !== text) td.setAttribute('data-label', text);
                    col += span;
                });
            });
        });
    }
    // Второстепенные столбцы (th.opt): ячейки помечаются data-opt, строка получает «Подробнее» на телефоне.
    function priority() {
        document.querySelectorAll('.scroll table:not(.matrix)').forEach(function (table) {
            var opt = Array.prototype.map.call(table.querySelectorAll('thead th'), function (th) { return th.classList.contains('opt'); });
            if (opt.indexOf(true) < 0) return;
            table.querySelectorAll('tbody tr').forEach(function (tr) {
                var col = 0, has = false;
                Array.prototype.forEach.call(tr.children, function (td) {
                    var span = td.colSpan || 1;
                    if (span === 1 && opt[col]) { td.setAttribute('data-opt', ''); has = true; }
                    col += span;
                });
                if (has && !tr.hasAttribute('data-has-opt')) {
                    tr.setAttribute('data-has-opt', '');
                    tr.setAttribute('tabindex', '0');
                    tr.setAttribute('aria-expanded', 'false');
                }
            });
        });
    }
    function toggleRow(tr) {
        if (!window.matchMedia('(max-width: 599px)').matches) return;
        var open = tr.hasAttribute('data-open');
        if (open) tr.removeAttribute('data-open'); else tr.setAttribute('data-open', '');
        tr.setAttribute('aria-expanded', open ? 'false' : 'true');
    }
    document.addEventListener('click', function (e) {
        var tr = e.target.closest && e.target.closest('tr[data-has-opt]');
        if (!tr || e.target.closest('a, button, input, select, textarea, label')) return;
        toggleRow(tr);
    });
    document.addEventListener('keydown', function (e) {
        if ((e.key === 'Enter' || e.key === ' ') && e.target.matches && e.target.matches('tr[data-has-opt]')) { e.preventDefault(); toggleRow(e.target); }
    });
    var pending = false;
    new MutationObserver(function () {
        if (pending) return;
        pending = true;
        requestAnimationFrame(function () { pending = false; label(); priority(); });
    }).observe(document.documentElement, { childList: true, subtree: true });
    document.addEventListener('DOMContentLoaded', function () { label(); priority(); });
})();

// Панель быстрого доступа: горячие клавиши, фокус в панели инструмента и обратно, буфер обмена.
window.kniterp = {
    _returnFocus: null,
    registerHotkeys: function (dotnet) {
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K' || e.key === 'л' || e.key === 'Л')) {
                e.preventDefault();
                dotnet.invokeMethodAsync('OnHotkey', 'search');
            } else if (e.key === 'Escape' && document.querySelector('.drawer.open')) {
                dotnet.invokeMethodAsync('OnHotkey', 'escape');
            }
        });
    },
    focusDrawer: function (id) {
        if (!document.querySelector('.drawer.open:focus-within')) kniterp._returnFocus = document.activeElement;
        setTimeout(function () { var el = document.getElementById(id); if (el) el.focus(); }, 60);
    },
    restoreFocus: function () {
        var el = kniterp._returnFocus;
        kniterp._returnFocus = null;
        if (el && document.contains(el)) el.focus();
    },
    copy: async function (text) {
        try { await navigator.clipboard.writeText(text); return true; } catch (e) { return false; }
    }
};
