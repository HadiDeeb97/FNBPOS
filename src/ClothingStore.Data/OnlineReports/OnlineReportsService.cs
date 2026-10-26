using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClothingStore.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Data.OnlineReports;

/// <summary>Where the shop's website is and how to reach it. Shared by every till (kept in the database).</summary>
public sealed record OnlineReportsConfig
{
    public bool Enabled { get; init; } = true;

    /// <summary>The website, e.g. https://pos-reports.rami.workers.dev</summary>
    public string SiteUrl { get; init; } = "";

    /// <summary>The secret the website checks on every upload.</summary>
    public string UploadKey { get; init; } = "";

    /// <summary>Goes up by one for "Sign out all devices"; the website ends every session when it sees a new value.</summary>
    public int SignOutAll { get; init; }

    /// <summary>Filled when the POS set the website up on Cloudflare itself (so it can update it).</summary>
    public CloudflareSite? Cloudflare { get; init; }

    /// <summary>The sales the shop aims for each month, shown on the website with how the month is going; null for none.</summary>
    public decimal? MonthlyTarget { get; init; }

    /// <summary>
    /// Google Search Console's code (the HTML tag method): shown on the website so the owner can prove it is theirs and
    /// ask Google to review a wrong "dangerous site" warning.
    /// </summary>
    public string? GoogleVerification { get; init; }
}

public sealed record CloudflareSite(string AccountId, string ScriptName, string DatabaseId, string Subdomain, string? WorkerBuild);

/// <summary>How uploading is going, for the Settings screen on any till.</summary>
public sealed record OnlineReportsState
{
    public DateTime? LastUploadUtc { get; init; }

    /// <summary>Uploads and documents sent on <see cref="CountDay"/> (a UTC day, as Cloudflare counts).</summary>
    public string? CountDay { get; init; }
    public int UploadsToday { get; init; }
    public int DocsToday { get; init; }

    /// <summary>Cloudflare said the free daily limit was reached: nothing is sent until then (midnight UTC).</summary>
    public DateTime? PausedUntilUtc { get; init; }

    /// <summary>Why the last try failed (an <see cref="OnlineReportsError"/> name), or null when it worked.</summary>
    public string? Error { get; init; }
    public string? ErrorDetail { get; init; }

    /// <summary>When it started failing.</summary>
    public DateTime? FailingSinceUtc { get; init; }

    /// <summary>The till that sent last.</summary>
    public string? Till { get; init; }

    /// <summary>When the slower parts (other periods, stock) were last sent.</summary>
    public DateTime? FullUploadUtc { get; init; }

    /// <summary>When the item list was last looked at.</summary>
    public DateTime? CatalogUtc { get; init; }

    /// <summary>Finished days and months already on the website.</summary>
    public string? DaysThrough { get; init; }
    public string? MonthsThrough { get; init; }
    public int SignOutAllSent { get; init; }

    /// <summary>A fingerprint of each document as last sent, so unchanged ones aren't sent again.</summary>
    public Dictionary<string, string> Sent { get; init; } = [];
}

public enum OnlineReportsError
{
    /// <summary>No internet, or the website couldn't be reached.</summary>
    Offline,

    /// <summary>The website refused the upload key.</summary>
    Rejected,

    /// <summary>Cloudflare's free daily limit was reached (or this POS's own daily cap): sending resumes at midnight UTC.</summary>
    Limit,

    /// <summary>The website is older than this POS and needs updating.</summary>
    Outdated,

    /// <summary>The website answered with an error.</summary>
    Server,
}

public enum SyncOutcome
{
    Sent,
    /// <summary>Online reports are off or not set up.</summary>
    Off,
    /// <summary>Another till is sending.</summary>
    OtherTill,
    Failed,
    /// <summary>Waiting for Cloudflare's next day: a daily limit was reached.</summary>
    Paused,
}

public sealed record SyncResult(SyncOutcome Outcome, int Documents = 0, OnlineReportsError? Error = null, string? Detail = null);

