using HarmonyLib;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;
using UnityEngine;

namespace AmiyaDuplicantMod {
	public sealed class Mod : KMod.UserMod2 {
		public override void OnLoad(Harmony harmony) {
			base.OnLoad(harmony);
			ModAssets.ModPath = path;
			PUtil.InitLibrary();
			new POptions().RegisterOptions(this, typeof(ModConfig));
			ModConfigStore.Initialize();
			PrtsResourceService.Initialize();
			long cacheBytes = PrtsResourceService.Instance.RunCacheMaintenance();
			Debug.Log("[AmiyaDuplicantMod] Loaded from " + ModAssets.ModPath);
			Debug.Log("[AmiyaDuplicantMod] Shared assets: " + ModAssets.SharedAssetsRoot);
			Debug.Log("[AmiyaDuplicantMod] Resource policy=" + ModConfigStore.DownloadPolicy +
				" indexedBytes=" + cacheBytes);
		}
	}

	[HarmonyPatch(typeof(MinionIdentity), "OnSpawn")]
	public static class MinionIdentityOnSpawnPatch {
		public static void Postfix(MinionIdentity __instance) {
			if (__instance == null || __instance.gameObject == null) return;
			if (__instance.gameObject.GetComponent<AmiyaDuplicantOverlay>() != null) return;
			__instance.gameObject.AddComponent<AmiyaDuplicantOverlay>();
		}
	}

	[HarmonyPatch(typeof(Game), "OnPrefabInit")]
	public static class GameOnPrefabInitPatch {
		public static void Postfix(Game __instance) {
			if (__instance == null || __instance.gameObject == null) return;
			if (__instance.gameObject.GetComponent<AppearanceOptionsHotkey>() != null) return;
			__instance.gameObject.AddComponent<AppearanceOptionsHotkey>();
		}
	}
}
