using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using PeterHan.PLib.Options;
using UnityEngine;

namespace ArknightsOperatorsMod {
	[JsonConverter(typeof(StringEnumConverter))]
	public enum ResourcePersistencePolicy {
		[EnumMember(Value = "按需缓存（512 MiB）")]
		OnDemandCache = 0,

		[EnumMember(Value = "永久保留已下载资源")]
		Permanent = 1
	}

	[ConfigFile("config.json", true, true)]
	public sealed class ModConfig : IOptions {
		public const int CurrentSchemaVersion = 5;
		public const int MinimumCacheCapacityMiB = 128;
		public const int DefaultCacheCapacityMiB = 512;
		public const int MaximumCacheCapacityMiB = 2000;
		public const int MinimumVisualScalePercent = 75;
		public const int DefaultVisualScalePercent = 125;
		public const int MaximumVisualScalePercent = 200;

		[JsonProperty]
		public int SchemaVersion { get; set; } = CurrentSchemaVersion;

		[JsonProperty]
		public ResourcePersistencePolicy DownloadPolicy { get; set; } =
			ResourcePersistencePolicy.OnDemandCache;

		[JsonProperty]
		public int CacheCapacityMiB { get; set; } = DefaultCacheCapacityMiB;

		[JsonProperty]
		public string DefaultCharacterId { get; set; } = "char_002_amiya";

		[JsonProperty]
		public string PreferredSkin { get; set; } = "默认";

		[JsonProperty]
		public string PreferredModel { get; set; } = "基建";

		[JsonProperty]
		public bool AutomaticModelSwitching { get; set; } = true;

		[JsonProperty]
		public int VisualScalePercent { get; set; } = DefaultVisualScalePercent;

		[JsonProperty]
		public Dictionary<string, int> VisualScaleOverrides { get; set; } =
			new Dictionary<string, int>(StringComparer.Ordinal);

		internal bool Normalize() {
			bool changed = false;
			if (SchemaVersion != CurrentSchemaVersion) {
				SchemaVersion = CurrentSchemaVersion;
				changed = true;
			}
			if (!Enum.IsDefined(typeof(ResourcePersistencePolicy), DownloadPolicy)) {
				DownloadPolicy = ResourcePersistencePolicy.OnDemandCache;
				changed = true;
			}
			if (!IsValidCacheCapacityMiB(CacheCapacityMiB)) {
				Debug.LogWarning("[ArknightsOperatorsMod] CacheCapacityMiB=" + CacheCapacityMiB +
					" is outside 128-2000; restored to 512 MiB");
				CacheCapacityMiB = DefaultCacheCapacityMiB;
				changed = true;
			}
			if (!IsValidVisualScalePercent(VisualScalePercent)) {
				Debug.LogWarning("[ArknightsOperatorsMod] VisualScalePercent=" + VisualScalePercent +
					" is outside 75-200; restored to 125 percent");
				VisualScalePercent = DefaultVisualScalePercent;
				changed = true;
			}
			if (VisualScaleOverrides == null) {
				VisualScaleOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
				changed = true;
			} else {
				Dictionary<string, int> validOverrides =
					new Dictionary<string, int>(StringComparer.Ordinal);
				foreach (KeyValuePair<string, int> entry in VisualScaleOverrides) {
					if (!IsValidAppearanceScaleKey(entry.Key) ||
						!IsValidVisualScalePercent(entry.Value)) {
						Debug.LogWarning("[ArknightsOperatorsMod] Ignored invalid visual scale override: " +
							(entry.Key ?? "<null>") + "=" + entry.Value);
						changed = true;
						continue;
					}
					validOverrides[entry.Key] = entry.Value;
				}
				if (validOverrides.Count != VisualScaleOverrides.Count)
					VisualScaleOverrides = validOverrides;
			}
			if (string.IsNullOrWhiteSpace(DefaultCharacterId)) {
				DefaultCharacterId = "char_002_amiya";
				changed = true;
			}
			if (string.IsNullOrWhiteSpace(PreferredSkin)) {
				PreferredSkin = "默认";
				changed = true;
			}
			if (string.IsNullOrWhiteSpace(PreferredModel)) {
				PreferredModel = "基建";
				changed = true;
			}
			return changed;
		}

