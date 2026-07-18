using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class PrtsResourceService : IDisposable {
		[Obsolete("Use the configured cache capacity instead of the legacy fixed limit")]
		public const long OnDemandCacheLimitBytes = 512L * 1024L * 1024L;

		private readonly ResourceIndexStore index;
		private readonly PrtsAssetClient client;
		private readonly object downloadGate = new object();
		private readonly Dictionary<string, SharedDownload> inFlightDownloads =
			new Dictionary<string, SharedDownload>(StringComparer.Ordinal);
		private readonly Dictionary<string, int> inFlightResourceKeys =
			new Dictionary<string, int>(StringComparer.Ordinal);
		private readonly object activeGate = new object();
		private readonly Dictionary<string, int> activeKeys = new Dictionary<string, int>(StringComparer.Ordinal);
		private bool disposed;

		public static PrtsResourceService Instance { get; private set; }

		public long IndexedDiskUsage {
			get { return index.GetIndexedDiskUsage(); }
		}

		private PrtsResourceService(PrtsAssetClient assetClient) {
			index = new ResourceIndexStore(ModAssets.CacheIndexPath, ModAssets.SharedAssetsRoot);
			client = assetClient ?? new PrtsAssetClient();
		}

		public static void Initialize() {
			if (Instance != null)
				return;
			ModAssets.InitializeSharedStorage();
			Instance = new PrtsResourceService(null);
		}

		internal static void InitializeForTests(PrtsAssetClient assetClient) {
			if (Instance != null)
				throw new InvalidOperationException("Resource service is already initialized");
			if (assetClient == null)
				throw new ArgumentNullException("assetClient");
			ModAssets.InitializeSharedStorage();
			Instance = new PrtsResourceService(assetClient);
		}

		public static void Shutdown() {
			if (Instance == null)
				return;
			Instance.Dispose();
			Instance = null;
		}

		public bool TryGetOffline(PrtsAssetRequest request, out string localPath) {
			ThrowIfDisposed();
			if (request == null)
				throw new ArgumentNullException("request");
			string destination = ResolveDestinationPath(request.RelativePath);
			ResourceIndexEntry entry;
			bool hasIndexEntry = index.TryGet(request.Key, out entry);
			if (hasIndexEntry && File.Exists(destination) &&
				IsCachedFileUsable(request, destination, entry)) {
				index.Touch(request.Key);
				localPath = destination;
				return true;
			}
			if (!hasIndexEntry && File.Exists(destination) && IsCachedFileUsable(request, destination, null)) {
				FileInfo file = new FileInfo(destination);
				index.Upsert(CreateEntry(request, file.Length, ComputeSha256(destination)));
				localPath = destination;
				return true;
			}
			localPath = null;
			return false;
		}

		public Task<string> GetOrDownloadAsync(
			PrtsAssetRequest request,
			CancellationToken cancellationToken
		) {
			ThrowIfDisposed();
			if (request == null)
				throw new ArgumentNullException("request");
			string flightKey = request.Key + "\n" + request.ResourceVersion;
			SharedDownload shared = null;
			bool created = false;
			lock (downloadGate) {
				inFlightDownloads.TryGetValue(flightKey, out shared);
				if (shared == null) {
					IncrementInFlightNoLock(request.Key);
					try {
						CancellationTokenSource source = new CancellationTokenSource();
						shared = new SharedDownload(source);
						shared.Task = GetOrDownloadCoreAsync(request, source.Token);
						inFlightDownloads[flightKey] = shared;
						created = true;
					} catch {
						DecrementInFlightNoLock(request.Key);
						throw;
					}
				}
				shared.Waiters++;
			}
			if (created)
				ObserveCompletedDownloadAsync(flightKey, request.Key, shared);
			return AwaitSharedAsync(shared, cancellationToken);
		}

		private async void ObserveCompletedDownloadAsync(string flightKey, string resourceKey,
			SharedDownload shared) {
			try {
				await shared.Task.ConfigureAwait(false);
			} catch {
			} finally {
				lock (downloadGate) {
					SharedDownload current;
					if (inFlightDownloads.TryGetValue(flightKey, out current) &&
						object.ReferenceEquals(current, shared))
						inFlightDownloads.Remove(flightKey);
					shared.CompletionObserved = true;
					DecrementInFlightNoLock(resourceKey);
					if (shared.Waiters == 0)
						shared.DisposeCancellation();
				}
			}
		}

		private void IncrementInFlightNoLock(string resourceKey) {
			int count;
			inFlightResourceKeys.TryGetValue(resourceKey, out count);
			inFlightResourceKeys[resourceKey] = count + 1;
		}

		private void DecrementInFlightNoLock(string resourceKey) {
			int count;
			if (!inFlightResourceKeys.TryGetValue(resourceKey, out count)) return;
			if (count <= 1) inFlightResourceKeys.Remove(resourceKey);
			else inFlightResourceKeys[resourceKey] = count - 1;
		}

		private async Task<string> AwaitSharedAsync(
			SharedDownload shared,
			CancellationToken cancellationToken
		) {
			try {
				if (!cancellationToken.CanBeCanceled)
					return await shared.Task.ConfigureAwait(false);
				cancellationToken.ThrowIfCancellationRequested();
				TaskCompletionSource<bool> canceled = new TaskCompletionSource<bool>();
				using (cancellationToken.Register(() => canceled.TrySetResult(true))) {
					Task completed = await Task.WhenAny(shared.Task, canceled.Task).ConfigureAwait(false);
					if (!object.ReferenceEquals(completed, shared.Task))
						throw new OperationCanceledException(cancellationToken);
				}
				return await shared.Task.ConfigureAwait(false);
			} finally {
				ReleaseDownloadWaiter(shared);
			}
		}

		private void ReleaseDownloadWaiter(SharedDownload shared) {
			lock (downloadGate) {
				if (shared.Waiters > 0)
					shared.Waiters--;
				if (shared.Waiters != 0)
					return;
				if (!shared.CompletionObserved && !shared.Task.IsCompleted)
					shared.Cancellation.Cancel();
				if (shared.CompletionObserved)
					shared.DisposeCancellation();
			}
		}

		private async Task<string> GetOrDownloadCoreAsync(
			PrtsAssetRequest request,
			CancellationToken cancellationToken
		) {
			string cachedPath;
			bool hasOfflineCopy = TryGetOffline(request, out cachedPath);
			ResourceIndexEntry cachedEntry;
			bool currentVersion = hasOfflineCopy && index.TryGet(request.Key, out cachedEntry) &&
				string.Equals(cachedEntry.ResourceVersion, request.ResourceVersion, StringComparison.Ordinal);
			if (currentVersion)
				return cachedPath;

			string destination = ResolveDestinationPath(request.RelativePath);
			string partPath = Path.Combine(ModAssets.TempRoot, Guid.NewGuid().ToString("N") + ".part");
			try {
				PrtsDownloadResult downloaded = await client.DownloadAsync(
					request,
					partPath,
					cancellationToken
				).ConfigureAwait(false);
				Directory.CreateDirectory(Path.GetDirectoryName(destination));
				AtomicFile.Replace(partPath, destination);
				index.Upsert(CreateEntry(
					request,
					downloaded.Length,
					downloaded.Sha256,
					downloaded.SourceUri,
					downloaded.ResourceVersion
				));
				ApplyCachePolicy(request.Key);
				return destination;
			} catch (OperationCanceledException) {
				if (File.Exists(partPath))
					File.Delete(partPath);
				throw;
			} catch (Exception error) {
				if (File.Exists(partPath))
					File.Delete(partPath);
				if (hasOfflineCopy) {
					Debug.LogWarning(
						"[ArknightsOperatorsMod] PRTS refresh failed; using the cached copy: " + error.Message
					);
					return cachedPath;
				}
				throw;
			}
		}

		public int ClearDownloadedResources() {
			ThrowIfDisposed();
			return index.Clear();
		}

		internal string CommitVerifiedFile(
			PrtsAssetRequest request,
			string stagedPath,
			Uri sourceUri,
			string resourceVersion
		) {
			ThrowIfDisposed();
			if (request == null) throw new ArgumentNullException("request");
			if (string.IsNullOrEmpty(stagedPath) || !File.Exists(stagedPath))
				throw new FileNotFoundException("Verified staged asset is missing", stagedPath);
			if (!IsCachedFileUsable(request, stagedPath, null))
				throw new InvalidDataException("Staged fallback asset does not match its manifest");

			string destination = ResolveDestinationPath(request.RelativePath);
			long stagedLength = new FileInfo(stagedPath).Length;
			string sha256 = ComputeSha256(stagedPath);
			Directory.CreateDirectory(Path.GetDirectoryName(destination));
			AtomicFile.Replace(stagedPath, destination);
			index.Upsert(CreateEntry(
				request,
				stagedLength,
				sha256,
				sourceUri,
				resourceVersion
			));
			ApplyCachePolicy(request.Key);
			return destination;
		}

		public long RunCacheMaintenance() {
			ThrowIfDisposed();
			ModConfig config = ModConfigStore.Current;
			if (config.DownloadPolicy == ResourcePersistencePolicy.Permanent)
				return index.GetIndexedDiskUsage();
			return TrimLeastRecentlyUsed(CacheLimitBytesForMiB(config.CacheCapacityMiB), null);
		}

		internal static long CacheLimitBytesForMiB(int capacityMiB) {
			return ModConfig.CacheCapacityBytes(capacityMiB);
		}

		public IDisposable Acquire(IEnumerable<string> keys) {
			ThrowIfDisposed();
			if (keys == null) throw new ArgumentNullException("keys");
			List<string> acquired = new List<string>();
			lock (activeGate) {
				foreach (string key in keys) {
					if (string.IsNullOrEmpty(key) || acquired.Contains(key)) continue;
					int count;
					activeKeys.TryGetValue(key, out count);
					activeKeys[key] = count + 1;
					acquired.Add(key);
				}
			}
			return new ResourceLease(this, acquired);
		}

		private void ApplyCachePolicy(string protectedKey) {
			ModConfig config = ModConfigStore.Current;
			if (config.DownloadPolicy == ResourcePersistencePolicy.Permanent)
				return;
			TrimLeastRecentlyUsed(CacheLimitBytesForMiB(config.CacheCapacityMiB), protectedKey);
		}

		private long TrimLeastRecentlyUsed(long limitBytes, string extraKey) {
			lock (downloadGate) {
				lock (activeGate) {
					HashSet<string> protectedKeys = new HashSet<string>(StringComparer.Ordinal);
					foreach (string key in inFlightResourceKeys.Keys) protectedKeys.Add(key);
					foreach (string key in activeKeys.Keys) protectedKeys.Add(key);
					if (!string.IsNullOrEmpty(extraKey)) protectedKeys.Add(extraKey);
					return index.TrimLeastRecentlyUsed(limitBytes, protectedKeys);
				}
			}
		}

		private void Release(IList<string> keys) {
			lock (activeGate) {
				for (int i = 0; i < keys.Count; i++) {
					int count;
					if (!activeKeys.TryGetValue(keys[i], out count)) continue;
					if (count <= 1) activeKeys.Remove(keys[i]);
					else activeKeys[keys[i]] = count - 1;
				}
			}
			if (!disposed) {
				try {
					ApplyCachePolicy(null);
				} catch (Exception error) {
					Debug.LogWarning("[ArknightsOperatorsMod] Cache maintenance after releasing resources failed: " +
						error.Message);
				}
			}
		}

		private static ResourceIndexEntry CreateEntry(
			PrtsAssetRequest request,
			long length,
			string sha256,
			Uri sourceUri = null,
			string resourceVersion = null
		) {
			return new ResourceIndexEntry {
				Key = request.Key,
				RelativePath = NormalizeRelativePath(request.RelativePath),
				SourceUrl = (sourceUri ?? request.SourceUri).AbsoluteUri,
				ResourceVersion = string.IsNullOrEmpty(resourceVersion)
					? request.ResourceVersion
					: resourceVersion,
				Length = length,
				Sha256 = sha256,
				LastAccessUtc = System.DateTime.UtcNow
			};
		}

		private static bool IsCachedFileUsable(
			PrtsAssetRequest request,
			string path,
			ResourceIndexEntry entry
		) {
			FileInfo file = new FileInfo(path);
			string actualHash = null;
			bool hasManifestHash = false;
			for (int i = 0; i < request.Sources.Count; i++) {
				PrtsAssetSource source = request.Sources[i];
				if (string.IsNullOrEmpty(source.ExpectedSha256))
					continue;
				hasManifestHash = true;
				if (source.ExpectedLength.HasValue && file.Length != source.ExpectedLength.Value)
					continue;
				if (actualHash == null)
					actualHash = ComputeSha256(path);
				if (string.Equals(actualHash, source.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			if (hasManifestHash)
				return false;
			long expectedLength = entry == null ? -1L : entry.Length;
			if (expectedLength >= 0L && file.Length != expectedLength)
				return false;
			string expectedHash = entry == null ? string.Empty : entry.Sha256;
			return string.IsNullOrEmpty(expectedHash) || string.Equals(
				ComputeSha256(path), expectedHash, StringComparison.OrdinalIgnoreCase
			);
		}

		private static string ResolveDestinationPath(string relativePath) {
			string normalized = NormalizeRelativePath(relativePath);
			string root = Path.GetFullPath(ModAssets.SharedAssetsRoot)
				.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string destination = Path.GetFullPath(Path.Combine(root, normalized));
			if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Asset path escapes the shared cache directory");
			return destination;
		}

		private static string NormalizeRelativePath(string relativePath) {
			if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
				throw new InvalidOperationException("Asset path must be relative");
			return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		}

		private static string ComputeSha256(string path) {
			using (SHA256 sha = SHA256.Create())
			using (FileStream stream = File.OpenRead(path))
				return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
		}

		private void ThrowIfDisposed() {
			if (disposed)
				throw new ObjectDisposedException("PrtsResourceService");
		}

		public void Dispose() {
			if (disposed)
				return;
			disposed = true;
			SharedDownload[] downloads;
			lock (downloadGate) {
				downloads = new SharedDownload[inFlightDownloads.Count];
				inFlightDownloads.Values.CopyTo(downloads, 0);
			}
			for (int i = 0; i < downloads.Length; i++)
				downloads[i].Cancellation.Cancel();
			client.Dispose();
		}

		private sealed class SharedDownload {
			public readonly CancellationTokenSource Cancellation;
			public Task<string> Task;
			public int Waiters;
			public bool CompletionObserved;
			private bool cancellationDisposed;

			public SharedDownload(CancellationTokenSource cancellation) {
				Cancellation = cancellation;
			}

			public void DisposeCancellation() {
				if (cancellationDisposed) return;
				cancellationDisposed = true;
				Cancellation.Dispose();
			}
		}

		private sealed class ResourceLease : IDisposable {
			private PrtsResourceService owner;
			private readonly IList<string> keys;

			public ResourceLease(PrtsResourceService owner, IList<string> keys) {
				this.owner = owner;
				this.keys = keys;
			}

			public void Dispose() {
				PrtsResourceService current = Interlocked.Exchange(ref owner, null);
				if (current == null) return;
				current.Release(keys);
			}
		}
	}
}
