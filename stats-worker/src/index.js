// DBD Reshade community ranking: a tiny Cloudflare Worker that counts preset installs, anonymously.
//
//   POST /v1/install   {"preset": "Koda - Coldwind Farm (Koda).ini", "client": "<random uuid>"}
//                      -> 204 counted / already counted, 400 bad request, 429 too many requests
//   GET  /v1/top       -> {"updated": "...", "presets": {"<file name>": <installs>, ...}}
//
// The code is public on purpose. It holds no secrets: the only one, SALT, is stored in Cloudflare
// (`npx wrangler secret put SALT`). Nothing the app sends is trusted:
//   - only presets that exist in the repository's media/presets.json are counted;
//   - each app installation counts once per preset (its random id is stored only as a salted hash);
//   - each network can make at most DAILY_LIMIT requests a day (addresses are stored only as a hash
//     salted with SALT and the date, so they can't be recovered or linked across days).

const PRESETS_URL = "https://raw.githubusercontent.com/Joolace/dbd-reshade/wpf-app/media/presets.json";
const DAILY_LIMIT = 60;
const TOP_CACHE_SECONDS = 600;
const CLIENT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default {
    async fetch(request, env, ctx) {
        const url = new URL(request.url);
        try {
            if (url.pathname === "/v1/install" && request.method === "POST") return await install(request, env);
            if (url.pathname === "/v1/top" && request.method === "GET") return await top(request, env, ctx);
            return json({ error: "not found" }, 404);
        } catch (err) {
            console.error(err);
            return json({ error: "server error" }, 500);
        }
    },

    // Daily clean-up (see [triggers] in wrangler.toml): rate-limit rows are only needed for today.
    async scheduled(event, env) {
        await env.DB.prepare("DELETE FROM daily WHERE day < ?").bind(today()).run();
    },
};

async function install(request, env) {
    if (!env.SALT) return json({ error: "server not configured" }, 500);

    // Rate limit per network and day, before doing anything else.
    const ip = request.headers.get("CF-Connecting-IP") || "unknown";
    const ipHash = await sha256(`${env.SALT}|${today()}|${ip}`);
    const row = await env.DB.prepare(
        "INSERT INTO daily (day, ip, requests) VALUES (?, ?, 1) " +
        "ON CONFLICT (day, ip) DO UPDATE SET requests = requests + 1 RETURNING requests"
    ).bind(today(), ipHash).first();
    if (row && row.requests > DAILY_LIMIT) return json({ error: "too many requests" }, 429);

    // Validate the body.
    let body;
    try {
        body = await request.json();
    } catch {
        return json({ error: "invalid json" }, 400);
    }
    const preset = typeof body?.preset === "string" ? body.preset.trim() : "";
    const client = typeof body?.client === "string" ? body.client.trim() : "";
    if (!preset || preset.length > 200 || !preset.toLowerCase().endsWith(".ini") || !CLIENT_ID.test(client))
        return json({ error: "invalid request" }, 400);

    const known = await knownPresets();
    if (!known.has(preset)) return json({ error: "unknown preset" }, 400);

    // Once per app installation and preset.
    const clientHash = await sha256(`${env.SALT}|client|${client}`);
    const seen = await env.DB.prepare("INSERT OR IGNORE INTO seen (preset, client) VALUES (?, ?)")
        .bind(preset, clientHash).run();
    if (seen.meta.changes > 0) {
        await env.DB.prepare(
            "INSERT INTO counts (preset, installs) VALUES (?, 1) " +
            "ON CONFLICT (preset) DO UPDATE SET installs = installs + 1"
        ).bind(preset).run();
    }
    return new Response(null, { status: 204 });
}

async function top(request, env, ctx) {
    // Served from Cloudflare's cache for a few minutes: the database is read rarely.
    const cache = caches.default;
    const key = new Request(new URL("/v1/top", request.url).toString(), { method: "GET" });
    const cached = await cache.match(key);
    if (cached) return cached;

    const { results } = await env.DB.prepare(
        "SELECT preset, installs FROM counts WHERE installs > 0 ORDER BY installs DESC LIMIT 500"
    ).all();
    const presets = Object.fromEntries(results.map(r => [r.preset, r.installs]));

    const response = json({ updated: new Date().toISOString(), presets }, 200, {
        "Cache-Control": `public, max-age=${TOP_CACHE_SECONDS}`,
    });
    ctx.waitUntil(cache.put(key, response.clone()));
    return response;
}

/** File names in the repository's presets.json (fetched at most once an hour thanks to Cloudflare's cache). */
async function knownPresets() {
    const res = await fetch(PRESETS_URL, { cf: { cacheTtl: 3600, cacheEverything: true } });
    if (!res.ok) throw new Error(`presets.json: HTTP ${res.status}`);
    return new Set(Object.keys(await res.json()));
}

function today() {
    return new Date().toISOString().slice(0, 10);
}

async function sha256(text) {
    const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
    return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}

function json(data, status = 200, headers = {}) {
    return new Response(JSON.stringify(data), {
        status,
        headers: { "Content-Type": "application/json; charset=utf-8", ...headers },
    });
}
