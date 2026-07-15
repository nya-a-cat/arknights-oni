using System.IO;
using PeterHan.PLib.Options;

namespace AmiyaDuplicantMod {
	public static class ModAssets {
		public static string ModPath;
		private static string sharedRoot;

		public static string SharedRoot {
			get {
				if (string.IsNullOrEmpty(sharedRoot))
					sharedRoot = Path.GetDirectoryName(POptions.GetConfigFilePath(typeof(ModConfig)));
				return sharedRoot;
			}
		}

		public static string SharedAssetsRoot {
			get { return Path.Combine(SharedRoot, "assets"); }
		}

		public static string TempRoot {
			get { return Path.Combine(SharedRoot, "tmp"); }
		}

		public static string CacheIndexPath {
			get { return Path.Combine(SharedRoot, "cache-index.json"); }
		}

		public static string OperatorCatalogPath {
			get { return Path.Combine(ModPath, "assets", "catalog", "operator_appearances_20260604.json"); }
		}

		public static string AmiyaAtlasPath {
			get { return Path.Combine(ModPath, "assets", "spine", "amiya", "amiya.atlas"); }
		}

		public static string AmiyaSkeletonPath {
			get { return Path.Combine(ModPath, "assets", "spine", "amiya", "amiya.skel"); }
		}

		public static string AmiyaFrameDir {
			get { return Path.Combine(ModPath, "assets", "frames", "amiya"); }
		}

		public static string AmiyaFrameManifestPath {
			get { return Path.Combine(AmiyaFrameDir, "manifest.json"); }
		}

		public static string AmiyaFrameLibraryIndexPath {
			get { return Path.Combine(AmiyaFrameDir, "library", "index.json"); }
		}

		public static void InitializeSharedStorage() {
			Directory.CreateDirectory(SharedRoot);
			Directory.CreateDirectory(SharedAssetsRoot);
			Directory.CreateDirectory(TempRoot);
		}
	}
}
