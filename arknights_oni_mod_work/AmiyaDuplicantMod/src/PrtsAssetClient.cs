using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace AmiyaDuplicantMod {
	public sealed class PrtsAssetRequest {
		public string Key { get; private set; }
		public Uri SourceUri { get; private set; }
		public string RelativePath { get; private set; }
		public string ResourceVersion { get; private set; }
		public long? ExpectedLength { get; private set; }
		public string ExpectedSha256 { get; private set; }

		public PrtsAssetRequest(
			string key,
			Uri sourceUri,
			string relativePath,
			string resourceVersion = null,
			long? expectedLength = null,
			string expectedSha256 = null
		) {
			if (string.IsNullOrWhiteSpace(key))
				throw new ArgumentNullException("key");
			if (sourceUri == null)
				throw new ArgumentNullException("sourceUri");
			if (string.IsNullOrWhiteSpace(relativePath))
				throw new ArgumentNullException("relativePath");
			Key = key;
			SourceUri = sourceUri;
			RelativePath = relativePath;
			ResourceVersion = resourceVersion ?? string.Empty;
			ExpectedLength = expectedLength;
			ExpectedSha256 = NormalizeHash(expectedSha256);
		}

		private static string NormalizeHash(string value) {
			return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
		}
	}

	public sealed class PrtsDownloadResult {
		public long Length { get; private set; }
		public string Sha256 { get; private set; }

		internal PrtsDownloadResult(long length, string sha256) {
			Length = length;
			Sha256 = sha256;
		}
	}

	public sealed class PrtsAssetClient : IDisposable {
		public const int TimeoutSeconds = 120;
		public const int RetryCount = 3;
		public const long MaximumAssetBytes = 64L * 1024L * 1024L;

		private static readonly HashSet<string> AllowedHosts = new HashSet<string>(
			StringComparer.OrdinalIgnoreCase
		) {
			"torappu.prts.wiki",
			"static.prts.wiki"
		};

		private readonly HttpClient httpClient;
		private readonly SemaphoreSlim serialGate = new SemaphoreSlim(1, 1);
		private bool disposed;

		public PrtsAssetClient() {
			HttpClientHandler handler = new HttpClientHandler {
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
			};
			httpClient = new HttpClient(handler) {
				Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
			};
			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AmiyaDuplicantMod/0.2");
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
			ValidateUri(request.SourceUri);

			await serialGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try {
				Exception lastError = null;
				for (int attempt = 0; attempt <= RetryCount; attempt++) {
					cancellationToken.ThrowIfCancellationRequested();
					try {
						return await DownloadOnceAsync(request, partPath, cancellationToken)
							.ConfigureAwait(false);
					} catch (OperationCanceledException) {
						throw;
					} catch (Exception error) {
						lastError = error;
						DeletePartFile(partPath);
						if (attempt == RetryCount)
							break;
						await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken)
							.ConfigureAwait(false);
					}
				}
				throw new IOException(
					"PRTS download failed after " + (RetryCount + 1) + " attempts: " + request.SourceUri,
					lastError
				);
			} finally {
				serialGate.Release();
			}
		}

		private async Task<PrtsDownloadResult> DownloadOnceAsync(
			PrtsAssetRequest request,
			string partPath,
			CancellationToken cancellationToken
		) {
			Directory.CreateDirectory(Path.GetDirectoryName(partPath));
			using (HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, request.SourceUri))
			using (HttpResponseMessage response = await httpClient.SendAsync(
				message,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken
			).ConfigureAwait(false)) {
				response.EnsureSuccessStatusCode();
				long? contentLength = response.Content.Headers.ContentLength;
				if (contentLength.HasValue && contentLength.Value > MaximumAssetBytes)
					throw new InvalidDataException("PRTS asset exceeds the 64 MiB per-file limit");
				if (request.ExpectedLength.HasValue && contentLength.HasValue &&
					request.ExpectedLength.Value != contentLength.Value)
					throw new InvalidDataException("PRTS Content-Length does not match the manifest");

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
						if (written > MaximumAssetBytes)
							throw new InvalidDataException("PRTS asset exceeds the 64 MiB per-file limit");
						sha.TransformBlock(buffer, 0, read, null, 0);
						await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
					}
					sha.TransformFinalBlock(new byte[0], 0, 0);
					await output.FlushAsync(cancellationToken).ConfigureAwait(false);
					string actualHash = ToHex(sha.Hash);
					if (request.ExpectedLength.HasValue && request.ExpectedLength.Value != written)
						throw new InvalidDataException("Downloaded length does not match the manifest");
					if (!string.IsNullOrEmpty(request.ExpectedSha256) &&
						!string.Equals(request.ExpectedSha256, actualHash, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException("Downloaded SHA-256 does not match the manifest");
					return new PrtsDownloadResult(written, actualHash);
				}
			}
		}

		private static void ValidateUri(Uri sourceUri) {
			if (!sourceUri.IsAbsoluteUri || sourceUri.Scheme != Uri.UriSchemeHttps ||
				!AllowedHosts.Contains(sourceUri.Host))
				throw new InvalidOperationException("Only HTTPS PRTS asset hosts are allowed");
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