/// <summary>
/// Sends the shop's reports to its website. Every till runs <see cref="SyncAsync"/> every few minutes; one of them at a
/// time (a lease in the database) actually sends. Each run sends what is happening now, plus the slower parts every half
/// hour and any finished days and months not sent yet. Documents that haven't changed aren't sent again, but every run
/// tells the website the shop is online.
/// </summary>
public class OnlineReportsService(IDbContextFactory<PosDbContext> factory, OnlineReportsBuilder builder, HttpClient http)
{
    public const string ConfigKey = "onlineReports.config";
    public const string StateKey = "onlineReports.state";
    public const string LeaseKey = "onlineReports.lease";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FullInterval = TimeSpan.FromMinutes(30);

    /// <summary>How often the item list is looked at (and sent when stock or items changed).</summary>
    public static readonly TimeSpan CatalogInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LeaseTime = TimeSpan.FromMinutes(12);

    /// <summary>The website's upload format (cloud/reports-worker, VERSION).</summary>
    public const int FormatVersion = 1;

    /// <summary>D1's free plan allows 50 queries per request; the website takes up to 30 documents per upload.</summary>
    public const int DocsPerUpload = 25;

    /// <summary>History sent on the first upload.</summary>
    public const int HistoryDays = 90;
    public const int HistoryMonths = 12;

    /// <summary>Past days kept on the website (13 months).</summary>
    public const int KeepDays = 400;

    /// <summary>Finished days and months built per run, so catching up never keeps a till busy for long.</summary>
    private const int HistoryPerRun = 25;

    // This POS's own daily caps, far below Cloudflare's free plan (100,000 requests and 100,000 rows written a day): a
    // normal day is about 300 uploads and a few hundred documents. A bug or a stuck loop can never use up the free plan.
    public const int MaxUploadsPerDay = 400;
    public const int MaxDocsPerDay = 5000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>Raised after each run (on a background thread), so an open Settings screen can refresh.</summary>
    public event EventHandler<SyncResult>? Synced;

    // ---- Settings ------------------------------------------------------------------------------------------------

    public async Task<OnlineReportsConfig?> GetConfigAsync(CancellationToken ct = default) =>
        Parse<OnlineReportsConfig>(await ReadAsync(ConfigKey, ct));

    public Task SaveConfigAsync(OnlineReportsConfig config, CancellationToken ct = default) =>
        WriteAsync(ConfigKey, JsonSerializer.Serialize(config, Json), ct);

