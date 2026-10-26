using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ClothingStore.Data.OnlineReports;

namespace ClothingStore.Tests;

public class CloudflareDeployerTests
{
    /// <summary>Stands in for the Cloudflare API: answers like it does and records every call.</summary>
    private sealed class FakeCloudflare : HttpMessageHandler
    {
        public List<(string Method, string Path, string? ContentType, string Body)> Calls { get; } = [];
        public string? Subdomain { get; set; }
        public List<string> TakenSubdomains { get; } = [];
        public List<string> Databases { get; } = [];
        public bool RefuseToken { get; set; }
        public string? UploadText { get; private set; }
        public string? UploadBoundary { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer cf-token-1234567890abcdefghij", request.Headers.Authorization?.ToString());
            var path = request.RequestUri!.PathAndQuery.Replace("/client/v4", "");
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method.Method, path, request.Content?.Headers.ContentType?.MediaType, body));

            if (RefuseToken) return Fail(HttpStatusCode.Forbidden, 10000, "Authentication error");
            switch (request.Method.Method, path)
            {
                case ("GET", "/accounts?per_page=50"):
                    return Ok(new JsonArray { new JsonObject { ["id"] = "acc1", ["name"] = "Rami's Account" } });
                case ("GET", "/accounts/acc1/workers/subdomain"):
                    return Subdomain is null ? Fail(HttpStatusCode.NotFound, 10007, "This subdomain does not exist") : Ok(new JsonObject { ["subdomain"] = Subdomain });
                case ("PUT", "/accounts/acc1/workers/subdomain"):
                    var wanted = JsonNode.Parse(body)!["subdomain"]!.GetValue<string>();
                    if (TakenSubdomains.Count > 0)
                    {
                        TakenSubdomains.RemoveAt(0);
                        return Fail(HttpStatusCode.Conflict, 10031, "Subdomain unavailable");
                    }
                    Subdomain = wanted;
                    return Ok(new JsonObject { ["subdomain"] = wanted });
                case ("GET", "/accounts/acc1/d1/database?per_page=100"):
                    var list = new JsonArray();
                    foreach (var d in Databases) list.Add(new JsonObject { ["name"] = d, ["uuid"] = $"uuid-{d}" });
                    list.Add(new JsonObject { ["name"] = "other-db", ["uuid"] = "uuid-other" });
                    return Ok(list);
                case ("POST", "/accounts/acc1/d1/database"):
                    var name = JsonNode.Parse(body)!["name"]!.GetValue<string>();
                    Databases.Add(name);
                    return Ok(new JsonObject { ["name"] = name, ["uuid"] = $"uuid-{name}" });
                case ("PUT", "/accounts/acc1/workers/scripts/pos-reports"):
                    UploadText = body;
                    UploadBoundary = request.Content!.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
                    return Ok(new JsonObject { ["id"] = "pos-reports" });
                case ("POST", "/accounts/acc1/workers/scripts/pos-reports/subdomain"):
                    return Ok(new JsonObject { ["enabled"] = true });
                default:
                    return Fail(HttpStatusCode.NotFound, 7003, $"No route for {request.Method} {path}");
            }
        }

        private static HttpResponseMessage Ok(JsonNode result) => Json(HttpStatusCode.OK,
            new JsonObject { ["success"] = true, ["errors"] = new JsonArray(), ["result"] = result });

        private static HttpResponseMessage Fail(HttpStatusCode status, int code, string message) => Json(status,
            new JsonObject { ["success"] = false, ["errors"] = new JsonArray { new JsonObject { ["code"] = code, ["message"] = message } }, ["result"] = null });

        private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private const string Token = "cf-token-1234567890abcdefghij";

    [Fact]
    public async Task Sets_up_a_new_account_from_one_token()
    {
        var cloudflare = new FakeCloudflare();
        var deployer = new CloudflareDeployer(new HttpClient(cloudflare));
        var steps = new List<string>();

        var (site, url) = await deployer.DeployAsync(Token, "upload-key-abc", "Rami's Boutique!", progress: new SyncProgress(steps));

        // A workers.dev name made from the shop's name, the database, the website, then its address switched on.
        Assert.Matches(@"^ramis-boutique-\d{4}$", cloudflare.Subdomain);
        Assert.Equal($"https://pos-reports.{cloudflare.Subdomain}.workers.dev", url);
        Assert.Equal(new CloudflareSite("acc1", "pos-reports", "uuid-pos-reports", cloudflare.Subdomain!, CloudflareDeployer.CurrentBuild), site);
        Assert.Equal(["pos-reports"], cloudflare.Databases);
        Assert.Equal(["account", "subdomain", "database", "upload", "address"], steps);
        var enable = cloudflare.Calls.Single(c => c.Path.EndsWith("/scripts/pos-reports/subdomain"));
        Assert.True(JsonNode.Parse(enable.Body)!["enabled"]!.GetValue<bool>());

        // The upload: metadata with the database and the upload key, the code as modules and the pages as text.
        var parts = Parts(cloudflare.UploadText!, cloudflare.UploadBoundary!);
        Assert.All(cloudflare.UploadText!.Split('\n').Where(l => l.StartsWith("Content-Disposition")), l => Assert.Matches("name=\"[^\"]+\"", l));
        var metadata = JsonNode.Parse(parts["metadata"].Body)!;
        Assert.Equal("index.js", metadata["main_module"]!.GetValue<string>());
        var bindings = metadata["bindings"]!.AsArray();
        Assert.Contains(bindings, b => b!["type"]!.GetValue<string>() == "d1" && b["name"]!.GetValue<string>() == "DB" && b["id"]!.GetValue<string>() == "uuid-pos-reports");
        Assert.Contains(bindings, b => b!["type"]!.GetValue<string>() == "secret_text" && b["name"]!.GetValue<string>() == "UPLOAD_KEY" && b["text"]!.GetValue<string>() == "upload-key-abc");
        Assert.Equal("application/javascript+module", parts["index.js"].Type);
        Assert.Equal("application/javascript+module", parts["db.js"].Type);
        Assert.Equal("text/plain", parts["ui/app.js"].Type);
        Assert.Equal("text/plain", parts["ui/index.html"].Type);
        Assert.Contains("import indexHtml from \"./ui/index.html\"", parts["index.js"].Body);
    }

    [Fact]
    public async Task Setting_up_again_reuses_the_address_and_database()
    {
        var cloudflare = new FakeCloudflare { Subdomain = "rami" };
        cloudflare.Databases.Add("pos-reports");
        var deployer = new CloudflareDeployer(new HttpClient(cloudflare));

        var (site, url) = await deployer.DeployAsync(Token, "k", "Shop");

        Assert.Equal("https://pos-reports.rami.workers.dev", url);
        Assert.Equal("uuid-pos-reports", site.DatabaseId);
        Assert.DoesNotContain(cloudflare.Calls, c => c.Method != "GET" && (c.Path.EndsWith("/workers/subdomain") || c.Path.EndsWith("/d1/database")));
    }

    [Fact]
    public async Task Tries_another_name_when_the_address_is_taken()
    {
        var cloudflare = new FakeCloudflare();
        cloudflare.TakenSubdomains.AddRange(["x", "y"]);
        var deployer = new CloudflareDeployer(new HttpClient(cloudflare));

        await deployer.DeployAsync(Token, "k", "متجر"); // an Arabic name has no letters for an address

        Assert.Equal(3, cloudflare.Calls.Count(c => c.Method == "PUT" && c.Path.EndsWith("/workers/subdomain")));
        Assert.Matches(@"^shop-\d{4}$", cloudflare.Subdomain);
    }

    [Fact]
    public async Task A_refused_token_says_so()
    {
        var deployer = new CloudflareDeployer(new HttpClient(new FakeCloudflare { RefuseToken = true }));
        var error = await Assert.ThrowsAsync<CloudflareException>(() => deployer.DeployAsync(Token, "k", "Shop"));
        Assert.Equal("account", error.Step);
        Assert.Contains("missing a permission", error.Message);
    }

    [Fact]
    public void The_pos_carries_the_whole_website()
    {
        var names = CloudflareDeployer.WorkerFiles.Select(f => f.Name).ToList();
        Assert.Equal([
            "crypto.js", "db.js", "index.js", "ui/app.css", "ui/app.js", "ui/charts.js", "ui/dom.js",
            "ui/fonts/inter.woff2", "ui/fonts/plex-arabic-400.woff2", "ui/fonts/plex-arabic-600.woff2", "ui/fonts/plex-arabic-700.woff2",
            "ui/format.js", "ui/i18n.js", "ui/icon.svg", "ui/icons.js", "ui/index.html", "ui/insights.js", "ui/manifest.webmanifest",
            "ui/pages.js", "ui/sw.js",
        ], names);
        Assert.Equal("application/octet-stream", CloudflareDeployer.ModuleType("ui/fonts/inter.woff2"));
        Assert.Equal("text/plain", CloudflareDeployer.ModuleType("ui/pages.js"));
        Assert.Equal("application/javascript+module", CloudflareDeployer.ModuleType("index.js"));
        // Every file the server imports is uploaded (or Cloudflare would refuse the script).
        var server = System.Text.Encoding.UTF8.GetString(CloudflareDeployer.WorkerFiles.Single(f => f.Name == "index.js").Content);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(server, @"from ""\./([^""]+)"""))
            Assert.Contains(m.Groups[1].Value, names);
        Assert.Matches("^[0-9a-f]{12}$", CloudflareDeployer.CurrentBuild);
    }

    [Theory]
    [InlineData("Rami's Boutique!", "ramis-boutique")]
    [InlineData("  ZARA  Kids 2 ", "zara-kids-2")]
    [InlineData("بوتيك رامي", "shop")]
    [InlineData("A very long shop name that goes on and on", "a-very-long-shop-name-that-goe")]
    public void Address_names_come_from_the_shop_name(string name, string slug) => Assert.Equal(slug, CloudflareDeployer.Slug(name));

    /// <summary>The parts of a multipart/form-data body, by name: their content type and text.</summary>
    private static Dictionary<string, (string Type, string Body)> Parts(string body, string boundary)
    {
        var parts = new Dictionary<string, (string, string)>();
        foreach (var chunk in body.Split("--" + boundary).Skip(1))
        {
            if (chunk.StartsWith("--")) break;
            var split = chunk.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var headers = chunk[..split];
            var name = System.Text.RegularExpressions.Regex.Match(headers, "name=\"([^\"]+)\"").Groups[1].Value;
            var type = System.Text.RegularExpressions.Regex.Match(headers, "Content-Type: ([^;\r\n]+)").Groups[1].Value;
            parts[name] = (type, chunk[(split + 4)..^2]);
        }
        return parts;
    }

    /// <summary>Records progress at once (Progress&lt;T&gt; would post it later).</summary>
    private sealed class SyncProgress(List<string> steps) : IProgress<string>
    {
        public void Report(string value) => steps.Add(value);
    }
}
