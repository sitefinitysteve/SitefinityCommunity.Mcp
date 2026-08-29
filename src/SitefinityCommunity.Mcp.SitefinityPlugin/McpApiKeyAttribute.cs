// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using ServiceStack;
using ServiceStack.Web;
using Telerik.Sitefinity.Configuration;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// ServiceStack request filter that validates the X-MCP-API-Key header
    /// against the API key stored in Sitefinity's McpConfig section.
    /// Throws UnauthorizedAccessException on failure (ServiceStack returns 401).
    /// <para>
    /// Also brute-force throttles per client IP: after
    /// <see cref="MaxFailuresPerWindow"/> failed attempts inside
    /// <see cref="FailureWindowMinutes"/> minutes the IP is frozen for
    /// <see cref="FreezeMinutes"/> minutes and gets HTTP 429 with a Retry-After header.
    /// A correct key always wins — it unfreezes and resets the counter — so a flood of bad
    /// requests from a shared egress IP can never lock out the legitimate MCP server.
    /// </para>
    /// </summary>
    public class McpApiKeyAttribute : ServiceStack.RequestFilterAttribute
    {
        /// <summary>Failed attempts allowed inside one rolling window before an IP is frozen.</summary>
        private const int MaxFailuresPerWindow = 10;

        /// <summary>Length of the rolling failure window, in minutes.</summary>
        private const int FailureWindowMinutes = 5;

        /// <summary>How long a tripped IP stays frozen, in minutes.</summary>
        private const int FreezeMinutes = 15;

        /// <summary>
        /// Hard ceiling on tracked IPs. Bounds memory against a spray from many source addresses;
        /// once exceeded, expired entries are dropped first, then the oldest.
        /// </summary>
        private const int MaxTrackedIps = 10000;

        /// <summary>Per-IP failure state. Static so it survives across requests in the app domain.</summary>
        private static readonly ConcurrentDictionary<string, FailureEntry> Failures =
            new ConcurrentDictionary<string, FailureEntry>(StringComparer.Ordinal);

        /// <summary>Guards pruning so only one thread compacts the dictionary at a time.</summary>
        private static readonly object PruneLock = new object();

        public override void Execute(IRequest req, IResponse res, object requestDto)
        {
            var config = Config.Get<McpConfig>();

            if (!config.Enabled)
            {
                SafeAudit(config, req, GetClientIp(req), "disabled", null);
                throw new UnauthorizedAccessException("MCP endpoints are disabled.");
            }

            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                throw new InvalidOperationException("MCP API key not configured in Sitefinity settings.");
            }

            var apiKey = req.Headers["X-MCP-API-Key"];
            var clientIp = GetClientIp(req);

            // Evaluate the key FIRST, even for a frozen IP: a correct key unfreezes and resets, so a
            // deliberate flood against the MCP server's own address cannot lock the real client out.
            var keyIsValid = !string.IsNullOrWhiteSpace(apiKey)
                && ConstantTimeEquals(apiKey, config.ApiKey);

            if (keyIsValid)
            {
                SafeReset(clientIp);
                SafeAudit(config, req, clientIp, "valid", null);
                return;
            }

            // Wrong or missing key (a missing key is a probe and counts the same). Throttle state is
            // advisory — if anything in it misbehaves we fall through to the plain 401 rather than 500.
            var retryAfterSeconds = 0;
            var frozen = SafeIsFrozen(clientIp, out retryAfterSeconds);

            SafeRecordFailure(clientIp);

            if (frozen)
            {
                SafeAudit(config, req, clientIp, "throttled", null);
                TryAddRetryAfter(res, retryAfterSeconds);

                throw new HttpError((HttpStatusCode)429,
                    "too many failed authentication attempts; retry later");
            }

            SafeAudit(config, req, clientIp, string.IsNullOrWhiteSpace(apiKey) ? "missing-key" : "invalid-key", apiKey);

            throw new UnauthorizedAccessException("Invalid or missing MCP API key.");
        }

        // ── Key comparison ───────────────────────────────────────────

        /// <summary>
        /// Compares two keys in time independent of where they first differ, so response timing does
        /// not leak a prefix. Walks the full length of BOTH inputs and accumulates inequality
        /// (including the length difference) instead of returning early.
        /// <para>
        /// Hand-rolled because .NET Framework 4.8 has no <c>CryptographicOperations.FixedTimeEquals</c>.
        /// </para>
        /// </summary>
        /// <param name="presented">Key supplied on the request.</param>
        /// <param name="expected">Key configured in Sitefinity.</param>
        private static bool ConstantTimeEquals(string presented, string expected)
        {
            if (presented == null || expected == null)
            {
                return false;
            }

            var a = Encoding.UTF8.GetBytes(presented);
            var b = Encoding.UTF8.GetBytes(expected);

            // Seed with the length difference so unequal lengths can never compare equal.
            var diff = (uint)a.Length ^ (uint)b.Length;
            var length = a.Length > b.Length ? a.Length : b.Length;

            for (var i = 0; i < length; i++)
            {
                var x = i < a.Length ? a[i] : (byte)0;
                var y = i < b.Length ? b[i] : (byte)0;
                diff |= (uint)(x ^ y);
            }

            return diff == 0;
        }

        // ── Throttle plumbing (never throws) ─────────────────────────

        /// <summary>
        /// Direct connection address of the caller. <c>X-Forwarded-For</c> and <c>X-Real-IP</c> are
        /// deliberately ignored — they are attacker-controlled, and honouring them would let one
        /// client evade the throttle by rotating a header value.
        /// </summary>
        /// <param name="req">Current request.</param>
        private static string GetClientIp(IRequest req)
        {
            try
            {
                // HttpContext first: ServiceStack's IRequest.RemoteIp consults X-Forwarded-For.
                var context = HttpContext.Current;

                if (context != null && context.Request != null
                    && !string.IsNullOrWhiteSpace(context.Request.UserHostAddress))
                {
                    return context.Request.UserHostAddress;
                }

                if (req != null && !string.IsNullOrWhiteSpace(req.UserHostAddress))
                {
                    return req.UserHostAddress;
                }
            }
            catch (Exception)
            {
                // Fall through to the shared bucket.
            }

            return "unknown";
        }

        /// <summary>
        /// Whether the IP is currently frozen, and for how many more seconds.
        /// Any failure is reported as "not frozen" so a throttle bug cannot deny service.
        /// </summary>
        /// <param name="clientIp">Caller's address.</param>
        /// <param name="retryAfterSeconds">Receives the remaining freeze time in seconds.</param>
        private static bool SafeIsFrozen(string clientIp, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;

            try
            {
                FailureEntry entry;

                if (!Failures.TryGetValue(clientIp, out entry))
                {
                    return false;
                }

                lock (entry)
                {
                    if (entry.FrozenUntilUtc <= DateTime.UtcNow)
                    {
                        return false;
                    }

                    var remaining = (entry.FrozenUntilUtc - DateTime.UtcNow).TotalSeconds;
                    retryAfterSeconds = remaining < 1 ? 1 : (int)Math.Ceiling(remaining);
                    return true;
                }
            }
            catch (Exception)
            {
                retryAfterSeconds = 0;
                return false;
            }
        }

        /// <summary>
        /// Records one failed attempt, rolling the window and freezing the IP once the threshold
        /// is crossed. Swallows everything — throttling must never turn a 401 into a 500.
        /// </summary>
        /// <param name="clientIp">Caller's address.</param>
        private static void SafeRecordFailure(string clientIp)
        {
            try
            {
                var now = DateTime.UtcNow;
                var entry = Failures.GetOrAdd(clientIp, _ => new FailureEntry(now));

                lock (entry)
                {
                    // Roll the window: a quiet period resets the count rather than accumulating
                    // slow-drip failures into a freeze weeks later.
                    if (now - entry.WindowStartUtc > TimeSpan.FromMinutes(FailureWindowMinutes))
                    {
                        entry.WindowStartUtc = now;
                        entry.Count = 0;
                    }

                    entry.Count++;
                    entry.LastSeenUtc = now;

                    if (entry.Count >= MaxFailuresPerWindow)
                    {
                        entry.FrozenUntilUtc = now.AddMinutes(FreezeMinutes);
                        entry.WindowStartUtc = now;
                        entry.Count = 0;
                    }
                }

                if (Failures.Count > MaxTrackedIps)
                {
                    Prune();
                }
            }
            catch (Exception)
            {
                // Degrade to "no throttle".
            }
        }

        /// <summary>
        /// Clears an IP's failure state after a successful authentication.
        /// </summary>
        /// <param name="clientIp">Caller's address.</param>
        private static void SafeReset(string clientIp)
        {
            try
            {
                FailureEntry removed;
                Failures.TryRemove(clientIp, out removed);
            }
            catch (Exception)
            {
                // Nothing to do — a stale entry only costs the caller a later reset.
            }
        }

        /// <summary>
        /// Compacts the dictionary back under <see cref="MaxTrackedIps"/>: expired entries first,
        /// then the least recently seen. Only one thread prunes at a time; others skip.
        /// </summary>
        private static void Prune()
        {
            if (!System.Threading.Monitor.TryEnter(PruneLock))
            {
                return;
            }

            try
            {
                if (Failures.Count <= MaxTrackedIps)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                var stale = TimeSpan.FromMinutes(FailureWindowMinutes + FreezeMinutes);

                foreach (var pair in Failures.ToArray())
                {
                    var entry = pair.Value;
                    bool expired;

                    lock (entry)
                    {
                        expired = entry.FrozenUntilUtc <= now && now - entry.LastSeenUtc > stale;
                    }

                    if (expired)
                    {
                        FailureEntry removed;
                        Failures.TryRemove(pair.Key, out removed);
                    }
                }

                if (Failures.Count <= MaxTrackedIps)
                {
                    return;
                }

                // Still over: drop the least recently seen, never a currently frozen entry.
                var victims = Failures.ToArray()
                    .Where(p => p.Value.FrozenUntilUtc <= now)
                    .OrderBy(p => p.Value.LastSeenUtc)
                    .Take(Failures.Count - MaxTrackedIps)
                    .ToList();

                foreach (var victim in victims)
                {
                    FailureEntry removed;
                    Failures.TryRemove(victim.Key, out removed);
                }
            }
            catch (Exception)
            {
                // Leave the dictionary as-is; it is bounded by the next attempt.
            }
            finally
            {
                System.Threading.Monitor.Exit(PruneLock);
            }
        }

        /// <summary>
        /// Adds the Retry-After header, ignoring hosts that refuse late header writes.
        /// </summary>
        /// <param name="res">Current response.</param>
        /// <param name="seconds">Seconds the caller should wait.</param>
        private static void TryAddRetryAfter(IResponse res, int seconds)
        {
            try
            {
                if (res != null && seconds > 0)
                {
                    res.AddHeader("Retry-After", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch (Exception)
            {
                // Header is advisory; the 429 status still carries the message.
            }
        }

        // ── Request audit (never throws) ─────────────────────────────

        /// <summary>Audit file name, inside Sitefinity's own Logs folder.</summary>
        private const string AuditFileName = "McpAudit.log";

        /// <summary>Roll the audit file once it passes this size, in bytes.</summary>
        private const long AuditMaxBytes = 10 * 1024 * 1024;

        /// <summary>Archives kept alongside the live file (McpAudit.1.log … McpAudit.3.log).</summary>
        private const int AuditArchiveCount = 3;

        /// <summary>Cap on the logged <c>X-Forwarded-For</c> value; the header is caller-supplied.</summary>
        private const int ForwardedForMaxChars = 200;

        /// <summary>Serializes audit writes across concurrent requests.</summary>
        private static readonly object AuditLock = new object();

        /// <summary>
        /// Appends one line describing this request to <c>App_Data\Sitefinity\Logs\McpAudit.log</c>:
        /// <c>{utcIso}Z | ip={directIp} | xff={forwardedFor} | {method} {path} | {redactedQuery} | auth={outcome}</c>.
        /// <para>
        /// Both IP perspectives are recorded, labelled, and never conflated. <c>ip=</c> is the direct
        /// TCP connection address — trustworthy, and what the throttle keys on. <c>xff=</c> is the
        /// <c>X-Forwarded-For</c> header verbatim (<c>-</c> when absent), which behind a proxy or CDN
        /// carries the real client but is spoofable, so it is informational only and never throttled on.
        /// </para>
        /// <para>
        /// Requests only — never results, and never any API key material, not even a prefix. The
        /// query string is redacted with the same per-parameter deny-list plus pattern scan used for
        /// IIS query strings, because an audit trail that leaks a secret is worse than no audit trail.
        /// </para>
        /// <para>
        /// The whole path is best-effort: a locked file, a full disk or an unmapped path degrades to
        /// no auditing. Auditing must never change the outcome of a request.
        /// </para>
        /// </summary>
        /// <param name="config">Current MCP configuration.</param>
        /// <param name="req">Current request.</param>
        /// <param name="clientIp">Direct connection address of the caller.</param>
        /// <param name="outcome">One of valid, invalid-key, missing-key, throttled, disabled.</param>
        /// <param name="presentedKey">
        /// The rejected key, used ONLY to derive the non-reversible fingerprint on an
        /// <c>invalid-key</c> line. Pass <c>null</c> for every other outcome — a VALID key is never
        /// fingerprinted, logged, or hinted at in any form.
        /// </param>
        private static void SafeAudit(McpConfig config, IRequest req, string clientIp, string outcome, string presentedKey)
        {
            try
            {
                if (config == null || !config.AuditRequests)
                {
                    return;
                }

                var path = GetAuditFilePath();

                if (path == null)
                {
                    return;
                }

                var method = "?";
                var pathInfo = "?";
                var query = string.Empty;

                if (req != null)
                {
                    method = string.IsNullOrEmpty(req.Verb) ? "?" : req.Verb;
                    pathInfo = string.IsNullOrEmpty(req.PathInfo) ? "?" : req.PathInfo;
                    query = GetRawQueryString(req);
                }

                var line = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                    + "Z | ip=" + Sanitize(clientIp)
                    + " | xff=" + Sanitize(GetForwardedFor(req))
                    + " | " + Sanitize(method) + " " + Sanitize(pathInfo)
                    + " | " + Sanitize(RedactQueryString(query))
                    + " | auth=" + outcome + DescribeAttemptedKey(outcome, presentedKey);

                lock (AuditLock)
                {
                    RollIfNeeded(path);

                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(line);
                    }
                }
            }
            catch (Exception)
            {
                // Degrade to "no audit". Never affect the request.
            }
        }

        /// <summary>
        /// Full path of the audit file, or <c>null</c> when the Logs folder cannot be resolved or does
        /// not exist. Sitefinity owns that folder, so it is never created here.
        /// </summary>
        private static string GetAuditFilePath()
        {
            try
            {
                // Fully qualified: `using ServiceStack;` also brings a HostingEnvironment into scope.
                var folder = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/Sitefinity/Logs");

                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    return null;
                }

                return Path.Combine(folder, AuditFileName);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Rolls the audit file once it exceeds <see cref="AuditMaxBytes"/>, shifting
        /// McpAudit.2.log to McpAudit.3.log and so on, and dropping the oldest archive.
        /// Caller holds <see cref="AuditLock"/>.
        /// </summary>
        /// <param name="path">Path of the live audit file.</param>
        private static void RollIfNeeded(string path)
        {
            try
            {
                var info = new FileInfo(path);

                if (!info.Exists || info.Length < AuditMaxBytes)
                {
                    return;
                }

                var folder = Path.GetDirectoryName(path);
                var stem = Path.GetFileNameWithoutExtension(AuditFileName);
                var extension = Path.GetExtension(AuditFileName);

                var oldest = Path.Combine(folder, stem + "." + AuditArchiveCount + extension);

                if (File.Exists(oldest))
                {
                    File.Delete(oldest);
                }

                for (var i = AuditArchiveCount - 1; i >= 1; i--)
                {
                    var from = Path.Combine(folder, stem + "." + i + extension);
                    var to = Path.Combine(folder, stem + "." + (i + 1) + extension);

                    if (File.Exists(from))
                    {
                        File.Move(from, to);
                    }
                }

                File.Move(path, Path.Combine(folder, stem + ".1" + extension));
            }
            catch (Exception)
            {
                // Could not roll (file locked, permissions) — keep appending to the current file.
            }
        }

        /// <summary>
        /// Forensic fingerprint of a REJECTED key, appended only to an <c>invalid-key</c> line as
        /// <c>attempted: len={n} prefix={6 chars} sha256={12 hex}</c>.
        /// <para>
        /// The raw key is deliberately never written. The invalid keys that actually show up in
        /// practice are nearly-valid — a prod key hitting dev, a stale key after a rotation, a
        /// one-character typo — so logging them verbatim would turn the audit file into a store of
        /// live credentials. Length plus a short prefix plus a truncated SHA-256 is enough to
        /// recognise a key you already hold (hash your known keys and compare) without the log ever
        /// carrying one.
        /// </para>
        /// <para>
        /// A VALID key is never fingerprinted — no prefix, no hash, nothing.
        /// </para>
        /// </summary>
        /// <param name="outcome">Auth outcome for this request.</param>
        /// <param name="presentedKey">The rejected key, or null.</param>
        private static string DescribeAttemptedKey(string outcome, string presentedKey)
        {
            try
            {
                if (!string.Equals(outcome, "invalid-key", StringComparison.Ordinal)
                    || string.IsNullOrEmpty(presentedKey))
                {
                    return string.Empty;
                }

                var prefixLength = presentedKey.Length < 6 ? presentedKey.Length : 6;
                var prefix = presentedKey.Substring(0, prefixLength);

                string hash;

                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(presentedKey));
                    var hex = new StringBuilder(12);

                    for (var i = 0; i < 6; i++)
                    {
                        hex.Append(bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    }

                    hash = hex.ToString();
                }

                return " attempted: len=" + presentedKey.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " prefix=" + Sanitize(prefix)
                    + " sha256=" + hash;
            }
            catch (Exception)
            {
                // Fingerprinting is a nicety; never let it cost the audit line.
                return string.Empty;
            }
        }

        /// <summary>
        /// The <c>X-Forwarded-For</c> header verbatim, or <c>-</c> when absent. Recorded for
        /// attribution only — behind a proxy or CDN it names the real client, but it is caller-supplied
        /// and therefore forgeable, so it is never used for throttling. Truncated to
        /// <see cref="ForwardedForMaxChars"/> characters and pattern-scanned like any other header value.
        /// </summary>
        /// <param name="req">Current request.</param>
        private static string GetForwardedFor(IRequest req)
        {
            try
            {
                string value = null;

                var context = HttpContext.Current;

                if (context != null && context.Request != null)
                {
                    value = context.Request.Headers["X-Forwarded-For"];
                }

                if (string.IsNullOrWhiteSpace(value) && req != null && req.Headers != null)
                {
                    value = req.Headers["X-Forwarded-For"];
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    return "-";
                }

                value = value.Trim();

                if (value.Length > ForwardedForMaxChars)
                {
                    value = value.Substring(0, ForwardedForMaxChars) + "...(truncated)";
                }

                return McpSecretRedactor.Redact(value);
            }
            catch (Exception)
            {
                return "-";
            }
        }

        /// <summary>
        /// Raw query string for the request, preferring the ASP.NET context so the value matches what
        /// actually arrived on the wire.
        /// </summary>
        /// <param name="req">Current request.</param>
        private static string GetRawQueryString(IRequest req)
        {
            try
            {
                var context = HttpContext.Current;

                if (context != null && context.Request != null && context.Request.Url != null)
                {
                    var raw = context.Request.Url.Query;
                    return raw.StartsWith("?", StringComparison.Ordinal) ? raw.Substring(1) : raw;
                }

                if (req != null && req.QueryString != null)
                {
                    var pairs = new List<string>();

                    foreach (var key in req.QueryString.AllKeys)
                    {
                        if (key != null)
                        {
                            pairs.Add(key + "=" + req.QueryString[key]);
                        }
                    }

                    return string.Join("&", pairs.ToArray());
                }
            }
            catch (Exception)
            {
                // Fall through — an audit line without a query is still useful.
            }

            return string.Empty;
        }

        /// <summary>
        /// Redacts a raw query string: each <c>name=value</c> pair whose name hits the redactor's
        /// deny-list loses its value outright, then the remainder is pattern-scanned so a token under
        /// an innocuous parameter name is still caught. Mirrors the IIS query handling in
        /// <c>McpSystemLogService</c>.
        /// </summary>
        /// <param name="query">Raw query string, without the leading question mark.</param>
        private static string RedactQueryString(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return string.Empty;
            }

            var pairs = query.Split('&');
            var rebuilt = new List<string>(pairs.Length);

            foreach (var pair in pairs)
            {
                var eq = pair.IndexOf('=');

                if (eq <= 0)
                {
                    rebuilt.Add(pair);
                    continue;
                }

                var name = pair.Substring(0, eq);

                rebuilt.Add(McpSecretRedactor.IsDeniedKey(name)
                    ? name + "=" + McpSecretRedactor.Placeholder
                    : pair);
            }

            return McpSecretRedactor.Redact(string.Join("&", rebuilt.ToArray()));
        }

        /// <summary>
        /// Flattens a value onto one audit line: newlines and pipes would otherwise let a crafted URL
        /// forge extra log entries.
        /// </summary>
        /// <param name="value">Value to place in a pipe-delimited field.</param>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("|", "/");
        }

        /// <summary>
        /// Rolling failure state for one client IP. All access is under <c>lock (entry)</c>.
        /// </summary>
        private sealed class FailureEntry
        {
            /// <summary>Creates an entry whose window opens now.</summary>
            /// <param name="nowUtc">Current UTC time.</param>
            public FailureEntry(DateTime nowUtc)
            {
                this.WindowStartUtc = nowUtc;
                this.LastSeenUtc = nowUtc;
                this.FrozenUntilUtc = DateTime.MinValue;
                this.Count = 0;
            }

            /// <summary>Failures recorded in the current window.</summary>
            public int Count { get; set; }

            /// <summary>When the current rolling window opened.</summary>
            public DateTime WindowStartUtc { get; set; }

            /// <summary>Most recent attempt, used to pick pruning victims.</summary>
            public DateTime LastSeenUtc { get; set; }

            /// <summary>Freeze expiry; <c>DateTime.MinValue</c> when not frozen.</summary>
            public DateTime FrozenUntilUtc { get; set; }
        }
    }
}
