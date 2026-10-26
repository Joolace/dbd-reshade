-- DBD Reshade community ranking: anonymous install counts per preset.
-- Apply with:  npx wrangler d1 execute dbdreshade-stats --remote --file=schema.sql

-- Total installs per preset (what the app shows).
CREATE TABLE IF NOT EXISTS counts (
    preset TEXT PRIMARY KEY,
    installs INTEGER NOT NULL DEFAULT 0
);

-- One row per (preset, app installation) already counted, so installing the same preset again
-- doesn't count twice. "client" is a salted hash of the app's random id: it can't be traced back.
CREATE TABLE IF NOT EXISTS seen (
    preset TEXT NOT NULL,
    client TEXT NOT NULL,
    PRIMARY KEY (preset, client)
);

-- Requests per network per day, to slow down anyone trying to inflate the numbers.
-- "ip" is a hash of the address with a secret salt that changes every day: no address is stored.
CREATE TABLE IF NOT EXISTS daily (
    day TEXT NOT NULL,
    ip TEXT NOT NULL,
    requests INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (day, ip)
);
