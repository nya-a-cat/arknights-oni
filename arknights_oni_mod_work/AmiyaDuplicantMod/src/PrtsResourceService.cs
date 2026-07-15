using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AmiyaDuplicantMod {
	public sealed class PrtsResourceService : IDisposable {
		public const long OnDemandCacheLimitBytes = 512L * 1024L * 1024L;

		private readonly ResourceIndexStore index;
		private readonly PrtsAssetClient client;
		private readonly object downloadGate = new object();
		private readonly Dictionary<string, Task<string>> inFlightDownloads =
			new Dictionary<string, Task<string>>(StringComparer.Ordinal);
		private readonly object activeGate = new object();
		private readonly Dictionary<string, int> activeKeys = new Dictionary<string, int>(StringComparer.Ordinal);
		private bool disposed;

		public static PrtsResourceService Instance { get; private set; }

		public long IndexedDiskUsage {
			get { return index.GetIndexedDiskUsage(); }
		}

		private PrtsResourceService() {
			index = new ResourceIndexStore(ModAssets.CacheIndexPath, ModAssets.SharedAssetsRoot);
			client = new PrtsAssetClient();
		}

		public static void Initialize() {
			if (Instance != null)
				return;
			ModAssets.InitializeSharedStorage();
			Instance = new PrtsResourceService();
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
			Task<string> shared = null;
			bool created = false;
			lock (downloadGate) {
				if (inFlightDownloads.TryGetValue(flightKey, out shared) && shared.IsCompleted) {
					inFlightDownloads.Remove(flightKey);
					shared = null;
				}
				if (shared == null) {
					shared = GetOrDownloadCoreAsync(request, CancellationToken.None);
					inFlightDownloads[flightKey] = shared;
					created = true;
				}
			}
			if (created)
				ObserveCompletedDownloadAsync(flightKey, shared);
			return AwaitSharedAsync(shared, cancellationToken);
		}

		private async void ObserveCompletedDownloadAsync(string flightKey, Task<string> shared) {
			try {
				await shared.ConfigureAwait(false);
			} catch {
			} finally {
				lock (downloadGate) {
					Task<string> current;
					if (inFlightDownloads.TryGetValue(flightKey, out current) &&
						object.ReferenceEquals(current, shared))
						inFlightDownloads.Remove(flightKey);
				}
			}
		}

		private static async Task<string> AwaitSharedAsync(
			Task<string> shared,
			CancellationToken cancellationToken
		) {
			if (!cancellationToken.CanBeCanceled)
				return await shared.ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			TaskCompletionSource<bool> canceled = new TaskCompletionSource<bool>();
			using (cancellationToken.Register(() => canceled.TrySetResult(true))) {
				Task completed = await Task.WhenAny(shared, canceled.Task).ConfigureAwait(false);
				if (!object.ReferenceEquals(completed, shared))
					throw new OperationCanceledException(cancellationToken);
			}
			return await shared.ConfigureAwait(false);
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
				index.Upsert(CreateEntry(request, downloaded.Length, downloaded.Sha256));
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
						"[AmiyaDuplicantMod] PRTS refresh failed; using the cached copy: " + error.Message
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

		public long RunCacheMaintenance() {
			ThrowIfDisposed();
			if (ModConfigStore.DownloadPolicy == ResourcePersistencePolicy.Permanent)
				return index.GetIndexedDiskUsage();
			return index.TrimLeastRecentlyUsed(OnDemandCacheLimitBytes, SnapshotProtectedKeys(null));
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
			if (ModConfigStore.DownloadPolicy == ResourcePersistencePolicy.Permanent)
				return;
			HashSet<string> protectedKeys = SnapshotProtectedKeys(protectedKey);
			index.TrimLeastRecentlyUsed(OnDemandCacheLimitBytes, protectedKeys);
		}

		private HashSet<string> SnapshotProtectedKeys(string extraKey) {
			HashSet<string> protectedKeys = new HashSet<string>(StringComparer.Ordinal);
			lock (activeGate) {
				foreach (string key in activeKeys.Keys) protectedKeys.Add(key);
			}
			if (!string.IsNullOrEmpty(extraKey)) protectedKeys.Add(extraKey);
			return protectedKeys;
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
		}

		private static ResourceIndexEntry CreateEntry(
			PrtsAssetRequest request,
			long length,
			string sha256
		) {
			return new ResourceIndexEntry {
				Key = request.Key,
				RelativePath = NormalizeRelativePath(request.RelativePath),
				SourceUrl = request.SourceUri.AbsoluteUri,
				ResourceVersion = request.ResourceVersion,
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
			long expectedLength = request.ExpectedLength ?? (entry == null ? -1L : entry.Length);
			if (expectedLength >= 0L && file.Length != expectedLength)
				return false;
			string expectedHash = !string.IsNullOrEmpty(request.ExpectedSha256)
				? request.ExpectedSha256
				: entry == null ? string.Empty : entry.Sha256;
			return string.IsNullOrEmpty(expectedHash) || string.Equals(
				ComputeSha256(path),
				expectedHash,
				StringComparison.OrdinalIgnoreCase
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
			client.Dispose();
		}

		private sealed class ResourceLease : IDisposable {
			private PrtsResourceService owner;
			private readonly IList<string> keys;

			public ResourceLease(PrtsResourceService owner, IList<string> keys) {
				this.owner = owner;
				this.keys = keys;
			}

			public void Dispose() {
				PrtsResourceService current = owner;
				if (current == null) return;
				owner = null;
				current.Release(keys);
			}
		}
	}
}