    /// <summary>Forgets the website (it stays on Cloudflare until deleted there) and what was sent to it.</summary>
    public async Task RemoveConfigAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.AppState.Where(s => s.Key == ConfigKey || s.Key == StateKey || s.Key == LeaseKey).ExecuteDeleteAsync(ct);
    }

    public async Task<OnlineReportsState> GetStateAsync(CancellationToken ct = default) =>
        Parse<OnlineReportsState>(await ReadAsync(StateKey, ct)) ?? new OnlineReportsState();

    /// <summary>Ends every session on the website on the next upload (a lost phone, someone who left).</summary>
    public async Task SignOutAllDevicesAsync(CancellationToken ct = default)
    {
        var config = await GetConfigAsync(ct) ?? throw new InvalidOperationException("Online reports aren't set up.");
        await SaveConfigAsync(config with { SignOutAll = config.SignOutAll + 1 }, ct);
    }

    /// <summary>A new random upload key (256 bits).</summary>
    public static string NewUploadKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>"https://x.workers.dev/" or "x.workers.dev" → "https://x.workers.dev"; null if it isn't a web address.</summary>
    public static string? NormalizeSiteUrl(string? text)
    {
        text = text?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(text)) return null;
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || string.IsNullOrEmpty(uri.Host)) return null;
        // Plain http only for a site on this PC (testing with wrangler dev).
        if (uri.Scheme == "http" && !uri.IsLoopback) return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>Whether the website answers and takes this POS's uploads (its format version).</summary>
    public async Task<(bool Ok, int? Version)> CheckSiteAsync(string siteUrl, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync($"{siteUrl}/api/health", ct);
            if (!response.IsSuccessStatusCode) return (false, null);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var version = doc.RootElement.TryGetProperty("version", out var v) && v.TryGetInt32(out var n) ? n : (int?)null;
            return (version == FormatVersion, version);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, null);
        }
    }

    // ---- Sending -------------------------------------------------------------------------------------------------

    /// <summary>
    /// One run. <paramref name="force"/> ("Upload now") sends everything again, even unchanged, and takes over from
    /// another till.
    /// </summary>
    public async Task<SyncResult> SyncAsync(string tillId, string? tillName, bool force = false, CancellationToken ct = default)
    {
        if (!await _running.WaitAsync(force ? TimeSpan.FromMinutes(2) : TimeSpan.Zero, ct))
            return new SyncResult(SyncOutcome.OtherTill); // this till is already sending
        try
        {
            var result = await RunAsync(tillId, tillName, force, ct);
            Synced?.Invoke(this, result);
            return result;
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<SyncResult> RunAsync(string tillId, string? tillName, bool force, CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        if (config is not { Enabled: true } || NormalizeSiteUrl(config.SiteUrl) is not { } site || config.UploadKey.Length == 0)
            return new SyncResult(SyncOutcome.Off);
        if (!await TakeLeaseAsync(tillId, force, ct)) return new SyncResult(SyncOutcome.OtherTill);

        var state = await GetStateAsync(ct);
        var utcDay = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (state.CountDay != utcDay) state = state with { CountDay = utcDay, UploadsToday = 0, DocsToday = 0 };
        if (state.PausedUntilUtc is { } paused && paused > DateTime.UtcNow) return new SyncResult(SyncOutcome.Paused, Error: OnlineReportsError.Limit);
        if (state.UploadsToday >= MaxUploadsPerDay)
        {
            await SaveStateAsync(state with { PausedUntilUtc = NextUtcMidnight(), Error = nameof(OnlineReportsError.Limit), ErrorDetail = "daily cap" }, ct);
            return new SyncResult(SyncOutcome.Paused, Error: OnlineReportsError.Limit);
        }
        var sent = new Dictionary<string, string>(state.Sent);
        var now = DateTime.Now;
        var today = now.Date;
        var full = force || state.FullUploadUtc is not { } lastFull || DateTime.UtcNow - lastFull >= FullInterval;

        // What changed since the last upload.
        var docs = new List<(string Key, string Json)>();
        void Put(string key, string json)
        {
            docs.RemoveAll(d => d.Key == key);
            docs.Add((key, json));
        }
        void Add(string key, object doc)
        {
            var json = JsonSerializer.Serialize(doc, doc.GetType(), Json);
            if (force || !sent.TryGetValue(key, out var hash) || hash != Hash(json)) Put(key, json);
        }

        Add("store", await builder.StoreAsync(ct) with
        {
            MonthlyTarget = config.MonthlyTarget is > 0 ? config.MonthlyTarget : null,
            GoogleVerification = NormalizeGoogleCode(config.GoogleVerification),
        });
        Add("live", await builder.LiveAsync(ct));
        Add("period:today", await builder.PeriodAsync("today", today, ct));
        if (full)
        {
            foreach (var period in OnlineReportsBuilder.Periods.Skip(1))
                Add($"period:{period}", await builder.PeriodAsync(period, today, ct));
            Add("stock", await builder.StockAsync(today, ct));
            // Yesterday again: a sale voided this morning still changes it.
            Add($"day:{Day(today.AddDays(-1))}", await builder.ReportAsync(today.AddDays(-1), today, ct));
        }
        var catalog = force || state.CatalogUtc is not { } lastCatalog || DateTime.UtcNow - lastCatalog >= CatalogInterval;
        if (catalog)
            foreach (var part in await builder.CatalogAsync(today, ct))
                Add($"catalog:{part.Part}", part);

        // Finished days and months not on the website yet (the first time: the last 90 days and 12 months).
        var budget = HistoryPerRun;
        var lastDay = today.AddDays(-1);
        var nextDay = ParseDay(state.DaysThrough) is { } d && d >= today.AddDays(-HistoryDays) ? d.AddDays(1) : today.AddDays(-HistoryDays);
        for (; nextDay <= lastDay && budget > 0; nextDay = nextDay.AddDays(1), budget--)
            Put($"day:{Day(nextDay)}", JsonSerializer.Serialize(await builder.ReportAsync(nextDay, nextDay.AddDays(1), ct), Json));
        var daysThrough = Day(nextDay.AddDays(-1));

        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var firstMonth = thisMonth.AddMonths(-HistoryMonths);
        var nextMonth = ParseMonth(state.MonthsThrough) is { } m && m >= firstMonth ? m.AddMonths(1) : firstMonth;
        for (; nextMonth < thisMonth && budget > 0; nextMonth = nextMonth.AddMonths(1), budget--)
            Put($"month:{Month(nextMonth)}", JsonSerializer.Serialize(await builder.ReportAsync(nextMonth, nextMonth.AddMonths(1), ct), Json));
        var monthsThrough = Month(nextMonth.AddMonths(-1));

        // The accounts, when they changed (or every half hour, in case the website lost them).
        var users = await builder.UsersAsync(ct);
        var usersJson = JsonSerializer.Serialize(users, Json);
        var sendUsers = full || !sent.TryGetValue("users", out var usersHash) || usersHash != Hash(usersJson);
        var signOut = config.SignOutAll > state.SignOutAllSent ? config.SignOutAll : (int?)null;

        // Within the day's cap: what is happening now first, history last (it carries on tomorrow).
        var room = Math.Max(0, MaxDocsPerDay - state.DocsToday);
        if (docs.Count > room)
        {
            docs = docs.Take(room).ToList();
            daysThrough = state.DaysThrough; // the history goes on from where it was, tomorrow
            monthsThrough = state.MonthsThrough;
        }

        // Up in batches; the first also carries the accounts and clean-up, and goes even with no documents (it tells the
        // website the shop is online).
        var batches = docs.Chunk(DocsPerUpload).ToList();
        if (batches.Count == 0) batches.Add([]);
        var count = 0;
        for (var i = 0; i < batches.Count; i++)
        {
            var body = new StringBuilder();
            body.Append("{\"v\":").Append(FormatVersion).Append(",\"docs\":{");
            body.AppendJoin(',', batches[i].Select(doc => $"{JsonSerializer.Serialize(doc.Key)}:{doc.Json}"));
            body.Append('}');
            if (i == 0)
            {
                if (sendUsers) body.Append(",\"users\":").Append(usersJson);
                if (signOut is { } epoch) body.Append(",\"signOutAll\":").Append(epoch);
                if (full) body.Append(",\"keepDaysFrom\":\"").Append(Day(today.AddDays(-KeepDays))).Append('"');
            }
            body.Append('}');

            var failure = await PostAsync(site, config.UploadKey, body.ToString(), ct);
            state = state with { UploadsToday = state.UploadsToday + 1 };
            if (failure is not null)
            {
                // Keep what did go up, so the next run carries on from there.
                await SaveStateAsync(state with
                {
                    Error = failure.Value.Error.ToString(), ErrorDetail = failure.Value.Detail,
                    FailingSinceUtc = state.Error is null ? DateTime.UtcNow : state.FailingSinceUtc ?? DateTime.UtcNow,
                    PausedUntilUtc = failure.Value.Error == OnlineReportsError.Limit ? NextUtcMidnight() : null,
                    Till = tillName, Sent = sent,
                }, ct);
                return new SyncResult(failure.Value.Error == OnlineReportsError.Limit ? SyncOutcome.Paused : SyncOutcome.Failed,
                    count, failure.Value.Error, failure.Value.Detail);
            }
            state = state with { DocsToday = state.DocsToday + batches[i].Length };
            foreach (var (key, json) in batches[i]) sent[key] = Hash(json);
            if (i == 0)
            {
                sent["users"] = Hash(usersJson);
                if (signOut is { } epoch) state = state with { SignOutAllSent = epoch };
            }
            count += batches[i].Length;
        }

        // Day documents are only remembered as "sent" by DaysThrough, so the fingerprints stay small.
        foreach (var key in sent.Keys.Where(k => k.StartsWith("day:") || k.StartsWith("month:")).ToList()) sent.Remove(key);
        await SaveStateAsync(state with
        {
            LastUploadUtc = DateTime.UtcNow, Error = null, ErrorDetail = null, FailingSinceUtc = null, PausedUntilUtc = null, Till = tillName,
            FullUploadUtc = full ? DateTime.UtcNow : state.FullUploadUtc, CatalogUtc = catalog ? DateTime.UtcNow : state.CatalogUtc, DaysThrough = daysThrough, MonthsThrough = monthsThrough, Sent = sent,
        }, ct);
        return new SyncResult(SyncOutcome.Sent, count);
    }

    private async Task<(OnlineReportsError Error, string? Detail)?> PostAsync(string site, string key, string body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{site}/api/upload")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return null;
            var text = await response.Content.ReadAsStringAsync(ct);
            var detail = $"{(int)response.StatusCode} {(text.Length > 200 ? text[..200] : text)}";
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => (OnlineReportsError.Rejected, detail),
                // Cloudflare's free plan answers this way once a daily limit is used up (error 1027).
                HttpStatusCode.TooManyRequests => (OnlineReportsError.Limit, detail),
                _ when text.Contains("1027") => (OnlineReportsError.Limit, detail),
                HttpStatusCode.BadRequest when text.Contains("bad_version") => (OnlineReportsError.Outdated, detail),
                HttpStatusCode.NotFound => (OnlineReportsError.Outdated, detail),
                _ => (OnlineReportsError.Server, detail),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (OnlineReportsError.Offline, ex.Message);
        }
    }

    // ---- One till at a time ----------------------------------------------------------------------------------------

    /// <summary>
    /// This till sends if no other till has sent in the last few minutes. Written only if the value is still what was
    /// read, so two tills can't both take it.
    /// </summary>
    private async Task<bool> TakeLeaseAsync(string tillId, bool force, CancellationToken ct)
    {
        var mine = $"{tillId}|{DateTime.UtcNow.Add(LeaseTime).Ticks}";
        await using var db = await factory.CreateDbContextAsync(ct);
        var old = await db.AppState.AsNoTracking().Where(s => s.Key == LeaseKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (old is null)
        {
            db.AppState.Add(new AppState { Key = LeaseKey, Value = mine });
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                return false; // another till took it at the same moment
            }
        }
        var parts = old.Split('|');
        var holder = parts[0];
        var expired = parts.Length != 2 || !long.TryParse(parts[1], out var ticks) || ticks < DateTime.UtcNow.Ticks;
        if (holder != tillId && !expired && !force) return false;
        return await db.AppState.Where(s => s.Key == LeaseKey && s.Value == old)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, mine), ct) == 1;
    }

    // ---- Storage ---------------------------------------------------------------------------------------------------

    private Task SaveStateAsync(OnlineReportsState state, CancellationToken ct)
    {
        // The database keeps 2000 characters for it.
        if (state.ErrorDetail?.Length > 300) state = state with { ErrorDetail = state.ErrorDetail[..300] };
        if (state.Till?.Length > 100) state = state with { Till = state.Till[..100] };
        return WriteAsync(StateKey, JsonSerializer.Serialize(state, Json), ct);
    }

    private async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AppState.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
    }

    private async Task WriteAsync(string key, string value, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (await db.AppState.Where(s => s.Key == key).ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, value), ct) == 1) return;
        db.AppState.Add(new AppState { Key = key, Value = value });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Created by another till meanwhile: write over it.
            await using var again = await factory.CreateDbContextAsync(ct);
            await again.AppState.Where(s => s.Key == key).ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, value), ct);
        }
    }

    private static T? Parse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTime NextUtcMidnight() => DateTime.UtcNow.Date.AddDays(1);

    /// <summary>
    /// Google's code from what was pasted: the code itself, or the whole tag
    /// (&lt;meta name="google-site-verification" content="abc…" /&gt;). Null when there is none.
    /// </summary>
    public static string? NormalizeGoogleCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, "content\\s*=\\s*[\"']([^\"']+)[\"']");
        var code = (match.Success ? match.Groups[1].Value : text).Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z0-9_-]{10,100}$") ? code : null;
    }

    private static string Hash(string json) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(json)), 0, 12);

    private static string Day(DateTime date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static string Month(DateTime date) => date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime? ParseDay(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private static DateTime? ParseMonth(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
}
