#!/usr/bin/env node
// Launcher shim: exec the platform binary downloaded by install.js, passing through
// args, stdio (the MCP transport), and exit code.
"use strict";

const path = require("path");
const fs = require("fs");
const { spawn } = require("child_process");

const exe = path.join(__dirname, "dist",
    process.platform === "win32" ? "SitefinityCommunity.Mcp.exe" : "SitefinityCommunity.Mcp");

if (!fs.existsSync(exe)) {
    console.error("sitefinity-comm-mcp: binary not found — the install step may have failed.");
    console.error("Try reinstalling: npm install -g sitefinity-comm-mcp");
    process.exit(1);
}

const child = spawn(exe, process.argv.slice(2), { stdio: "inherit" });
child.on("exit", (code, signal) => process.exit(signal ? 1 : (code ?? 0)));
child.on("error", (err) => {
    console.error(`sitefinity-comm-mcp: failed to start: ${err.message}`);
    process.exit(1);
});