		internal static bool IsValidCacheCapacityMiB(int capacityMiB) {
			return capacityMiB >= MinimumCacheCapacityMiB &&
				capacityMiB <= MaximumCacheCapacityMiB;
		}

		internal static bool IsValidVisualScalePercent(int scalePercent) {
			return scalePercent >= MinimumVisualScalePercent &&
				scalePercent <= MaximumVisualScalePercent;
		}

		internal static string AppearanceScaleKey(string characterId, string skin, string model) {
			if (string.IsNullOrWhiteSpace(characterId))
				throw new ArgumentException("Character ID is required", "characterId");
			if (string.IsNullOrWhiteSpace(skin))
				throw new ArgumentException("Skin is required", "skin");
			if (string.IsNullOrWhiteSpace(model))
				throw new ArgumentException("Model is required", "model");
			return characterId.Trim().ToLowerInvariant() + "\u001f" + skin.Trim() + "\u001f" +
				model.Trim();
		}

		internal int ResolveVisualScalePercent(string characterId, string skin, string model) {
			int scalePercent;
			string key = AppearanceScaleKey(characterId, skin, model);
			return VisualScaleOverrides != null &&
				VisualScaleOverrides.TryGetValue(key, out scalePercent) ?
				scalePercent : VisualScalePercent;
		}

		private static bool IsValidAppearanceScaleKey(string key) {
			if (string.IsNullOrWhiteSpace(key)) return false;
			string[] parts = key.Split(new[] { '\u001f' }, StringSplitOptions.None);
			return parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[0]) &&
				!string.IsNullOrWhiteSpace(parts[1]) && !string.IsNullOrWhiteSpace(parts[2]);
		}

		internal static long CacheCapacityBytes(int capacityMiB) {
			if (!IsValidCacheCapacityMiB(capacityMiB))
				capacityMiB = DefaultCacheCapacityMiB;
			return capacityMiB * 1024L * 1024L;
		}

		internal static bool IsCacheUsageOverTarget(long indexedBytes,
			ResourcePersistencePolicy policy, int capacityMiB) {
			return policy == ResourcePersistencePolicy.OnDemandCache &&
				indexedBytes > CacheCapacityBytes(capacityMiB);
		}

		internal static bool CanApplyCacheCapacityInput(ResourcePersistencePolicy policy,
			bool inputValid) {
			return policy == ResourcePersistencePolicy.Permanent || inputValid;
		}

		public IEnumerable<IOptionsEntry> CreateOptions() {
			string category = ModLocalization.Text("全局设置", "Global settings");
			yield return new SelectOneOptionsEntry(
				nameof(DownloadPolicy),
				new OptionAttribute(
					ModLocalization.Text("资源保存策略", "Resource retention"),
					ModLocalization.Text(
						"按需缓存会在容量范围内清理旧资源；永久保留会保存已下载资源。",
						"On-demand caching removes old resources within the capacity target; permanent retention keeps downloaded resources."
					),
					category
				),
				typeof(ResourcePersistencePolicy)
			);
			yield return new IntOptionsEntry(
				nameof(CacheCapacityMiB),
				new OptionAttribute(
					ModLocalization.Text("按需缓存容量（MiB）", "On-demand cache capacity (MiB)"),
					ModLocalization.Text(
						"请输入 128 到 2000 之间的整数；永久保留模式会保存此值供以后使用。",
						"Enter an integer from 128 to 2000. Permanent retention preserves the value for later use."
					),
					category
				),
				new LimitAttribute(MinimumCacheCapacityMiB, MaximumCacheCapacityMiB, 1)
			);
			yield return new CheckboxOptionsEntry(
				nameof(AutomaticModelSwitching),
				new OptionAttribute(
					ModLocalization.Text("自动模型切换", "Automatic model switching"),
					ModLocalization.Text(
						"日常状态使用基建模型，挖矿、战斗、眩晕和死亡使用战斗模型。",
						"Use the base model for daily states and the combat model for digging, combat, stun, and death."
					),
					category
				)
			);
			yield return new IntOptionsEntry(
				nameof(VisualScalePercent),
				new OptionAttribute(
					ModLocalization.Text("默认外观大小（%）", "Default appearance size (%)"),
					ModLocalization.Text(
						"100% 是旧版大小，默认 125%，可设置为 75% 到 200%。",
						"100% is the previous size. The default is 125%, configurable from 75% to 200%."
					),
					category
				),
				new LimitAttribute(MinimumVisualScalePercent, MaximumVisualScalePercent, 1)
			);
		}

