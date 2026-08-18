// Downloads the self-contained sitefinity-mcp binary for this platform from the matching
// GitHub release. The npm package itself is just this downloader plus a launcher shim, so
// `npm install -g sitefinity-mcp` works with no .NET installed — the same pattern esbuild
// and Biome use, minus the per-platform sub-packages.
"use strict";

const fs = require("fs");
const path = require("path");
const https = require("https");
const { execFileSync } = require("child_process");

const { version } = require("./package.json");

const RID_MAP = {
    "win32-x64": "win-x64",
    "linux-x64": "linux-x64",
    "darwin-x64": "osx-x64",
    "darwin-arm64": "osx-arm64",
};

const key = `${process.platform}-${process.arch}`;
const rid = RID_MAP[key];

if (!rid) {
    console.error(`sitefinity-comm-mcp: unsupported platform '${key}'.`);
    console.error("Supported: win32-x64, linux-x64, darwin-x64, darwin-arm64.");
    console.error("You can run from source instead: https://github.com/sitefinitysteve/SitefinityCommunity.Mcp");
    process.exit(1);
}

const url = `https://github.com/sitefinitysteve/SitefinityCommunity.Mcp/releases/download/v${version}/sitefinity-comm-mcp-${rid}.tar.gz`;
const destDir = path.join(__dirname, "dist");
const archive = path.join(__dirname, `download-${rid}.tar.gz`);

function download(u, dest, redirects, done) {
    if (redirects > 5) {
        return done(new Error("too many redirects"));
    }

    https.get(u, { headers: { "user-agent": "sitefinity-comm-mcp-installer" } }, (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
            res.resume();
            return download(res.headers.location, dest, redirects + 1, done);
        }
        if (res.statusCode !== 200) {
            res.resume();
            return done(new Error(`HTTP ${res.statusCode} for ${u}`));
        }
        const out = fs.createWriteStream(dest);
        res.pipe(out);
        out.on("finish", () => out.close(done));
        out.on("error", done);
    }).on("error", done);
}

console.log(`sitefinity-comm-mcp: downloading v${version} for ${rid}...`);

download(url, archive, 0, (err) => {
    if (err) {
        console.error(`sitefinity-comm-mcp: download failed: ${err.message}`);
        console.error(`  ${url}`);
        console.error("Check https://github.com/sitefinitysteve/SitefinityCommunity.Mcp/releases for this version.");
        process.exit(1);
    }

    try {
        fs.mkdirSync(destDir, { recursive: true });
        // tar ships with Windows 10+, macOS, and every mainstream Linux.
        execFileSync("tar", ["-xzf", archive, "-C", destDir], { stdio: "inherit" });
        fs.unlinkSync(archive);

        const exe = path.join(destDir, process.platform === "win32" ? "SitefinityCommunity.Mcp.exe" : "SitefinityCommunity.Mcp");
        if (!fs.existsSync(exe)) {
            throw new Error(`extracted archive did not contain ${path.basename(exe)}`);
        }
        if (process.platform !== "win32") {
            fs.chmodSync(exe, 0o755);
        }

        console.log("sitefinity-comm-mcp: installed.");
    } catch (e) {
        console.error(`sitefinity-comm-mcp: extract failed: ${e.message}`);
        process.exit(1);
    }
});
