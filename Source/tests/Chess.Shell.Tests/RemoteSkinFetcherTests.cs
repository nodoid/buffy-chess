using System;
using System.IO;
using System.Net.Http;
using Chess.Desktop.Rendering;
using NUnit.Framework;

namespace Chess.Desktop.Tests
{
    /// <summary>
    /// Chapter 5's graphics regression tests for the *web* skin path: the
    /// fetcher must degrade gracefully - no exception may ever escape, and a
    /// failed fetch must leave the cache untouched so the renderer can fall
    /// back (procedural atlas or previous cached pack).
    /// </summary>
    [TestFixture]
    public class RemoteSkinFetcherTests : IDisposable
    {
        private readonly string _cache = Path.Combine(
            Path.GetTempPath(), "skin-cache-test-" + Guid.NewGuid().ToString("N"));

        public RemoteSkinFetcherTests()
        {
            Directory.CreateDirectory(_cache);
        }

        [Test]
        public void UnreachableEndpoint_DegradesGracefully()
        {
            // port 1 on loopback: guaranteed connection refused, immediately
            var fetcher = new RemoteSkinFetcher(new HttpClient(),
                _cache);
            var result = fetcher.FetchAsync(
                new Uri("http://127.0.0.1:1/skin.json"), timeoutMs: 3000)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.Success, "unreachable endpoint cannot succeed");
            Assert.IsNotNull(result.Reason);
            Assert.IsNull(result.SkinDirectory);
        }

        [Test]
        public void HandleFetchLeavesCacheUntouched()
        {
            var sentry = Path.Combine(_cache, ".sentry");
            File.WriteAllText(sentry, "untouched");
            var fetcher = new RemoteSkinFetcher(new HttpClient(), _cache);
            fetcher.FetchAsync(new Uri("http://127.0.0.1:1/skin.json"), 2000)
                .GetAwaiter().GetResult();

            Assert.AreEqual("untouched", File.ReadAllText(sentry),
                "a failed fetch must not write into the cache");
        }

        public void Dispose()
        {
            try { Directory.Delete(_cache, true); } catch { }
        }
    }
}
