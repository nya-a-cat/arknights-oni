using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArknightsOperatorsMod;

namespace UnityEngine {
	public static class Debug {
		public static void LogWarning(object value) { Console.Error.WriteLine(value); }
	}
}

namespace ArknightsOperatorsMod {
	public enum ResourcePersistencePolicy { OnDemandCache, Permanent }

	public sealed class ModConfig {
		public const int MinimumCacheCapacityMiB = 128;
		public const int DefaultCacheCapacityMiB = 512;
		public const int MaximumCacheCapacityMiB = 2000;

		public ResourcePersistencePolicy DownloadPolicy { get; set; }
		public int CacheCapacityMiB { get; set; }

		public static long CacheCapacityBytes(int capacityMiB) {
			if (capacityMiB < MinimumCacheCapacityMiB || capacityMiB > MaximumCacheCapacityMiB)
				capacityMiB = DefaultCacheCapacityMiB;
			return capacityMiB * 1024L * 1024L;
		}
	}

	public static class ModConfigStore {
		public static ModConfig Current {
			get {
				return new ModConfig {
					DownloadPolicy = ResourcePersistencePolicy.OnDemandCache,
					CacheCapacityMiB = ModConfig.DefaultCacheCapacityMiB
				};
			}
		}
	}

	public static class ModAssets {
		public static string SharedRoot;
		public static string SharedAssetsRoot { get { return Path.Combine(SharedRoot, "assets"); } }
		public static string TempRoot { get { return Path.Combine(SharedRoot, "tmp"); } }
		public static string CacheIndexPath { get { return Path.Combine(SharedRoot, "cache-index.json"); } }

		public static void InitializeSharedStorage() {
			Directory.CreateDirectory(SharedRoot);
			Directory.CreateDirectory(SharedAssetsRoot);
			Directory.CreateDirectory(TempRoot);
		}
	}
}

internal static class OperatorThumbnailLoaderTests {
	private sealed class FakeHandler : HttpMessageHandler {
		private readonly Dictionary<string, byte[]> responses;
		private readonly string blockedUrl;
		private readonly TaskCompletionSource<bool> releaseBlocked =
			new TaskCompletionSource<bool>();
		private int requests;

		public ManualResetEventSlim BlockedRequestStarted { get; private set; }
		public ManualResetEventSlim BlockedRequestCanceled { get; private set; }
		public int Requests { get { return Volatile.Read(ref requests); } }

		public FakeHandler(Dictionary<string, byte[]> responses, string blockedUrl) {
			this.responses = responses;
			this.blockedUrl = blockedUrl;
			BlockedRequestStarted = new ManualResetEventSlim(false);
			BlockedRequestCanceled = new ManualResetEventSlim(false);
		}

