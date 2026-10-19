const themeStorageKey = 'crkd-theme';
// Matches --theme-transition-duration in dashboard.css.
const themeTransitionMs = 300;
let themeTransitionTimer;

function updateThemeToggleButtons(activeTheme) {
    document.querySelectorAll('.theme-toggle-button').forEach(button => {
        button.textContent = activeTheme === 'light' ? 'Dark Mode' : 'Light Mode';
        button.setAttribute(
            'aria-label',
            activeTheme === 'light' ? 'Switch to dark mode' : 'Switch to light mode'
        );
    });
}

function applyTheme(theme) {
    const activeTheme = theme === 'light' ? 'light' : 'dark';
    document.documentElement.dataset.theme = activeTheme;
    localStorage.setItem(themeStorageKey, activeTheme);
    updateThemeToggleButtons(activeTheme);

    window.dispatchEvent(new CustomEvent('themechange', { detail: { theme: activeTheme } }));
}

function toggleDarkMode() {
    // Fade the colours between themes instead of switching them instantly.
    const root = document.documentElement;
    root.classList.add('theme-transition');
    window.clearTimeout(themeTransitionTimer);
    themeTransitionTimer = window.setTimeout(() => {
        root.classList.remove('theme-transition');
    }, themeTransitionMs + 100);

    applyTheme(root.dataset.theme === 'light' ? 'dark' : 'light');
}

applyTheme(localStorage.getItem(themeStorageKey));

// Pages load this script in <head>, before the toggle buttons exist.
document.addEventListener('DOMContentLoaded', () => {
    updateThemeToggleButtons(document.documentElement.dataset.theme);
});
