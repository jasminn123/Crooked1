const themeStorageKey = 'crkd-theme';

function applyTheme(theme) {
    const activeTheme = theme === 'light' ? 'light' : 'dark';
    document.documentElement.dataset.theme = activeTheme;
    localStorage.setItem(themeStorageKey, activeTheme);

    document.querySelectorAll('.theme-toggle-button').forEach(button => {
        button.textContent = activeTheme === 'light' ? 'Dark Mode' : 'Light Mode';
        button.setAttribute(
            'aria-label',
            activeTheme === 'light' ? 'Switch to dark mode' : 'Switch to light mode'
        );
    });

    window.dispatchEvent(new CustomEvent('themechange', { detail: { theme: activeTheme } }));
}

function toggleDarkMode() {
    applyTheme(document.documentElement.dataset.theme === 'light' ? 'dark' : 'light');
}

applyTheme(localStorage.getItem(themeStorageKey));
