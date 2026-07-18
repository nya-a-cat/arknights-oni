using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ArknightsOperatorsMod {
	public sealed class PrtsAssetSource {
		public Uri SourceUri { get; private set; }
		public long? ExpectedLength { get; private set; }
		public string ExpectedSha256 { get; private set; }
		public string ResourceVersion { get; private set; }

		public PrtsAssetSource(
			Uri sourceUri,
			long? expectedLength = null,
			string expectedSha256 = null,
			string resourceVersion = null
		) {
			if (sourceUri == null)
				throw new ArgumentNullException("sourceUri");
			SourceUri = sourceUri;
			ExpectedLength = expectedLength;
			ExpectedSha256 = NormalizeHash(expectedSha256);
			ResourceVersion = resourceVersion ?? string.Empty;
		}

		private static string NormalizeHash(string value) {
			return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
		}
	}

	public sealed class PrtsAssetRequest {
		public string Key { get; private set; }
		public Uri SourceUri { get; private set; }
		public IList<PrtsAssetSource> Sources { get; private set; }
		public string RelativePath { get; private set; }
		public string ResourceVersion { get; private set; }
		public long? ExpectedLength { get; private set; }
		public string ExpectedSha256 { get; private set; }
		public long MaximumBytes { get; private set; }

		public PrtsAssetRequest(
			string key,
			Uri sourceUri,
			string relativePath,
			string resourceVersion = null,
			long? expectedLength = null,
			string expectedSha256 = null,
			long maximumBytes = PrtsAssetClient.MaximumAssetBytes
		) {
			if (string.IsNullOrWhiteSpace(key))
				throw new ArgumentNullException("key");
			if (sourceUri == null)
				throw new ArgumentNullException("sourceUri");
			if (string.IsNullOrWhiteSpace(relativePath))
				throw new ArgumentNullException("relativePath");
			Key = key;
			Sources = new List<PrtsAssetSource> {
				new PrtsAssetSource(sourceUri, expectedLength, expectedSha256, resourceVersion)
			}.AsReadOnly();
			SourceUri = Sources[0].SourceUri;
			RelativePath = relativePath;
			ResourceVersion = resourceVersion ?? string.Empty;
			ExpectedLength = Sources[0].ExpectedLength;
			ExpectedSha256 = Sources[0].ExpectedSha256;
			MaximumBytes = ValidateMaximumBytes(maximumBytes);
		}

		public PrtsAssetRequest(
			string key,
			IEnumerable<PrtsAssetSource> sources,
			string relativePath,
			string resourceVersion = null,
			long maximumBytes = PrtsAssetClient.MaximumAssetBytes
		) {
			if (string.IsNullOrWhiteSpace(key))
				throw new ArgumentNullException("key");
			if (sources == null)
				throw new ArgumentNullException("sources");
			if (string.IsNullOrWhiteSpace(relativePath))
				throw new ArgumentNullException("relativePath");
			List<PrtsAssetSource> candidates = new List<PrtsAssetSource>();
			foreach (PrtsAssetSource source in sources) {
				if (source == null)
					throw new ArgumentException("Asset source cannot be null", "sources");
				bool duplicate = false;
				for (int i = 0; i < candidates.Count; i++) {
					if (string.Equals(candidates[i].SourceUri.AbsoluteUri,
						source.SourceUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)) {
						duplicate = true;
						break;
					}
				}
				if (!duplicate)
					candidates.Add(source);
			}
			if (candidates.Count == 0)
				throw new ArgumentException("At least one asset source is required", "sources");
			Key = key;
			Sources = candidates.AsReadOnly();
			SourceUri = Sources[0].SourceUri;
			RelativePath = relativePath;
			ResourceVersion = resourceVersion ?? string.Empty;
			ExpectedLength = Sources[0].ExpectedLength;
			ExpectedSha256 = Sources[0].ExpectedSha256;
			MaximumBytes = ValidateMaximumBytes(maximumBytes);
		}

		private static long ValidateMaximumBytes(long value) {
			if (value <= 0L || value > PrtsAssetClient.MaximumDownloadBytes)
				throw new ArgumentOutOfRangeException("maximumBytes");
			return value;
		}
	}

	public sealed class PrtsDownloadResult {
		public long Length { get; private set; }
		public string Sha256 { get; private set; }
		public Uri SourceUri { get; private set; }
		public string ResourceVersion { get; private set; }

		internal PrtsDownloadResult(long length, string sha256, Uri sourceUri, string resourceVersion) {
			Length = length;
			Sha256 = sha256;
			SourceUri = sourceUri;
			ResourceVersion = resourceVersion ?? string.Empty;
		}
	}

	public sealed class PrtsAssetClient : IDisposable {
		public const int TimeoutSeconds = 120;
		public const int RetryCount = 3;
		public const long MaximumAssetBytes = 64L * 1024L * 1024L;
		public const long MaximumDownloadBytes = 512L * 1024L * 1024L;

		private static readonly HashSet<string> AllowedPrtsHosts = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase
		) {
			"torappu.prts.wiki",
			"static.prts.wiki",
			"media.prts.wiki"
		};
		private static readonly HashSet<string> AllowedReleaseRedirectHosts = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase
		) {
			"release-assets.githubusercontent.com",
			"objects.githubusercontent.com"
		};
		private const string ReleasePathPrefix = "/nya-a-cat/arknights-oni/releases/download/";

		private readonly HttpClient httpClient;
		private readonly SemaphoreSlim serialGate = new SemaphoreSlim(1, 1);
		private bool disposed;

		public PrtsAssetClient() {
			HttpClientHandler handler = new HttpClientHandler {
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
			};
			httpClient = CreateHttpClient(handler);
		}

		internal PrtsAssetClient(HttpMessageHandler handler) {
			if (handler == null)
				throw new ArgumentNullException("handler");
			httpClient = CreateHttpClient(handler);
		}

		private static HttpClient CreateHttpClient(HttpMessageHandler handler) {
			HttpClient client = new HttpClient(handler) {
				Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
			};
			client.DefaultRequestHeaders.UserAgent.ParseAdd("ArknightsONI/0.3");
			return client;
		}

		public async Task<PrtsDownloadResult> DownloadAsync(
			PrtsAssetRequest request,
			string partPath,
			CancellationToken cancellationToken
		) {
			if (request == null)
				throw new ArgumentNullException("request");
			if (string.IsNullOrEmpty(partPath))
				throw new ArgumentNullException("partPath");
			ThrowIfDisposed();
			for (int i = 0; i < request.Sources.Count; i++)
				ValidateSourceUri(request.Sources[i].SourceUri);

			await serialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try {
				Exception lastError = null;
				for (int attempt = 0; attempt <= RetryCount; attempt++) {
					for (int sourceIndex = 0; sourceIndex < request.Sources.Count; sourceIndex++) {
						cancellationToken.ThrowIfCancellationRequested();
						try {
							return await DownloadOnceAsync(
								request.Sources[sourceIndex],
								request.MaximumBytes,
								partPath,
								cancellationToken
							).ConfigureAwait(false);
						} catch (OperationCanceledException error) {
							if (cancellationToken.IsCancellationRequested)
								throw;
							lastError = error;
							DeletePartFile(partPath);
						} catch (Exception error) {
							lastError = error;
							DeletePartFile(partPath);
						}
					}
					if (attempt < RetryCount)
						await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken)
							.ConfigureAwait(false);
				}
				throw new IOException(
					"Asset download failed after trying " + request.Sources.Count +
					" source(s) for " + (RetryCount + 1) + " round(s)",
					lastError
				);
			} finally {
				serialGate.Release();
			}
		}

		private async Task<PrtsDownloadResult> DownloadOnceAsync(
			PrtsAssetSource source,
			long maximumBytes,
			string partPath,
			CancellationToken cancellationToken
		) {
			Directory.CreateDirectory(Path.GetDirectoryName(partPath));
			using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, source.SourceUri))
			using (HttpResponseMessage response = await httpClient.SendAsync(
				message,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken
			).ConfigureAwait(false)) {
				Uri responseUri = response.RequestMessage == null
					? source.SourceUri
					: response.RequestMessage.RequestUri;
				ValidateResponseUri(responseUri);
				response.EnsureSuccessStatusCode();
				long? contentLength = response.Content.Headers.ContentLength;
				if (contentLength.HasValue && contentLength.Value > maximumBytes)
					throw new InvalidDataException("Asset exceeds its configured per-file limit");
				if (source.ExpectedLength.HasValue && contentLength.HasValue &&
					source.ExpectedLength.Value != contentLength.Value)
					throw new InvalidDataException("Content-Length does not match the fallback manifest");

				long written = 0L;
				byte[] buffer = new byte[64 * 1024];
				using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
				using (FileStream output = new FileStream(
					partPath,
					FileMode.Create,
					FileAccess.Write,
					FileShare.None,
					buffer.Length,
					true
				))
				using (SHA256 sha = SHA256.Create()) {
					while (true) {
						int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
							.ConfigureAwait(false);
						if (read == 0)
							break;
						written += read;
						if (written > maximumBytes)
							throw new InvalidDataException("Asset exceeds its configured per-file limit");
						sha.TransformBlock(buffer, 0, read, null, 0);
						await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
					}
					sha.TransformFinalBlock(new byte[0], 0, 0);
					await output.FlushAsync(cancellationToken).ConfigureAwait(false);
					string actualHash = ToHex(sha.Hash);
					if (source.ExpectedLength.HasValue && source.ExpectedLength.Value != written)
						throw new InvalidDataException("Downloaded length does not match the manifest");
					if (!string.IsNullOrEmpty(source.ExpectedSha256) &&
						!string.Equals(source.ExpectedSha256, actualHash, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("Downloaded SHA-256 does not match the manifest");
					return new PrtsDownloadResult(
						written,
						actualHash,
						source.SourceUri,
						source.ResourceVersion
					);
				}
			}
		}

		private static void ValidateSourceUri(Uri sourceUri) {
			if (!IsHttps(sourceUri))
				throw new InvalidOperationException("Only approved HTTPS asset sources are allowed");
			if (AllowedPrtsHosts.Contains(sourceUri.Host))
				return;
			if (string.Equals(sourceUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
				sourceUri.AbsolutePath.StartsWith(ReleasePathPrefix, StringComparison.OrdinalIgnoreCase))
				return;
			throw new InvalidOperationException("Only approved PRTS and arknights-oni Release sources are allowed");
		}

		private static void ValidateResponseUri(Uri responseUri) {
			if (!IsHttps(responseUri))
				throw new InvalidOperationException("Asset redirect left the approved HTTPS boundary");
			if (AllowedPrtsHosts.Contains(responseUri.Host) ||
				AllowedReleaseRedirectHosts.Contains(responseUri.Host))
				return;
			if (string.Equals(responseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
				responseUri.AbsolutePath.StartsWith(ReleasePathPrefix, StringComparison.OrdinalIgnoreCase))
				return;
			throw new InvalidOperationException("Asset redirect left the approved host boundary");
		}

		private static bool IsHttps(Uri uri) {
			return uri != null && uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps;
		}

		private static string ToHex(byte[] bytes) {
			return BitConverter.ToString(bytes).Replace("-", string.Empty);
		}

		private static void DeletePartFile(string path) {
			if (File.Exists(path))
				File.Delete(path);
		}

		private void ThrowIfDisposed() {
			if (disposed)
				throw new ObjectDisposedException("PrtsAssetClient");
		}

		public void Dispose() {
			if (disposed)
				return;
			disposed = true;
			httpClient.Dispose();
			serialGate.Dispose();
		}
	}
}
