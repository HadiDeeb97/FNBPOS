using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClothingStore.Data.OnlineReports;

/// <summary>Cloudflare refused a step, with its own message (a wrong token, missing permission…).</summary>
public sealed class CloudflareException(string step, string message, int? code = null) : Exception(message)
{
    public string Step { get; } = step;
    public int? Code { get; } = code;
}

/// <summary>
/// Puts the shop's reports website on the shop's own free Cloudflare account with one API token: finds the account,
/// gives it a workers.dev address if it has none, creates the database, uploads the website (a copy of
/// cloud/reports-worker carried inside the POS) and switches its address on. Running it again updates the website.
/// The calls are the ones wrangler makes for `wrangler deploy`.
/// </summary>
public class CloudflareDeployer(HttpClient http)
{
    public const string ApiBase = "https://api.cloudflare.com/client/v4";
    public const string ScriptName = "pos-reports";
    public const string DatabaseName = "pos-reports";
    private const string CompatibilityDate = "2026-09-01"; // as in wrangler.toml
    private const string ResourcePrefix = "ReportsWorker/";

    private static readonly Lazy<IReadOnlyList<(string Name, byte[] Content)>> Files = new(LoadFiles);

    /// <summary>The website files carried by this POS (module name, e.g. "index.js" or "ui/app.js").</summary>
    public static IReadOnlyList<(string Name, byte[] Content)> WorkerFiles => Files.Value;

    /// <summary>A fingerprint of those files: when it differs from what a shop's website runs, the website is updated.</summary>
    public static string CurrentBuild { get; } = ComputeBuild();

    /// <summary>
    /// Sets up (or updates) the website. <paramref name="existing"/> is what an earlier setup created, reused when it is
    /// still there. Reports each step through <paramref name="progress"/>.
    /// </summary>
    public async Task<(CloudflareSite Site, string Url)> DeployAsync(
        string token, string uploadKey, string storeName, CloudflareSite? existing = null, IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        token = token.Trim();
        if (token.Length == 0) throw new CloudflareException("token", "No API token.");

        progress?.Report("account");
        var account = await AccountAsync(token, existing?.AccountId, ct);

        progress?.Report("subdomain");
        var subdomain = await SubdomainAsync(token, account, storeName, ct);

        progress?.Report("database");
        var databaseId = await DatabaseAsync(token, account, ct);

        progress?.Report("upload");
        await UploadAsync(token, account, databaseId, uploadKey, ct);

        progress?.Report("address");
        await CallAsync(token, HttpMethod.Post, $"/accounts/{account}/workers/scripts/{ScriptName}/subdomain", "address",
            new JsonObject { ["enabled"] = true, ["previews_enabled"] = false }, ct);

        var site = new CloudflareSite(account, ScriptName, databaseId, subdomain, CurrentBuild);
        return (site, $"https://{ScriptName}.{subdomain}.workers.dev");
    }

    /// <summary>The account the token works on (the one set up before, when the token still reaches it).</summary>
    private async Task<string> AccountAsync(string token, string? preferred, CancellationToken ct)
    {
        var result = await CallAsync(token, HttpMethod.Get, "/accounts?per_page=50", "account", null, ct);
        var ids = (result as JsonArray ?? []).Select(a => a?["id"]?.GetValue<string>()).OfType<string>().ToList();
        if (ids.Count == 0) throw new CloudflareException("account", "This token can't see any Cloudflare account. Create it with the permissions in the guide.");
        return preferred is not null && ids.Contains(preferred) ? preferred : ids[0];
    }

    /// <summary>The account's workers.dev name; a new account gets one made from the shop's name.</summary>
    private async Task<string> SubdomainAsync(string token, string account, string storeName, CancellationToken ct)
    {
        try
        {
            var current = await CallAsync(token, HttpMethod.Get, $"/accounts/{account}/workers/subdomain", "subdomain", null, ct);
            if (current?["subdomain"]?.GetValue<string>() is { Length: > 0 } name) return name;
        }
        catch (CloudflareException ex) when (ex.Code == 10007)
        {
            // None yet.
        }

        // Names are shared by everyone on Cloudflare: try the shop's name with a few random endings.
        var slug = Slug(storeName);
        for (var attempt = 0; ; attempt++)
        {
            var name = $"{slug}-{RandomNumberGenerator.GetInt32(1000, 10000)}";
            try
            {
                var created = await CallAsync(token, HttpMethod.Put, $"/accounts/{account}/workers/subdomain", "subdomain",
                    new JsonObject { ["subdomain"] = name }, ct);
                return created?["subdomain"]?.GetValue<string>() ?? name;
            }
            catch (CloudflareException ex) when (ex.Code == 10031 && attempt < 4)
            {
                // Taken: try another ending.
            }
        }
    }

    /// <summary>"Rami's Boutique!" → "ramis-boutique"; something plain when the name has no Latin letters (an Arabic name).</summary>
    public static string Slug(string? storeName)
    {
        var text = Regex.Replace((storeName ?? "").ToLowerInvariant().Replace("'", "").Replace("\u2019", ""), "[^a-z0-9]+", "-").Trim('-');
        if (text.Length > 30) text = text[..30].TrimEnd('-');
        return text.Length >= 3 ? text : "shop";
    }

