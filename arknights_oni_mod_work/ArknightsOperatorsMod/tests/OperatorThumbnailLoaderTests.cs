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
		public int Requests { get { return Volatile.Read(ref requests); } }

		public FakeHandler(Dictionary<string, byte[]> responses, string blockedUrl) {
			this.responses = responses;
			this.blockedUrl = blockedUrl;
			BlockedRequestStarted = new ManualResetEventSlim(false);
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
				await releaseBlocked.Task.ConfigureAwait(false);
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

		const string fastUrl = "https://media.prts.wiki/thumb/fast.png";
		const string slowUrl = "https://media.prts.wiki/thumb/slow.png";
		Dictionary<string, byte[]> responses = new Dictionary<string, byte[]> {
			{ fastUrl, PngHeader(96, 96) },
			{ slowUrl, PngHeader(96, 96) }
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
			handler.ReleaseBlockedRequest();
			PrtsAssetRequest slowRequest = OperatorThumbnailLoader.CreateRequest(slow);
			PrtsResourceService.Instance.GetOrDownloadAsync(slowRequest, CancellationToken.None)
				.GetAwaiter().GetResult();
		} finally {
			PrtsResourceService.Shutdown();
		}

		Console.WriteLine("OperatorThumbnailLoaderTests: " + assertions +
			" passed; offline HTTP mock requests=" + handler.Requests);
		return 0;
	}
}
