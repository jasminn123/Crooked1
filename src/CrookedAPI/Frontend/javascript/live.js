// Live updates over Supabase Realtime (websockets). When data changes, the server broadcasts
// which kinds changed (transactions, products, exchanges, activity, staff, forecast,
// notifications) on one channel. Pages register what they show with
// Live.on(topics, reload) and reload it quietly. Nothing happens until SUPABASE_URL and
// SUPABASE_ANON_KEY are set on the server. Load it after ui.js.
const Live = (() => {
    const SUPABASE_SCRIPT = 'https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2.57.4/dist/umd/supabase.js';
    const listeners = [];
    let connecting = null;
    let status = null;
    let missedUpdates = false;

    // refresh runs at most once per burst of changes (wait ms after the last one).
    function on(topics, refresh, { wait = 400 } = {}) {
        listeners.push({ topics: new Set(topics), refresh: UI.debounce(refresh, wait) });
        start();
    }

    function notify(topics) {
        listeners.forEach(listener => {
            if (topics.some(topic => listener.topics.has(topic))) listener.refresh(topics);
        });
    }

    function loadScript(src) {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = src;
            script.async = true;
            script.onload = resolve;
            script.onerror = () => reject(new Error('The Supabase client could not be loaded.'));
            document.head.appendChild(script);
        });
    }

    // A small "Live" pill in the top bar, so people know the page updates by itself.
    function setStatus(state) {
        if (!status) {
            const bar = document.querySelector('.header-icons') || document.querySelector('.pos-header');
            if (!bar) return;
            status = UI.el('span', 'live-status');
            const profile = bar.querySelector('.profile-section');
            if (profile && profile.parentElement === bar) bar.insertBefore(status, profile);
            else bar.prepend(status);
        }

        const states = {
            live: ['Live', 'Changes from other devices appear here automatically.'],
            connecting: ['Connecting...', 'Connecting to live updates.'],
            offline: ['Offline', 'Live updates are paused. Reload the page to see the latest data.']
        };
        const [label, title] = states[state];
        status.className = `live-status is-${state}`;
        status.textContent = label;
        status.title = title;
    }

    async function connect() {
        const config = await UI.fetchJson('/api/Realtime/config');
        if (!config.enabled) return;

        setStatus('connecting');
        await loadScript(SUPABASE_SCRIPT);
        const client = window.supabase.createClient(config.url, config.anonKey, {
            auth: { persistSession: false, autoRefreshToken: false }
        });
        client.channel(config.channel)
            .on('broadcast', { event: config.event }, message => {
                const topics = (message.payload ?? message)?.topics;
                if (Array.isArray(topics)) notify(topics);
            })
            .subscribe(channelStatus => {
                if (channelStatus === 'SUBSCRIBED') {
                    setStatus('live');
                    // Changes made while the connection was down were never announced.
                    if (missedUpdates) {
                        missedUpdates = false;
                        listeners.forEach(listener => listener.refresh([]));
                    }
                } else {
                    missedUpdates = true;
                    setStatus(channelStatus === 'CLOSED' ? 'offline' : 'connecting');
                }
            });
    }

    function start() {
        connecting ??= connect().catch(error => {
            console.warn('Live updates are off:', error.message);
            if (status) setStatus('offline');
        });
        return connecting;
    }

    return { on };
})();
