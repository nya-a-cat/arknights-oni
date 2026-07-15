using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AmiyaDuplicantMod;
using Spine;

namespace UnityEngine {
	public static class Debug {
		public static void LogWarning(object value) { Console.Error.WriteLine(value); }
	}
}

namespace AmiyaDuplicantMod {
	public enum ResourcePersistencePolicy { OnDemandCache, Permanent }

	public sealed class ModConfig {
		public string DefaultCharacterId { get; set; }
		public string PreferredSkin { get; set; }
		public string PreferredModel { get; set; }
	}

	public static class ModConfigStore {
		public static ResourcePersistencePolicy DownloadPolicy {
			get { return ResourcePersistencePolicy.OnDemandCache; }
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

internal static class OperatorAssetResolverIntegrationTests {
	private sealed class FileCheckingTextureLoader : TextureLoader {
		public readonly List<string> Paths = new List<string>();

		public void Load(AtlasPage page, string path) {
			if (!File.Exists(path)) throw new FileNotFoundException("Missing atlas page", path);
			Paths.Add(path);
		}

		public void Unload(object texture) {
		}
	}

	private static void Require(bool condition, string message) {
		if (!condition) throw new InvalidOperationException(message);
	}

	private static string ComputeFileSha256(string path) {
		using (SHA256 sha = SHA256.Create())
		using (FileStream stream = File.OpenRead(path))
			return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected an isolated cache directory");
		ModAssets.SharedRoot = Path.GetFullPath(args[0]);
		PrtsResourceService.Initialize();
		try {
			ModConfig config = new ModConfig {
				DefaultCharacterId = "char_002_amiya",
				PreferredSkin = "默认",
				PreferredModel = "基建"
			};
			OperatorAssetResolver resolver = new OperatorAssetResolver(PrtsResourceService.Instance);
			Task<OperatorAssetBundle>[] coldLoads = new Task<OperatorAssetBundle>[4];
			for (int i = 0; i < coldLoads.Length; i++)
				coldLoads[i] = resolver.ResolveAsync(config, CancellationToken.None);
			CancellationTokenSource canceledSource = new CancellationTokenSource();
			Task<OperatorAssetBundle> canceledLoad = resolver.ResolveAsync(config, canceledSource.Token);
			canceledSource.Cancel();
			bool canceled = false;
			try {
				canceledLoad.GetAwaiter().GetResult();
			} catch (OperationCanceledException) {
				canceled = true;
			} finally {
				canceledSource.Dispose();
			}
			Require(canceled, "a canceled caller kept waiting for the shared download");
			Task.WaitAll(coldLoads);
			OperatorAssetBundle bundle = coldLoads[0].Result;
			for (int i = 1; i < coldLoads.Length; i++) {
				Require(coldLoads[i].Result.ResourceVersion == bundle.ResourceVersion,
					"parallel cold load version mismatch");
				Require(coldLoads[i].Result.SkeletonPath == bundle.SkeletonPath,
					"parallel cold load path mismatch");
			}
			Require(bundle.CharacterId == "char_002_amiya", "character ID mismatch");
			Require(bundle.Skin == "默认", "skin selection mismatch");
			Require(bundle.Model == "基建", "model selection mismatch");
			Require(File.Exists(bundle.AtlasPath), "atlas not cached");
			Require(File.Exists(bundle.SkeletonPath), "skeleton not cached");
			Require(bundle.TexturePaths.Count >= 1, "atlas pages not cached");

			FileCheckingTextureLoader textureLoader = new FileCheckingTextureLoader();
			Atlas atlas = new Atlas(bundle.AtlasPath, textureLoader);
			SkeletonData data;
			using (FileStream stream = File.OpenRead(bundle.SkeletonPath))
				data = new SkeletonBinary(new AtlasAttachmentLoader(atlas)).ReadSkeletonData(stream);
			Require(data != null && data.Bones.Count > 0, "empty bones");
			Require(data.Slots.Count > 0, "empty slots");
			Require(data.Animations.Count > 0, "empty animations");
			Require(textureLoader.Paths.Count == bundle.TexturePaths.Count, "atlas page count mismatch");

			long firstUsage = PrtsResourceService.Instance.IndexedDiskUsage;
			OperatorAssetBundle cached = resolver.ResolveAsync(config, CancellationToken.None).GetAwaiter().GetResult();
			Require(cached.ResourceVersion == bundle.ResourceVersion, "cache version mismatch");
			Require(PrtsResourceService.Instance.IndexedDiskUsage == firstUsage, "cache reuse changed disk usage");

			string expectedSkeletonHash = ComputeFileSha256(cached.SkeletonPath);
			byte[] corruptSkeleton = File.ReadAllBytes(cached.SkeletonPath);
			corruptSkeleton[0] ^= 0xff;
			File.WriteAllBytes(cached.SkeletonPath, corruptSkeleton);
			OperatorAssetBundle repaired = resolver.ResolveAsync(config, CancellationToken.None).
				GetAwaiter().GetResult();
			Require(ComputeFileSha256(repaired.SkeletonPath) == expectedSkeletonHash,
				"same-length cache corruption was not repaired");

			config.PreferredSkin = "播种者";
			config.PreferredModel = "正面";
			OperatorAssetBundle alternate = resolver.ResolveAsync(config, CancellationToken.None).
				GetAwaiter().GetResult();
			Require(alternate.CharacterId == "char_002_amiya", "alternate character mismatch");
			Require(alternate.Skin == "播种者", "alternate skin selection mismatch");
			Require(alternate.Model == "正面", "alternate model selection mismatch");
			Require(File.Exists(alternate.AtlasPath), "alternate atlas not cached");
			Require(File.Exists(alternate.SkeletonPath), "alternate skeleton not cached");
			Require(alternate.AtlasPath != bundle.AtlasPath,
				"alternate appearance reused the default atlas path");
			Console.WriteLine("OperatorAssetResolverIntegrationTests: passed " +
				bundle.CharacterName + " " + bundle.Skin + "/" + bundle.Model +
				" and " + alternate.Skin + "/" + alternate.Model +
				" spine=" + data.Version + " bones=" + data.Bones.Count +
				" slots=" + data.Slots.Count + " animations=" + data.Animations.Count +
				" pages=" + bundle.TexturePaths.Count + " bytes=" + firstUsage);
			return 0;
		} finally {
			PrtsResourceService.Shutdown();
		}
	}
}