		public void OnOptionsChanged() {
			Normalize();
			ModConfigStore.SaveAndApply(this);
			Debug.Log("[ArknightsOperatorsMod] Saved global settings; cache=" +
				CacheCapacityMiB + " MiB; scale=" + VisualScalePercent + "%");
		}
	}

	public static class ModConfigStore {
		private static readonly object Gate = new object();
		private static string configPath;
		private static System.DateTime lastWriteUtc;
		private static ModConfig current;

		public static event Action<ModConfig> AppearanceChanged;
		public static event Action<ModConfig> VisualScaleChanged;

		public static string ConfigPath {
			get {
				EnsureInitialized();
				return configPath;
			}
		}

		public static ModConfig Current {
			get {
				lock (Gate) {
					EnsureInitializedNoLock();
					ReloadWhenChangedNoLock();
					return Clone(current);
				}
			}
		}

		public static ResourcePersistencePolicy DownloadPolicy {
			get { return Current.DownloadPolicy; }
		}

		public static int CacheCapacityMiB {
			get { return Current.CacheCapacityMiB; }
		}

		public static void Initialize() {
			lock (Gate) {
				EnsureInitializedNoLock();
			}
		}

		public static void ApplySaved(ModConfig saved) {
			Apply(saved, false);
		}

		public static void SaveAndApply(ModConfig saved) {
			Apply(saved, true);
		}

		private static void Apply(ModConfig saved, bool persist) {
			if (saved == null) throw new ArgumentNullException("saved");
			Action<ModConfig> changed = null;
			ModConfig snapshot = null;
			Action<ModConfig> scaleChanged = null;
			ModConfig scaleSnapshot = null;
			bool cacheSettingsChanged = false;
			lock (Gate) {
				EnsureInitializedNoLock();
				string previousAppearance = AppearanceKey(current);
				ResourcePersistencePolicy previousPolicy = current.DownloadPolicy;
				int previousCapacityMiB = current.CacheCapacityMiB;
				ModConfig previousScaleConfig = Clone(current);
				current = Clone(saved);
				current.Normalize();
				cacheSettingsChanged = previousPolicy != current.DownloadPolicy ||
					previousCapacityMiB != current.CacheCapacityMiB;
				if (persist)
					WriteNoLock(current);
				else
					lastWriteUtc = GetLastWriteUtcNoLock();
				if (!string.Equals(previousAppearance, AppearanceKey(current), StringComparison.Ordinal)) {
					changed = AppearanceChanged;
					snapshot = Clone(current);
				}
				if (!VisualScaleSettingsEqual(previousScaleConfig, current)) {
					scaleChanged = VisualScaleChanged;
					scaleSnapshot = Clone(current);
				}
			}
			if (changed != null) changed(snapshot);
			if (scaleChanged != null) scaleChanged(scaleSnapshot);
			if (cacheSettingsChanged && PrtsResourceService.Instance != null) {
				try {
					PrtsResourceService.Instance.RunCacheMaintenance();
				} catch (Exception error) {
					Debug.LogWarning("[ArknightsOperatorsMod] Cache maintenance after saving settings failed: " +
						error.Message);
				}
			}
		}

		internal static string AppearanceKey(ModConfig config) {
			if (config == null) return "";
			return config.DefaultCharacterId + "|" + config.PreferredSkin + "|" +
				config.PreferredModel + "|" + config.AutomaticModelSwitching;
		}

		internal static ModConfig Clone(ModConfig source) {
			return new ModConfig {
				SchemaVersion = source.SchemaVersion,
				DownloadPolicy = source.DownloadPolicy,
				CacheCapacityMiB = source.CacheCapacityMiB,
				DefaultCharacterId = source.DefaultCharacterId,
				PreferredSkin = source.PreferredSkin,
				PreferredModel = source.PreferredModel,
				AutomaticModelSwitching = source.AutomaticModelSwitching,
				VisualScalePercent = source.VisualScalePercent,
				VisualScaleOverrides = source.VisualScaleOverrides == null ?
					new Dictionary<string, int>(StringComparer.Ordinal) :
					new Dictionary<string, int>(source.VisualScaleOverrides, StringComparer.Ordinal)
			};
		}

		internal static void SetAppearanceVisualScale(string characterId, string skin, string model,
			int scalePercent) {
			if (!ModConfig.IsValidVisualScalePercent(scalePercent))
				throw new ArgumentOutOfRangeException("scalePercent");
			ModConfig next = Current;
			next.VisualScaleOverrides[ModConfig.AppearanceScaleKey(characterId, skin, model)] =
				scalePercent;
			SaveAndApply(next);
		}

		internal static void ResetAppearanceVisualScale(string characterId, string skin, string model) {
			ModConfig next = Current;
			if (!next.VisualScaleOverrides.Remove(
				ModConfig.AppearanceScaleKey(characterId, skin, model))) return;
			SaveAndApply(next);
		}

		private static bool VisualScaleSettingsEqual(ModConfig left, ModConfig right) {
			if (left.VisualScalePercent != right.VisualScalePercent ||
				left.VisualScaleOverrides.Count != right.VisualScaleOverrides.Count) return false;
			foreach (KeyValuePair<string, int> entry in left.VisualScaleOverrides) {
				int rightValue;
				if (!right.VisualScaleOverrides.TryGetValue(entry.Key, out rightValue) ||
					rightValue != entry.Value) return false;
			}
			return true;
		}

		private static void EnsureInitialized() {
			lock (Gate) {
				EnsureInitializedNoLock();
			}
		}

		private static void EnsureInitializedNoLock() {
			if (current != null)
				return;
			configPath = POptions.GetConfigFilePath(typeof(ModConfig));
			current = ReadNoLock();
			if (current == null) {
				current = new ModConfig();
				WriteNoLock(current);
			}
			current.Normalize();
			lastWriteUtc = GetLastWriteUtcNoLock();
		}

		private static void ReloadWhenChangedNoLock() {
			System.DateTime actualWriteUtc = GetLastWriteUtcNoLock();
			if (actualWriteUtc == lastWriteUtc)
				return;
			ModConfig loaded = ReadNoLock();
			if (loaded != null) {
				loaded.Normalize();
				current = loaded;
			} else {
				current = new ModConfig();
				WriteNoLock(current);
			}
			lastWriteUtc = GetLastWriteUtcNoLock();
		}

		private static ModConfig ReadNoLock() {
			try {
				if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
					return null;
				ModConfig loaded = JsonConvert.DeserializeObject<ModConfig>(
					File.ReadAllText(configPath)
				);
				if (loaded != null && loaded.Normalize())
					WriteNoLock(loaded);
				return loaded;
			} catch (Exception error) {
				Debug.LogWarning("[ArknightsOperatorsMod] Failed to read config: " + error.Message);
				return null;
			}
		}

		private static void WriteNoLock(ModConfig config) {
			config.Normalize();
			string directory = Path.GetDirectoryName(configPath);
			Directory.CreateDirectory(directory);
			string partPath = configPath + ".part";
			File.WriteAllText(partPath, JsonConvert.SerializeObject(config, Formatting.Indented));
			AtomicFile.Replace(partPath, configPath);
			lastWriteUtc = GetLastWriteUtcNoLock();
		}

		private static System.DateTime GetLastWriteUtcNoLock() {
			return File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : System.DateTime.MinValue;
		}
	}
}
