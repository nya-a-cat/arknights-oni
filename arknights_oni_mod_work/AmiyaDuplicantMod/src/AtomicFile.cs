using System.IO;

namespace AmiyaDuplicantMod {
	internal static class AtomicFile {
		public static void Replace(string partPath, string destinationPath) {
			if (File.Exists(destinationPath))
				File.Replace(partPath, destinationPath, null);
			else
				File.Move(partPath, destinationPath);
		}
	}
}
