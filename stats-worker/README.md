# DBD Reshade community service (Cloudflare Worker)

A tiny service with two jobs:

- it counts how many times each preset is installed, so the app can show the **🔥 Most used** filter;
- it turns **Report a problem** in the app into a GitHub issue, so users don't need a GitHub account.

 It runs for free on Cloudflare Workers
(the free plan allows 100,000 requests a day) with a D1 database.

## What it stores (privacy)

- the **preset file name** and how many times it was installed;
- a **salted hash** of the app's random installation id, only to count each installation once per preset;
- a **salted hash of the network address, per day**, only to limit requests (deleted every day).

No names, accounts, e-mail or IP addresses are stored. Users can turn it off in the app's Settings.

## Security

The code is public; that's fine because:

- it contains **no secrets**: the only one, `SALT`, lives in Cloudflare (`npx wrangler secret put SALT`);
- it **trusts nothing** it receives: only presets listed in `media/presets.json` count, each app
  installation counts once per preset, and each network can make at most 60 requests a day;
- it has **no admin endpoints**: to reset or edit the counts, use the D1 console in the Cloudflare dashboard.

## Bug reports: the GitHub token

The worker needs a token that can create issues, and nothing else:

1. GitHub → your picture → **Settings** → **Developer settings** → **Personal access tokens** →
   **Fine-grained tokens** → **Generate new token**.
2. **Name**: `dbdreshade-reports`. **Expiration**: up to 1 year (put a reminder to renew it).
3. **Repository access**: **Only select repositories** → `Joolace/dbd-reshade`.
4. **Permissions** → **Repository permissions** → **Issues**: **Read and write**. Leave everything else as it is.
5. **Generate token** and copy it (it's shown only once).
6. Cloudflare → Workers & Pages → `dbdreshade-stats` → **Settings** → **Variables and Secrets** → **Add**:
   type **Secret**, name `GITHUB_TOKEN`, value the token → **Deploy**.

Reports arrive as issues titled `[Report] …` with the label `bug-report`. At most 3 reports a day can come
from the same network. The text users write can't mention people (`@`) or link other issues (`#12`).
If the token expires, the app shows "Couldn't send the report" and offers GitHub and Discord instead.

## Setup (once, about 10 minutes)

You need a free [Cloudflare account](https://dash.cloudflare.com/sign-up) and
[Node.js LTS](https://nodejs.org/). Then, in a terminal, from this `stats-worker` folder:

1. **Log in** (opens the browser):
   ```
   npx wrangler login
   ```
2. **Create the database**, then copy the `database_id` it prints into `wrangler.toml`:
   ```
   npx wrangler d1 create dbdreshade-stats
   ```
3. **Create the tables**:
   ```
   npx wrangler d1 execute dbdreshade-stats --remote --file=schema.sql
   ```
4. **Set the secret salt**: paste any long random text when asked (for example two GUIDs:
   in PowerShell run `[guid]::NewGuid().ToString() + [guid]::NewGuid().ToString()`). Keep it secret
   and never change it, or every installation would be counted again.
   ```
   npx wrangler secret put SALT
   ```
5. **Publish**:
   ```
   npx wrangler deploy
   ```
   It prints the address, like `https://dbdreshade-stats.<your-name>.workers.dev`.
6. **Check it works**: open `https://dbdreshade-stats.<your-name>.workers.dev/v1/top` in the
   browser; it shows `{"updated": "...", "presets": {}}`.
7. **Tell the app**: put the address in `app/src/DBDReshade/AppInfo.cs`:
   ```csharp
   public const string StatsUrl = "https://dbdreshade-stats.<your-name>.workers.dev";
   ```
   and release a new version. While `StatsUrl` is empty the app simply doesn't use the ranking.

## Later

- **Update the code**: edit `src/index.js`, then `npx wrangler deploy`.
- **See or fix the counts**: Cloudflare dashboard → Storage & Databases → D1 → `dbdreshade-stats` → Console,
  e.g. `SELECT * FROM counts ORDER BY installs DESC;` or `DELETE FROM counts WHERE preset = '...';`
- **Logs**: Cloudflare dashboard → Workers → `dbdreshade-stats` → Logs.

## API

| Request | Answer |
|---|---|
| `POST /v1/install` with `{"preset": "<file name>.ini", "client": "<uuid>"}` | `204` counted (or already counted), `400` invalid, `429` too many requests |
| `POST /v1/report` with `{"description", "contact", "system", "log"}` | `201 {"url", "number"}` issue created, `400` invalid, `429` too many reports, `502` GitHub refused it |
| `GET /v1/top` | `{"updated": "<time>", "presets": {"<file name>": <installs>}}` |
