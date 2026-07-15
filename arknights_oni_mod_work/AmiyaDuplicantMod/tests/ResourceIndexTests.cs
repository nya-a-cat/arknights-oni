using System;
using System.Collections.Generic;
using System.IO;
using AmiyaDuplicantMod;

namespace UnityEngine {
	public static class Debug {
		public static void LogWarning(object value) { Console.Error.WriteLine(value); }
	}
}

internal static class ResourceIndexTests {
	private static int assertions;

	private static void Require(bool condition, string message) {
		assertions++;
		if (!condition) throw new InvalidOperationException(message);
	}

	private static ResourceIndexEntry Entry(string key, string relativePath, long length, DateTime access) {
		return new ResourceIndexEntry {
			Key = key,
			RelativePath = relativePath,
			SourceUrl = "https://torappu.prts.wiki/" + key,
			ResourceVersion = "v1",
			Length = length,
			Sha256 = string.Empty,
			LastAccessUtc = access
		};
	}

	private static void WriteAsset(string assets, string relativePath, int bytes) {
		string path = Path.Combine(assets, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllBytes(path, new byte[bytes]);
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected an isolated test directory");
		string root = Path.GetFullPath(args[0]);
		string assets = Path.Combine(root, "assets");
		string indexPath = Path.Combine(root, "cache-index.json");
		Directory.CreateDirectory(assets);
		DateTime now = DateTime.UtcNow;

		WriteAsset(assets, "old.bin", 3);
		WriteAsset(assets, "middle.bin", 4);
		WriteAsset(assets, "new.bin", 5);
		ResourceIndexStore store = new ResourceIndexStore(indexPath, assets);
		store.Upsert(Entry("old", "old.bin", 3, now.AddMinutes(-3)));
		store.Upsert(Entry("middle", "middle.bin", 4, now.AddMinutes(-2)));
		store.Upsert(Entry("new", "new.bin", 5, now.AddMinutes(-1)));
		Require(store.GetIndexedDiskUsage() == 12, "indexed usage mismatch");
		Require(store.TrimLeastRecentlyUsed(9, null) == 9, "LRU trim did not reach the limit");
		Require(!File.Exists(Path.Combine(assets, "old.bin")), "oldest entry was not removed first");
		Require(File.Exists(Path.Combine(assets, "middle.bin")) && File.Exists(Path.Combine(assets, "new.bin")), "newer entries were removed unexpectedly");

		WriteAsset(assets, "protected.bin", 6);
		store.Upsert(Entry("protected", "protected.bin", 6, now.AddMinutes(-10)));
		HashSet<string> protectedKeys = new HashSet<string>(StringComparer.Ordinal) { "protected" };
		store.TrimLeastRecentlyUsed(6, protectedKeys);
		Require(File.Exists(Path.Combine(assets, "protected.bin")), "protected resource was removed");

		WriteAsset(assets, "version-v1.bin", 2);
		store.Upsert(Entry("versioned", "version-v1.bin", 2, now));
		WriteAsset(assets, "version-v2.bin", 3);
		store.Upsert(Entry("versioned", "version-v2.bin", 3, now.AddSeconds(1)));
		Require(!File.Exists(Path.Combine(assets, "version-v1.bin")), "obsolete version path was not removed");
		Require(File.Exists(Path.Combine(assets, "version-v2.bin")), "replacement version is missing");

		string outside = Path.Combine(root, "outside.bin");
		File.WriteAllBytes(outside, new byte[] { 1 });
		store.Upsert(Entry("escape", ".." + Path.DirectorySeparatorChar + "outside.bin", 1, now));
		store.Clear();
		Require(File.Exists(outside), "path escape entry deleted a file outside the cache root");

		Console.WriteLine("ResourceIndexTests: " + assertions + " passed");
		return 0;
	}
}
