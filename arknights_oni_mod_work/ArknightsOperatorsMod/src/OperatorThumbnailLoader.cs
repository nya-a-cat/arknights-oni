using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ArknightsOperatorsMod {
	public enum OperatorThumbnailFormat {
		Png,
		Jpeg
	}

	public sealed class OperatorThumbnailAsset : IDisposable {
		private IDisposable resourceLease;
		private Action<OperatorThumbnailAsset> disposeCallback;

		public string ResourceKey { get; private set; }
		public string LocalPath { get; private set; }
		public int Width { get; private set; }
		public int Height { get; private set; }
		public OperatorThumbnailFormat Format { get; private set; }

		internal OperatorThumbnailAsset(
			string resourceKey,
			string localPath,
			OperatorThumbnailFileInfo info,
			IDisposable resourceLease,
			Action<OperatorThumbnailAsset> disposeCallback
		) {
			ResourceKey = resourceKey;
			LocalPath = localPath;
			Width = info.Width;
			Height = info.Height;
			Format = info.Format;
			this.resourceLease = resourceLease;
			this.disposeCallback = disposeCallback;
		}

		public void Dispose() {
			IDisposable lease = Interlocked.Exchange(ref resourceLease, null);
			Action<OperatorThumbnailAsset> callback = Interlocked.Exchange(
				ref disposeCallback,
				null
			);
			if (lease != null) lease.Dispose();
			if (callback != null) callback(this);
		}
	}

	public sealed class OperatorThumbnailLoader : IDisposable {
		public const int ThumbnailWidth = 96;
		public const int MaximumConcurrentLoads = 2;
		public const long MaximumThumbnailBytes = 256L * 1024L;
		public const int MaximumDecodedDimension = 256;

		private readonly PrtsResourceService resources;
		private readonly SemaphoreSlim loadGate = new SemaphoreSlim(
			MaximumConcurrentLoads,
			MaximumConcurrentLoads
		);
		private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
		private readonly object activeGate = new object();
		private readonly HashSet<OperatorThumbnailAsset> activeAssets =
			new HashSet<OperatorThumbnailAsset>();
		private int disposed;

		public OperatorThumbnailLoader(PrtsResourceService resources) {
			if (resources == null) throw new ArgumentNullException("resources");
			this.resources = resources;
		}

		public async Task<OperatorThumbnailAsset> LoadAsync(
			OperatorAppearanceDefinition character,
			CancellationToken cancellationToken
		) {
			if (character == null) throw new ArgumentNullException("character");
			ThrowIfDisposed();
			PrtsAssetRequest request = CreateRequest(character);
			bool gateEntered = false;
			IDisposable lease = null;
			using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				lifetime.Token
			)) {
				try {
					await loadGate.WaitAsync(linked.Token).ConfigureAwait(false);
					gateEntered = true;
					linked.Token.ThrowIfCancellationRequested();
					lease = resources.Acquire(new[] { request.Key });
					string localPath = await resources.GetOrDownloadAsync(request, linked.Token)
						.ConfigureAwait(false);
					linked.Token.ThrowIfCancellationRequested();
					OperatorThumbnailFileInfo info = OperatorThumbnailFile.Inspect(
						localPath,
						MaximumThumbnailBytes,
						MaximumDecodedDimension
					);
					linked.Token.ThrowIfCancellationRequested();
					OperatorThumbnailAsset asset = new OperatorThumbnailAsset(
						request.Key,
						localPath,
						info,
						lease,
						ReleaseAsset
					);
					lease = null;
					bool accepted;
					lock (activeGate) {
						accepted = Volatile.Read(ref disposed) == 0;
						if (accepted) activeAssets.Add(asset);
					}
					if (!accepted) {
						asset.Dispose();
						throw new OperationCanceledException(lifetime.Token);
					}
					return asset;
				} finally {
					if (lease != null) lease.Dispose();
					if (gateEntered) loadGate.Release();
				}
			}
		}

		public static PrtsAssetRequest CreateRequest(OperatorAppearanceDefinition character) {
			if (character == null) throw new ArgumentNullException("character");
			if (!IsSafeCharacterId(character.Id))
				throw new InvalidDataException("Operator char_id is not safe for the thumbnail cache");
			Uri sourceUri;
			if (string.IsNullOrWhiteSpace(character.ThumbnailUrl) ||
				!Uri.TryCreate(character.ThumbnailUrl, UriKind.Absolute, out sourceUri))
				throw new InvalidOperationException("Operator thumbnail is unavailable: " + character.Id);
			return new PrtsAssetRequest(
				"thumbnail:" + character.Id + ":" + ThumbnailWidth,
				sourceUri,
				Path.Combine("thumbnails", ThumbnailWidth.ToString(), character.Id + ".img"),
				sourceUri.AbsoluteUri,
				null,
				null,
				MaximumThumbnailBytes
			);
		}

		private static bool IsSafeCharacterId(string characterId) {
			if (string.IsNullOrWhiteSpace(characterId) || characterId.Length > 128)
				return false;
			for (int i = 0; i < characterId.Length; i++) {
				char value = characterId[i];
				if (!char.IsLetterOrDigit(value) && value != '_' && value != '-')
					return false;
			}
			return true;
		}

		private void ReleaseAsset(OperatorThumbnailAsset asset) {
			lock (activeGate) activeAssets.Remove(asset);
		}

		private void ThrowIfDisposed() {
			if (Volatile.Read(ref disposed) != 0)
				throw new ObjectDisposedException("OperatorThumbnailLoader");
		}

		public void Dispose() {
			if (Interlocked.Exchange(ref disposed, 1) != 0) return;
			lifetime.Cancel();
			OperatorThumbnailAsset[] assets;
			lock (activeGate) {
				assets = new OperatorThumbnailAsset[activeAssets.Count];
				activeAssets.CopyTo(assets);
				activeAssets.Clear();
			}
			for (int i = 0; i < assets.Length; i++) assets[i].Dispose();
		}
	}

	public sealed class OperatorThumbnailFileInfo {
		public int Width { get; private set; }
		public int Height { get; private set; }
		public OperatorThumbnailFormat Format { get; private set; }

		internal OperatorThumbnailFileInfo(
			int width,
			int height,
			OperatorThumbnailFormat format
		) {
			Width = width;
			Height = height;
			Format = format;
		}
	}

	public static class OperatorThumbnailFile {
		private static readonly byte[] PngSignature = {
			0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
		};

		public static OperatorThumbnailFileInfo Inspect(
			string path,
			long maximumBytes,
			int maximumDimension
		) {
			if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
			FileInfo file = new FileInfo(path);
			if (!file.Exists) throw new FileNotFoundException("Operator thumbnail is missing", path);
			if (file.Length <= 0L || file.Length > maximumBytes)
				throw new InvalidDataException("Operator thumbnail exceeds its safe file-size boundary");
			byte[] bytes = File.ReadAllBytes(path);
			if (bytes.LongLength > maximumBytes)
				throw new InvalidDataException("Operator thumbnail exceeds its safe file-size boundary");
			OperatorThumbnailFileInfo info = IsPng(bytes)
				? ReadPng(bytes)
				: ReadJpeg(bytes);
			if (info.Width <= 0 || info.Height <= 0 || info.Width > maximumDimension ||
				info.Height > maximumDimension)
				throw new InvalidDataException("Operator thumbnail exceeds its safe decoded dimensions");
			return info;
		}

		private static bool IsPng(byte[] bytes) {
			if (bytes.Length < PngSignature.Length) return false;
			for (int i = 0; i < PngSignature.Length; i++) {
				if (bytes[i] != PngSignature[i]) return false;
			}
			return true;
		}

		private static OperatorThumbnailFileInfo ReadPng(byte[] bytes) {
			if (bytes.Length < 24 || bytes[12] != 0x49 || bytes[13] != 0x48 ||
				bytes[14] != 0x44 || bytes[15] != 0x52)
				throw new InvalidDataException("Operator thumbnail has an invalid PNG header");
			return new OperatorThumbnailFileInfo(
				ReadInt32BigEndian(bytes, 16),
				ReadInt32BigEndian(bytes, 20),
				OperatorThumbnailFormat.Png
			);
		}

		private static OperatorThumbnailFileInfo ReadJpeg(byte[] bytes) {
			if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
				throw new InvalidDataException("Operator thumbnail must be PNG or JPEG");
			int offset = 2;
			while (offset < bytes.Length) {
				while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
				if (offset >= bytes.Length) break;
				byte marker = bytes[offset++];
				if (marker == 0xD8 || marker == 0xD9 || marker == 0x01 ||
					(marker >= 0xD0 && marker <= 0xD7))
					continue;
				if (offset + 1 >= bytes.Length)
					throw new InvalidDataException("Operator thumbnail has a truncated JPEG segment");
				int segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
				if (segmentLength < 2 || offset + segmentLength > bytes.Length)
					throw new InvalidDataException("Operator thumbnail has an invalid JPEG segment");
				if (IsStartOfFrame(marker)) {
					if (segmentLength < 7)
						throw new InvalidDataException("Operator thumbnail has an invalid JPEG frame");
					return new OperatorThumbnailFileInfo(
						(bytes[offset + 5] << 8) | bytes[offset + 6],
						(bytes[offset + 3] << 8) | bytes[offset + 4],
						OperatorThumbnailFormat.Jpeg
					);
				}
				offset += segmentLength;
			}
			throw new InvalidDataException("Operator thumbnail JPEG has no frame dimensions");
		}

		private static bool IsStartOfFrame(byte marker) {
			return (marker >= 0xC0 && marker <= 0xC3) ||
				(marker >= 0xC5 && marker <= 0xC7) ||
				(marker >= 0xC9 && marker <= 0xCB) ||
				(marker >= 0xCD && marker <= 0xCF);
		}

		private static int ReadInt32BigEndian(byte[] bytes, int offset) {
			uint value = ((uint)bytes[offset] << 24) |
				((uint)bytes[offset + 1] << 16) |
				((uint)bytes[offset + 2] << 8) |
				bytes[offset + 3];
			if (value > int.MaxValue)
				throw new InvalidDataException("Operator thumbnail dimension is invalid");
			return (int)value;
		}
	}
}
