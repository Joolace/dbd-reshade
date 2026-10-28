// DBD Reshade community ranking: a tiny Cloudflare Worker that counts preset installs, anonymously.
//
//   POST /v1/install   {"preset": "Koda - Coldwind Farm (Koda).ini", "client": "<random uuid>"}
//                      -> 204 counted / already counted, 400 bad request, 429 too many requests
//   GET  /v1/top       -> {"updated": "...", "presets": {"<file name>": <installs>, ...}}
//   POST /v1/report    {"description": "...", "contact": "...", "system": "...", "log": "..."}
//                      -> 201 {"url": "<GitHub issue>", "number": 12}, 400 bad request, 429 too many reports
//
// The code is public on purpose. It holds no secrets: SALT and GITHUB_TOKEN are stored in Cloudflare
// (Settings > Variables and Secrets). GITHUB_TOKEN is a fine-grained token that can only create issues in
// the dbd-reshade repository. Nothing the app sends is trusted:
//   - only presets that exist in the repository's media/presets.json are counted;
//   - each app installation counts once per preset (its random id is stored only as a salted hash);
//   - each network can make at most DAILY_LIMIT requests a day (addresses are stored only as a hash
//     salted with SALT and the date, so they can't be recovered or linked across days).

const PRESETS_URL = "https://raw.githubusercontent.com/Joolace/dbd-reshade/wpf-app/media/presets.json";
const DAILY_LIMIT = 60;
const REPO = "Joolace/dbd-reshade";
const REPORTS_PER_DAY = 3;          // bug reports per network per day
const REPORT_LABEL = "bug-report";
const CLIENT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export default {
    async fetch(request, env, ctx) {
        const url = new URL(request.url);
        try {
            if (url.pathname === "/v1/install" && request.method === "POST") return await install(request, env);
            if (url.pathname === "/v1/top" && request.method === "GET") return await top(request, env);
            if (url.pathname === "/v1/report" && request.method === "POST") return await report(request, env);
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

async function top(request, env) {
    // Read straight from the database: it's one cheap query, and the app itself only asks every few hours.
    // (No edge cache: it kept serving an old, empty list right after the first installs.)
    const { results } = await env.DB.prepare(
        "SELECT preset, installs FROM counts WHERE installs > 0 ORDER BY installs DESC LIMIT 500"
    ).all();
    const presets = Object.fromEntries(results.map(r => [r.preset, r.installs]));
    return json({ updated: new Date().toISOString(), presets }, 200, { "Cache-Control": "no-store" });
}

// ---- Bug reports -------------------------------------------------------------------------------

async function report(request, env) {
    if (!env.SALT || !env.GITHUB_TOKEN) return json({ error: "server not configured" }, 500);

    // At most a few reports a day from the same network: nobody can flood the repository with issues.
    const ip = request.headers.get("CF-Connecting-IP") || "unknown";
    const ipHash = await sha256(`${env.SALT}|${today()}|report|${ip}`);
    const row = await env.DB.prepare(
        "INSERT INTO daily (day, ip, requests) VALUES (?, ?, 1) " +
        "ON CONFLICT (day, ip) DO UPDATE SET requests = requests + 1 RETURNING requests"
    ).bind(today(), ipHash).first();
    if (row && row.requests > REPORTS_PER_DAY) return json({ error: "too many reports" }, 429);

    let body;
    try { body = await request.json(); } catch { return json({ error: "invalid json" }, 400); }
    const text = (v, max) => (typeof v === "string" ? v.trim() : "").slice(0, max);
    const description = text(body?.description, 4000);
    const contact = text(body?.contact, 100);
    const system = text(body?.system, 2000);
    let log = text(body?.log, 200000);
    if (description.length < 10) return json({ error: "description too short" }, 400);

    // GitHub issues hold up to 65,536 characters: keep the end of the log, where the error usually is.
    const LOG_MAX = 45000;
    if (log.length > LOG_MAX) log = "[…]\n" + log.slice(-LOG_MAX);

    const summary = oneLine(description);
    const title = "[Report] " + quiet(summary.length > 80 ? summary.slice(0, 80) + "…" : summary);
    const issueBody = [
        "### What happened",
        quiet(description),
        "",
        contact ? `**Contact:** ${quiet(contact)}\n` : "",
        "### System",
        "```",
        fence(system) || "(not sent)",
        "```",
        "",
        log ? "<details><summary>Log</summary>\n\n```\n" + fence(log) + "\n```\n</details>" : "_No log attached._",
        "",
        "<sub>Sent from the DBD Reshade app.</sub>",
    ].join("\n");

    let issue = await createIssue(env, title, issueBody, [REPORT_LABEL]);
    // The label may not be allowed or may not exist: try once more without it.
    if (!issue.ok && (issue.status === 403 || issue.status === 422)) issue = await createIssue(env, title, issueBody, []);
    if (!issue.ok) {
        console.error("GitHub", issue.status, await issue.text());
        return json({ error: "github error" }, 502);
    }
    const created = await issue.json();
    return json({ url: created.html_url, number: created.number }, 201);
}

function createIssue(env, title, body, labels) {
    return fetch(`https://api.github.com/repos/${REPO}/issues`, {
        method: "POST",
        headers: {
            "Authorization": `Bearer ${env.GITHUB_TOKEN}`,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "dbdreshade-stats-worker",
            "Content-Type": "application/json",
        },
        body: JSON.stringify(labels.length ? { title, body, labels } : { title, body }),
    });
}

/** Text from users: no @mentions (they would notify people) and no #123 links to other issues. */
function quiet(s) {
    return s.replace(/@/g, "@​").replace(/#(\d)/g, "#​$1");
}

/** Text inside a ``` block must not be able to close it. */
function fence(s) {
    return s.replace(/```/g, "'''");
}

function oneLine(s) {
    return s.replace(/\s+/g, " ").trim();
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
