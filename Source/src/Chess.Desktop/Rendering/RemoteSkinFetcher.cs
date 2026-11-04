using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Chess.Core;

namespace Chess.Desktop.Rendering
{
    /// <summary>
    /// Chapter 5's web-skin loader: downloads a skin pack (one skin.json manifest
    /// plus its PNG slots) from an HTTP/HTTPS endpoint, validates every file
    /// against the manifest's SHA-256 entries, caches it in the platform save
    /// directory, and reports the local folder that PieceAtlas.Build consumes.
    /// Every failure degrades to "no update" - the previously cached pack, or
    /// the procedural atlas - never to a broken renderer.
    /// </summary>
    public sealed class RemoteSkinFetcher
    {
        private readonly HttpClient _http;
        private readonly string _cacheRoot;

        public RemoteSkinFetcher(HttpClient http, string cacheRoot)
        {
            _http = http;
            _cacheRoot = cacheRoot;
        }

        /// <summary>Fetcher result. Never throws for network problems.</summary>
        public sealed class FetchResult
        {
            public bool Success;
            public string SkinDirectory;    // null unless Success
            public string Reason;
        }

        /// <summary>
        /// Endpoint contract: <paramref name="manifestUrl"/> returns skin.json;
        /// each manifest entry's File name is resolved relative to it.
        /// </summary>
        public async Task<FetchResult> FetchAsync(Uri manifestUrl, int timeoutMs = 8000)
        {
            try
            {
                var cts = new System.Threading.CancellationTokenSource(timeoutMs);

                using var response = await _http.GetAsync(manifestUrl, cts.Token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cts.Token);

                var manifest = SkinManifest.Parse(json);
                if (manifest == null)
                    return new FetchResult { Reason = "manifest did not parse" };

                var missing = manifest.MissingSlots();
                if (missing.Count > 0)
                    return new FetchResult { Reason = "manifest missing slots: " + string.Join(",", missing) };

                var dir = Directory.CreateDirectory(
                    Path.Combine(_cacheRoot, Sanitise(manifest.Name)));

                var baseUri = new Uri(manifestUrl, manifestUrl.Scheme + "://" + manifestUrl.Authority);
                foreach (var entry in manifest.Entries)
                {
                    byte[] bytes = await _http.GetByteArrayAsync(
                        new Uri(manifestUrl, entry.File), cts.Token);

                    // content-addressed validation: never render unverified bytes
                    if (entry.Sha256 != null && !MatchesSha(bytes, entry.Sha256))
                        return new FetchResult { Reason = "sha mismatch on " + entry.File };

                    File.WriteAllBytes(Path.Combine(dir.FullName, entry.File), bytes);
                }
                File.WriteAllText(Path.Combine(dir.FullName, SkinManifest.FileName), json);

                return new FetchResult { Success = true, SkinDirectory = dir.FullName };
            }
            catch (TaskCanceledException)
            {
                return new FetchResult { Reason = "skin fetch timed out" };
            }
            catch (HttpRequestException e)
            {
                return new FetchResult { Reason = "network: " + e.Message };
            }
            catch (Exception e)
            {
                return new FetchResult { Reason = "unexpected: " + e.Message };
            }
        }

        /// <summary>The path the last successful fetch cached, for offline reuse.</summary>
        public string CachedDirectoryFor(string skinName) =>
            Directory.Exists(Path.Combine(_cacheRoot, Sanitise(skinName)))
                ? Path.Combine(_cacheRoot, Sanitise(skinName))
                : null;

        private static bool MatchesSha(byte[] bytes, string expectedHex)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var sb = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            // case-insensitive compare against the manifest's hex digest
            return string.Equals(sb.ToString(), expectedHex.Trim().ToLowerInvariant(),
                StringComparison.Ordinal);
        }

        private static string Sanitise(string name)
        {
            var bad = new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
            var clean = name;
            foreach (var c in bad) clean = clean.Replace(c, '_');
            return clean.Length == 0 ? "unnamed" : clean;
        }
    }
}