		public void ReleaseBlockedRequest() {
			releaseBlocked.TrySetResult(true);
		}

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken
		) {
			Interlocked.Increment(ref requests);
			if (string.Equals(request.RequestUri.AbsoluteUri, blockedUrl, StringComparison.Ordinal)) {
				BlockedRequestStarted.Set();
				try {
					Task canceled = Task.Delay(Timeout.Infinite, cancellationToken);
					Task completed = await Task.WhenAny(releaseBlocked.Task, canceled)
						.ConfigureAwait(false);
					if (!object.ReferenceEquals(completed, releaseBlocked.Task))
						cancellationToken.ThrowIfCancellationRequested();
				} catch (OperationCanceledException) {
					BlockedRequestCanceled.Set();
					throw;
				}
			}
			byte[] content;
			if (!responses.TryGetValue(request.RequestUri.AbsoluteUri, out content))
				return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
			return new HttpResponseMessage(HttpStatusCode.OK) {
				RequestMessage = request,
				Content = new ByteArrayContent(content)
			};
		}
	}

	private sealed class ConcurrencyHandler : HttpMessageHandler {
		private readonly byte[] content;
		private readonly TaskCompletionSource<bool> release = new TaskCompletionSource<bool>();
		private int active;
		private int maximumActive;
		private int requests;

		public ManualResetEventSlim TwoRequestsStarted { get; private set; }
		public int MaximumActive { get { return Volatile.Read(ref maximumActive); } }
		public int Requests { get { return Volatile.Read(ref requests); } }

		public ConcurrencyHandler(byte[] content) {
			this.content = content;
			TwoRequestsStarted = new ManualResetEventSlim(false);
		}

		public void Release() {
			release.TrySetResult(true);
		}

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken
		) {
			Interlocked.Increment(ref requests);
			int current = Interlocked.Increment(ref active);
			int observed;
			do {
				observed = Volatile.Read(ref maximumActive);
				if (observed >= current) break;
			} while (Interlocked.CompareExchange(ref maximumActive, current, observed) != observed);
			if (current >= 2) TwoRequestsStarted.Set();
			try {
				Task canceled = Task.Delay(Timeout.Infinite, cancellationToken);
				Task completed = await Task.WhenAny(release.Task, canceled).ConfigureAwait(false);
				if (!object.ReferenceEquals(completed, release.Task))
					cancellationToken.ThrowIfCancellationRequested();
				return new HttpResponseMessage(HttpStatusCode.OK) {
					RequestMessage = request,
					Content = new ByteArrayContent(content)
				};
			} finally {
				Interlocked.Decrement(ref active);
			}
		}
	}

	private static int assertions;

	private static void Require(bool condition, string message) {
		assertions++;
		if (!condition) throw new InvalidOperationException(message);
	}

	private static T RequireThrows<T>(Action action, string message) where T : Exception {
		assertions++;
		try {
			action();
		} catch (T error) {
			return error;
		}
		throw new InvalidOperationException(message);
	}

	private static byte[] PngHeader(int width, int height) {
		byte[] bytes = new byte[24] {
			0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
			0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
		};
		WriteInt32BigEndian(bytes, 16, width);
		WriteInt32BigEndian(bytes, 20, height);
		return bytes;
	}

	private static byte[] JpegHeader(int width, int height) {
		return new byte[] {
			0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08,
			(byte)(height >> 8), (byte)height,
			(byte)(width >> 8), (byte)width,
			0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00
		};
	}

	private static void WriteInt32BigEndian(byte[] bytes, int offset, int value) {
		bytes[offset] = (byte)(value >> 24);
		bytes[offset + 1] = (byte)(value >> 16);
		bytes[offset + 2] = (byte)(value >> 8);
		bytes[offset + 3] = (byte)value;
	}

	private static OperatorAppearanceDefinition Character(string id, string thumbnailUrl) {
		string thumbnailProperty = thumbnailUrl == null
			? string.Empty
			: ",\"thumbnail_url\":\"" + thumbnailUrl + "\"";
		string json = "{\"schema_version\":1,\"operators\":[{" +
			"\"id\":\"" + id + "\",\"name\":\"Test\"" + thumbnailProperty + "," +
			"\"skins\":[{\"name\":\"default\",\"models\":[\"build\"]}]}]}";
		return OperatorAppearanceCatalog.FromJson(json).Operators[0];
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected an isolated cache directory");
		string root = Path.GetFullPath(args[0]);
		Directory.CreateDirectory(root);
		ModAssets.SharedRoot = root;

		OperatorAppearanceDefinition legacy = Character("char_legacy", null);
		Require(legacy.ThumbnailUrl == null, "catalog without thumbnail_url is incompatible");
		RequireThrows<InvalidDataException>(
			() => Character("char_http", "http://media.prts.wiki/http.png"),
			"catalog accepted a non-HTTPS thumbnail"
		);

		string imageRoot = Path.Combine(root, "image-fixtures");
		Directory.CreateDirectory(imageRoot);
		string pngPath = Path.Combine(imageRoot, "valid.png");
		File.WriteAllBytes(pngPath, PngHeader(96, 80));
		OperatorThumbnailFileInfo png = OperatorThumbnailFile.Inspect(
			pngPath,
			OperatorThumbnailLoader.MaximumThumbnailBytes,
			OperatorThumbnailLoader.MaximumDecodedDimension
		);
		Require(png.Format == OperatorThumbnailFormat.Png && png.Width == 96 && png.Height == 80,
			"PNG dimensions were not detected");

		string jpegPath = Path.Combine(imageRoot, "valid.jpg");
		File.WriteAllBytes(jpegPath, JpegHeader(72, 96));
		OperatorThumbnailFileInfo jpeg = OperatorThumbnailFile.Inspect(
			jpegPath,
			OperatorThumbnailLoader.MaximumThumbnailBytes,
			OperatorThumbnailLoader.MaximumDecodedDimension
		);
		Require(jpeg.Format == OperatorThumbnailFormat.Jpeg && jpeg.Width == 72 && jpeg.Height == 96,
			"JPEG dimensions were not detected");

		string oversizedPath = Path.Combine(imageRoot, "oversized.png");
		File.WriteAllBytes(oversizedPath, PngHeader(300, 96));
		RequireThrows<InvalidDataException>(
			() => OperatorThumbnailFile.Inspect(
				oversizedPath,
				OperatorThumbnailLoader.MaximumThumbnailBytes,
				OperatorThumbnailLoader.MaximumDecodedDimension
			),
			"oversized decoded dimensions were accepted"
		);
		string invalidPath = Path.Combine(imageRoot, "invalid.img");
		File.WriteAllBytes(invalidPath, Encoding.UTF8.GetBytes("not-an-image"));
		RequireThrows<InvalidDataException>(
			() => OperatorThumbnailFile.Inspect(
				invalidPath,
				OperatorThumbnailLoader.MaximumThumbnailBytes,
				OperatorThumbnailLoader.MaximumDecodedDimension
			),
			"unknown image magic was accepted"
		);

		ConcurrencyHandler concurrencyHandler = new ConcurrencyHandler(PngHeader(96, 96));
		PrtsAssetClient concurrencyClient = new PrtsAssetClient(concurrencyHandler);
		try {
			PrtsAssetRequest firstConcurrent = OperatorThumbnailLoader.CreateRequest(Character(
				"char_concurrent_a",
				"https://media.prts.wiki/thumb/concurrent-a.png"
			));
			PrtsAssetRequest secondConcurrent = OperatorThumbnailLoader.CreateRequest(Character(
				"char_concurrent_b",
				"https://media.prts.wiki/thumb/concurrent-b.png"
			));
			Task<PrtsDownloadResult> firstDownload = concurrencyClient.DownloadAsync(
				firstConcurrent,
				Path.Combine(root, "concurrent-a.part"),
				CancellationToken.None
			);
			Task<PrtsDownloadResult> secondDownload = concurrencyClient.DownloadAsync(
				secondConcurrent,
				Path.Combine(root, "concurrent-b.part"),
				CancellationToken.None
			);
			Require(concurrencyHandler.TwoRequestsStarted.Wait(TimeSpan.FromSeconds(2)),
				"two thumbnail downloads did not start concurrently");
			concurrencyHandler.Release();
			Task.WaitAll(firstDownload, secondDownload);
			Require(concurrencyHandler.MaximumActive == 2,
				"thumbnail downloads exceeded or missed the two-slot limit");
		} finally {
			concurrencyHandler.Release();
			concurrencyClient.Dispose();
		}

		ConcurrencyHandler queueHandler = new ConcurrencyHandler(PngHeader(96, 96));
		PrtsResourceService.InitializeForTests(new PrtsAssetClient(queueHandler));
		try {
			OperatorThumbnailLoader queueLoader = new OperatorThumbnailLoader(
				PrtsResourceService.Instance,
				TimeSpan.FromMilliseconds(200)
			);
			Task<OperatorThumbnailAsset>[] queued = new[] {
				queueLoader.LoadAsync(Character(
					"char_queue_a",
					"https://media.prts.wiki/thumb/queue-a.png"
				), CancellationToken.None),
				queueLoader.LoadAsync(Character(
					"char_queue_b",
					"https://media.prts.wiki/thumb/queue-b.png"
				), CancellationToken.None),
				queueLoader.LoadAsync(Character(
					"char_queue_c",
					"https://media.prts.wiki/thumb/queue-c.png"
				), CancellationToken.None)
			};
			Require(queueHandler.TwoRequestsStarted.Wait(TimeSpan.FromSeconds(2)),
				"queue test did not fill both thumbnail slots");
			Thread.Sleep(275);
			int pendingIndex = -1;
			int timeoutCount = 0;
			for (int i = 0; i < queued.Length; i++) {
				if (!queued[i].IsCompleted) {
					pendingIndex = i;
					continue;
				}
				RequireThrows<TimeoutException>(
					() => queued[i].GetAwaiter().GetResult(),
					"active thumbnail did not time out"
				);
				timeoutCount++;
			}
			Require(timeoutCount == 2 && pendingIndex >= 0,
				"queued thumbnail consumed its timeout before entering a download slot");
			Require(queueHandler.Requests == 3,
				"queued thumbnail did not start after a slot became available");
			queueHandler.Release();
			OperatorThumbnailAsset queuedAsset = queued[pendingIndex].GetAwaiter().GetResult();
			queuedAsset.Dispose();
			queueLoader.Dispose();
		} finally {
			queueHandler.Release();
			PrtsResourceService.Shutdown();
		}

		const string fastUrl = "https://media.prts.wiki/thumb/fast.png";
		const string slowUrl = "https://media.prts.wiki/thumb/slow.png";
		const string timeoutUrl = "https://media.prts.wiki/thumb/timeout.png";
		const string shutdownUrl = "https://media.prts.wiki/thumb/shutdown.png";
		Dictionary<string, byte[]> responses = new Dictionary<string, byte[]> {
			{ fastUrl, PngHeader(96, 96) },
			{ slowUrl, PngHeader(96, 96) },
			{ timeoutUrl, PngHeader(96, 96) },
			{ shutdownUrl, PngHeader(96, 96) }
		};
		FakeHandler handler = new FakeHandler(responses, slowUrl);
		PrtsAssetClient client = new PrtsAssetClient(handler);
		PrtsResourceService.InitializeForTests(client);
		try {
			OperatorAppearanceDefinition fast = Character("char_fast", fastUrl);
			PrtsAssetRequest fastRequest = OperatorThumbnailLoader.CreateRequest(fast);
			Require(fastRequest.Key == "thumbnail:char_fast:96", "thumbnail cache key mismatch");
			Require(fastRequest.RelativePath == Path.Combine("thumbnails", "96", "char_fast.img"),
				"thumbnail cache path mismatch");
			Require(fastRequest.ResourceVersion == fastUrl, "thumbnail version must be the full URL");
			Require(fastRequest.MaximumBytes == 256L * 1024L, "thumbnail byte limit mismatch");
			Require(OperatorThumbnailLoader.MaximumConcurrentLoads == 2,
				"thumbnail concurrency limit mismatch");
			Require(OperatorThumbnailLoader.LoadTimeoutSeconds == 15,
				"thumbnail timeout mismatch");
			Require(PrtsAssetClient.MaximumConcurrentDownloads == 2,
				"asset client concurrency does not match the gallery limit");

			OperatorThumbnailLoader loader = new OperatorThumbnailLoader(PrtsResourceService.Instance);
			OperatorThumbnailAsset first = loader.LoadAsync(fast, CancellationToken.None)
				.GetAwaiter().GetResult();
			Require(File.Exists(first.LocalPath), "thumbnail download was not cached");
			first.Dispose();
			OperatorThumbnailAsset cached = loader.LoadAsync(fast, CancellationToken.None)
				.GetAwaiter().GetResult();
			Require(handler.Requests == 1, "cached thumbnail triggered another HTTP request");
			loader.Dispose();
			cached.Dispose();

			OperatorAppearanceDefinition slow = Character("char_slow", slowUrl);
			OperatorThumbnailLoader closing = new OperatorThumbnailLoader(
				PrtsResourceService.Instance
			);
			Task<OperatorThumbnailAsset> pending = closing.LoadAsync(slow, CancellationToken.None);
			Require(handler.BlockedRequestStarted.Wait(TimeSpan.FromSeconds(2)),
				"blocked thumbnail request did not start");
			closing.Dispose();
			RequireThrows<OperationCanceledException>(
				() => pending.GetAwaiter().GetResult(),
				"closing the thumbnail scope did not cancel its pending wait"
			);
			Require(handler.BlockedRequestCanceled.Wait(TimeSpan.FromSeconds(2)),
				"closing the thumbnail scope left the underlying HTTP request running");
			handler.ReleaseBlockedRequest();
			PrtsAssetRequest slowRequest = OperatorThumbnailLoader.CreateRequest(slow);
			PrtsResourceService.Instance.GetOrDownloadAsync(slowRequest, CancellationToken.None)
				.GetAwaiter().GetResult();

			PrtsResourceService.Shutdown();
			FakeHandler timeoutHandler = new FakeHandler(responses, timeoutUrl);
			PrtsResourceService.InitializeForTests(new PrtsAssetClient(timeoutHandler));
			OperatorThumbnailLoader timing = new OperatorThumbnailLoader(
				PrtsResourceService.Instance,
				TimeSpan.FromMilliseconds(50)
			);
			Task<OperatorThumbnailAsset> timeoutPending = timing.LoadAsync(
				Character("char_timeout", timeoutUrl),
				CancellationToken.None
			);
			Require(timeoutHandler.BlockedRequestStarted.Wait(TimeSpan.FromSeconds(2)),
				"timeout thumbnail request did not start");
			RequireThrows<TimeoutException>(
				() => timeoutPending.GetAwaiter().GetResult(),
				"thumbnail timeout did not fail the pending request"
			);
			Require(timeoutHandler.BlockedRequestCanceled.Wait(TimeSpan.FromSeconds(2)),
				"thumbnail timeout left the HTTP request running");
			timing.Dispose();
			PrtsResourceService.Shutdown();

			FakeHandler shutdownHandler = new FakeHandler(responses, shutdownUrl);
			PrtsResourceService.InitializeForTests(new PrtsAssetClient(shutdownHandler));
			OperatorAppearanceDefinition shutdownCharacter = Character("char_shutdown", shutdownUrl);
			Task<string> shutdownPending = PrtsResourceService.Instance.GetOrDownloadAsync(
				OperatorThumbnailLoader.CreateRequest(shutdownCharacter),
				CancellationToken.None
			);
			Require(shutdownHandler.BlockedRequestStarted.Wait(TimeSpan.FromSeconds(2)),
				"shutdown thumbnail request did not start");
			PrtsResourceService.Shutdown();
			RequireThrows<OperationCanceledException>(
				() => shutdownPending.GetAwaiter().GetResult(),
				"resource-service shutdown did not cancel its HTTP request"
			);
			Require(shutdownHandler.BlockedRequestCanceled.Wait(TimeSpan.FromSeconds(2)),
				"resource-service shutdown left the HTTP request running");
		} finally {
			PrtsResourceService.Shutdown();
		}

		Console.WriteLine("OperatorThumbnailLoaderTests: " + assertions +
			" passed; offline HTTP mock requests=" + handler.Requests);
		return 0;
	}
}
