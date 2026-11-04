/* =========================
   SESSION
   Loaded in <head> on every page behind the login (admin pages and the POS), so signed-out
   visitors are sent to the login page before anything is drawn. The server's session cookie
   is what really protects the data: the API answers 401 without it.
========================= */
const Session = (() => {
    const LOGIN_PAGE = 'index.html';
    // Saved by index.js at login. The theme setting is kept on logout.
    const SESSION_KEYS = ['userRole', 'userName', 'userUsername'];

    const isSignedIn = () => Boolean(localStorage.getItem('userRole'));

    function toLogin() {
        SESSION_KEYS.forEach(key => localStorage.removeItem(key));
        // Hide the page first so its data doesn't stay on screen while the login page loads.
        document.documentElement.style.visibility = 'hidden';
        // replace() keeps this page out of the history, so Back doesn't return to it.
        window.location.replace(LOGIN_PAGE);
    }

    // Deletes the session cookie on the server, then leaves for the login page.
    async function signOut() {
        try {
            await fetch('/api/Auth/logout', { method: 'POST' });
        } catch (error) {
            console.error('Logout Error:', error);
        }
        toLogin();
    }

    // Any 401 from our API means the server session is gone (logged out in another tab,
    // expired, or the server was redeployed), so go back to the login page.
    const nativeFetch = window.fetch.bind(window);
    window.fetch = async (...args) => {
        const response = await nativeFetch(...args);
        if (response.status === 401 && response.url) {
            const url = new URL(response.url);
            if (url.origin === window.location.origin && url.pathname.startsWith('/api/')) toLogin();
        }
        return response;
    };

    if (!isSignedIn()) toLogin();

    // Back and Forward can show this page from the browser's memory without running any
    // scripts again, so check the session once more whenever that happens.
    window.addEventListener('pageshow', event => {
        if (!event.persisted) return;
        if (!isSignedIn()) {
            toLogin();
            return;
        }
        fetch('/api/Auth/session').catch(() => {});
    });

    return { signOut };
})();