    private async Task<string> DatabaseAsync(string token, string account, CancellationToken ct)
    {
        var list = await CallAsync(token, HttpMethod.Get, $"/accounts/{account}/d1/database?per_page=100", "database", null, ct);
        var found = (list as JsonArray ?? []).FirstOrDefault(d => d?["name"]?.GetValue<string>() == DatabaseName);
        if (found?["uuid"]?.GetValue<string>() is { } id) return id;
        var created = await CallAsync(token, HttpMethod.Post, $"/accounts/{account}/d1/database", "database",
            new JsonObject { ["name"] = DatabaseName }, ct);
        return created?["uuid"]?.GetValue<string>() ?? throw new CloudflareException("database", "Cloudflare didn't return the new database.");
    }

    private async Task UploadAsync(string token, string account, string databaseId, string uploadKey, CancellationToken ct)
    {
        var metadata = new JsonObject
        {
            ["main_module"] = "index.js",
            ["compatibility_date"] = CompatibilityDate,
            ["bindings"] = new JsonArray
            {
                new JsonObject { ["type"] = "d1", ["name"] = "DB", ["id"] = databaseId },
                new JsonObject { ["type"] = "secret_text", ["name"] = "UPLOAD_KEY", ["text"] = uploadKey },
            },
        };
        using var form = new MultipartFormDataContent();
        form.Add(Part(new StringContent(metadata.ToJsonString(), Encoding.UTF8, "application/json"), "metadata", null));
        foreach (var (name, content) in WorkerFiles)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue(ModuleType(name));
            form.Add(Part(part, name, name));
        }
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/accounts/{account}/workers/scripts/{ScriptName}") { Content = form };
        await SendAsync(token, request, "upload", ct);
    }

    /// <summary>
    /// How Cloudflare is to load each file, as wrangler.toml says: the server code as ES modules, the pages as text, the
    /// fonts as raw bytes.
    /// </summary>
    public static string ModuleType(string name) =>
        name.EndsWith(".woff2", StringComparison.Ordinal) ? "application/octet-stream"
        : name.StartsWith("ui/", StringComparison.Ordinal) ? "text/plain"
        : "application/javascript+module";

    /// <summary>
    /// A form part with its name in quotes, as browsers and wrangler send it (.NET leaves "ui/app.js" unquoted, and a
    /// slash isn't allowed in an unquoted name).
    /// </summary>
    private static HttpContent Part(HttpContent content, string name, string? fileName)
    {
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = $"\"{name}\"",
            FileName = fileName is null ? null : $"\"{fileName}\"",
        };
        return content;
    }

    // ---- Calls -----------------------------------------------------------------------------------------------------

    private async Task<JsonNode?> CallAsync(string token, HttpMethod method, string path, string step, JsonObject? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, ApiBase + path);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await SendAsync(token, request, step, ct);
    }

    /// <summary>Sends a call and returns its "result", or throws with Cloudflare's own message.</summary>
    private async Task<JsonNode?> SendAsync(string token, HttpRequestMessage request, string step, CancellationToken ct)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CloudflareException(step, "Couldn't reach Cloudflare. Check the internet connection. (" + ex.Message + ")");
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            try
            {
                json = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
            }
            if (response.IsSuccessStatusCode && json?["success"]?.GetValue<bool>() != false) return json?["result"];

            var error = (json?["errors"] as JsonArray)?.FirstOrDefault();
            var code = error?["code"]?.GetValue<int>();
            var message = error?["message"]?.GetValue<string>() ?? $"{(int)response.StatusCode} {response.ReasonPhrase}";
            if ((int)response.StatusCode is 401 or 403 || code is 10000 or 9109)
                message = $"The token was refused or is missing a permission ({message}).";
            throw new CloudflareException(step, message, code);
        }
    }

    // ---- The website's files -----------------------------------------------------------------------------------------

    private static IReadOnlyList<(string Name, byte[] Content)> LoadFiles()
    {
        var assembly = typeof(CloudflareDeployer).Assembly;
        var files = new List<(string, byte[])>();
        // Only what the website loads (not, say, the fonts' licence texts kept beside them).
        string[] served = [".js", ".html", ".css", ".svg", ".webmanifest", ".woff2"];
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix) && served.Any(ext => n.EndsWith(ext, StringComparison.Ordinal)))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            // Built on Windows the folder separator is a backslash.
            files.Add((resource[ResourcePrefix.Length..].Replace('\\', '/'), copy.ToArray()));
        }
        return files.OrderBy(f => f.Item1, StringComparer.Ordinal).ToList();
    }

    private static string ComputeBuild()
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, content) in WorkerFiles)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(name));
            // Line endings differ between a Windows and a Linux checkout; the website is the same.
            if (name.EndsWith(".woff2", StringComparison.Ordinal)) sha.AppendData(content);
            else sha.AppendData(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content).Replace("\r\n", "\n")));
        }
        return Convert.ToHexString(sha.GetHashAndReset())[..12].ToLowerInvariant();
    }
}
