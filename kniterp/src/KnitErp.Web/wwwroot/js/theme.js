// knitERP: тема до отрисовки страницы (подключается в <head>, чтобы не было вспышки тёмной темы).
// Выбор — в профиле: тёмная, светлая, как в системе; хранится в этом браузере.
(function () {
    var theme = 'dark';
    try { theme = localStorage.getItem('kniterp.theme') || 'dark'; } catch (e) { }
    if (['dark', 'light', 'auto'].indexOf(theme) < 0) theme = 'dark';
    document.documentElement.setAttribute('data-theme', theme);
})();
