using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace AmiyaDuplicantMod {
	public sealed class ResourceIndexEntry {
		[JsonProperty]
		public string Key { get; set; }

		[JsonProperty]
		public string RelativePath { get; set; }

		[JsonProperty]
		public string SourceUrl { get; set; }

		[JsonProperty]
		public string ResourceVersion { get; set; }

		[JsonProperty]
		public long Length { get; set; }

		[JsonProperty]
		public string Sha256 { get; set; }

		[JsonProperty]
		public System.DateTime LastAccessUtc { get; set; }
	}

	public sealed class ResourceCacheIndex {
		public const int CurrentSchemaVersion = 1;

		[JsonProperty]
		public int SchemaVersion { get; set; } = CurrentSchemaVersion;

		[JsonProperty]
		public List<ResourceIndexEntry> Entries { get; set; } = new List<ResourceIndexEntry>();
	}

	public sealed class ResourceIndexStore {
		private readonly object gate = new object();
		private readonly string indexPath;
		private readonly string assetsRoot;
		private ResourceCacheIndex index;
		private Dictionary<string, ResourceIndexEntry> byKey;

		public ResourceIndexStore(string indexPath, string assetsRoot) {
			if (string.IsNullOrEmpty(indexPath))
				throw new ArgumentNullException("indexPath");
			if (string.IsNullOrEmpty(assetsRoot))
				throw new ArgumentNullException("assetsRoot");
			this.indexPath = indexPath;
			this.assetsRoot = Path.GetFullPath(assetsRoot);
			Load();
		}

		public bool TryGet(string key, out ResourceIndexEntry entry) {
			lock (gate) {
				ResourceIndexEntry stored;
				if (!byKey.TryGetValue(key, out stored)) {
					entry = null;
					return false;
				}
				entry = Clone(stored);
				return true;
			}
		}

		public void Upsert(ResourceIndexEntry entry) {
			if (entry == null || string.IsNullOrEmpty(entry.Key))
				throw new ArgumentException("Index entry and key are required", "entry");
			lock (gate) {
				ResourceIndexEntry stored;
				if (byKey.TryGetValue(entry.Key, out stored)) {
					string obsoletePath = null;
					if (!string.Equals(stored.RelativePath, entry.RelativePath, StringComparison.OrdinalIgnoreCase))
						TryResolvePath(stored.RelativePath, out obsoletePath);
					int position = index.Entries.IndexOf(stored);
					ResourceIndexEntry copy = Clone(entry);
					index.Entries[position] = copy;
					byKey[entry.Key] = copy;
					if (!string.IsNullOrEmpty(obsoletePath) && File.Exists(obsoletePath)) File.Delete(obsoletePath);
				} else {
					ResourceIndexEntry copy = Clone(entry);
					index.Entries.Add(copy);
					byKey.Add(copy.Key, copy);
				}
				PersistNoLock();
			}
		}

		public void Touch(string key) {
			lock (gate) {
				ResourceIndexEntry entry;
				if (byKey.TryGetValue(key, out entry)) {
					entry.LastAccessUtc = System.DateTime.UtcNow;
					PersistNoLock();
				}
			}
		}

		public long GetIndexedDiskUsage() {
			lock (gate) {
				long total = 0L;
				foreach (ResourceIndexEntry entry in index.Entries) {
					string fullPath;
					if (TryResolvePath(entry.RelativePath, out fullPath) && File.Exists(fullPath))
						total += new FileInfo(fullPath).Length;
				}
				return total;
			}
		}

		public long TrimLeastRecentlyUsed(long sizeLimitBytes, ISet<string> protectedKeys) {
			if (sizeLimitBytes < 0L)
				throw new ArgumentOutOfRangeException("sizeLimitBytes");
			lock (gate) {
				long total = GetIndexedDiskUsageNoLock();
				if (total <= sizeLimitBytes)
					return total;

				List<ResourceIndexEntry> candidates = index.Entries
					.Where(entry => protectedKeys == null || !protectedKeys.Contains(entry.Key))
					.OrderBy(entry => entry.LastAccessUtc)
					.ToList();

				foreach (ResourceIndexEntry entry in candidates) {
					if (total <= sizeLimitBytes)
						break;
					string fullPath;
					long length = 0L;
					if (TryResolvePath(entry.RelativePath, out fullPath) && File.Exists(fullPath)) {
						length = new FileInfo(fullPath).Length;
						File.Delete(fullPath);
					}
					total -= length;
					index.Entries.Remove(entry);
					byKey.Remove(entry.Key);
				}
				PersistNoLock();
				return Math.Max(0L, total);
			}
		}

		public int Clear() {
			lock (gate) {
				int removed = 0;
				foreach (ResourceIndexEntry entry in index.Entries.ToList()) {
					string fullPath;
					if (TryResolvePath(entry.RelativePath, out fullPath) && File.Exists(fullPath))
						File.Delete(fullPath);
					removed++;
				}
				index.Entries.Clear();
				byKey.Clear();
				PersistNoLock();
				return removed;
			}
		}

		private void Load() {
			lock (gate) {
				try {
					index = File.Exists(indexPath)
						? JsonConvert.DeserializeObject<ResourceCacheIndex>(File.ReadAllText(indexPath))
						: null;
				} catch (Exception error) {
					Debug.LogWarning("[AmiyaDuplicantMod] Failed to read cache index: " + error.Message);
					index = null;
				}
				if (index == null || index.SchemaVersion != ResourceCacheIndex.CurrentSchemaVersion)
					index = new ResourceCacheIndex();
				if (index.Entries == null)
					index.Entries = new List<ResourceIndexEntry>();
				byKey = index.Entries
					.Where(entry => entry != null && !string.IsNullOrEmpty(entry.Key))
					.GroupBy(entry => entry.Key, StringComparer.Ordinal)
					.ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
				index.Entries = byKey.Values.ToList();
			}
		}

		private long GetIndexedDiskUsageNoLock() {
			long total = 0L;
			foreach (ResourceIndexEntry entry in index.Entries) {
				string fullPath;
				if (TryResolvePath(entry.RelativePath, out fullPath) && File.Exists(fullPath))
					total += new FileInfo(fullPath).Length;
			}
			return total;
		}

		private bool TryResolvePath(string relativePath, out string fullPath) {
			fullPath = null;
			if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
				return false;
			string candidate = Path.GetFullPath(Path.Combine(assetsRoot, relativePath));
			string rootPrefix = assetsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
				Path.DirectorySeparatorChar;
			if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
				return false;
			fullPath = candidate;
			return true;
		}

		private void PersistNoLock() {
			Directory.CreateDirectory(Path.GetDirectoryName(indexPath));
			string partPath = indexPath + ".part";
			index.SchemaVersion = ResourceCacheIndex.CurrentSchemaVersion;
			File.WriteAllText(partPath, JsonConvert.SerializeObject(index, Formatting.Indented));
			AtomicFile.Replace(partPath, indexPath);
		}

		private static ResourceIndexEntry Clone(ResourceIndexEntry entry) {
			return new ResourceIndexEntry {
				Key = entry.Key,
				RelativePath = entry.RelativePath,
				SourceUrl = entry.SourceUrl,
				ResourceVersion = entry.ResourceVersion,
				Length = entry.Length,
				Sha256 = entry.Sha256,
				LastAccessUtc = entry.LastAccessUtc
			};
		}
	}
}
