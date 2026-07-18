using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace ArknightsOperatorsMod {
	public sealed class OperatorAppearanceCatalog {
		[JsonProperty("schema_version")]
		public int SchemaVersion { get; private set; }

		[JsonProperty("operators")]
		public List<OperatorAppearanceDefinition> Operators { get; private set; }

		public static OperatorAppearanceCatalog Load(string path) {
			if (!File.Exists(path))
				throw new FileNotFoundException("Missing operator appearance catalog", path);
			return FromJson(File.ReadAllText(path));
		}

		public static OperatorAppearanceCatalog FromJson(string json) {
			OperatorAppearanceCatalog catalog = JsonConvert.DeserializeObject<OperatorAppearanceCatalog>(json);
			if (catalog == null || catalog.SchemaVersion != 1 || catalog.Operators == null ||
				catalog.Operators.Count == 0)
				throw new InvalidDataException("Operator appearance catalog is empty or unsupported");
			for (int i = 0; i < catalog.Operators.Count; i++)
				catalog.Operators[i].Validate();
			catalog.Operators.Sort(OperatorAppearanceDefinition.CompareByDisplayName);
			return catalog;
		}

		public OperatorAppearanceDefinition FindById(string characterId) {
			if (string.IsNullOrWhiteSpace(characterId)) return null;
			for (int i = 0; i < Operators.Count; i++) {
				OperatorAppearanceDefinition item = Operators[i];
				if (string.Equals(item.Id, characterId, StringComparison.OrdinalIgnoreCase))
					return item;
			}
			return null;
		}

		public OperatorAppearanceDefinition FindExact(string query) {
			if (string.IsNullOrWhiteSpace(query)) return null;
			string trimmed = query.Trim();
			OperatorAppearanceDefinition idMatch = FindById(trimmed);
			if (idMatch != null) return idMatch;

			OperatorAppearanceDefinition nameMatch = null;
			for (int i = 0; i < Operators.Count; i++) {
				OperatorAppearanceDefinition item = Operators[i];
				if (!item.MatchesName(trimmed, true)) continue;
				if (nameMatch != null) return null;
				nameMatch = item;
			}
			return nameMatch;
		}

		public IList<OperatorAppearanceDefinition> Search(string query, int limit) {
			int safeLimit = Math.Max(1, limit);
			List<OperatorAppearanceDefinition> results = new List<OperatorAppearanceDefinition>(safeLimit);
			string needle = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
			for (int i = 0; i < Operators.Count && results.Count < safeLimit; i++) {
				OperatorAppearanceDefinition item = Operators[i];
				if (needle == null || item.MatchesName(needle, false) ||
					item.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
					results.Add(item);
			}
			return results;
		}

		public OperatorAppearanceSelection Normalize(string characterId, string skinName, string modelName) {
			OperatorAppearanceDefinition character = FindById(characterId) ??
				FindById("char_002_amiya") ?? Operators[0];
			OperatorSkinDefinition skin = character.FindSkin(skinName) ??
				character.FindSkin("默认") ?? character.Skins[0];
			string model = skin.FindModel(modelName) ?? skin.FindModel("基建") ??
				skin.FindModel("正面") ?? skin.FindModel("战斗") ?? skin.FindModel("背面") ??
				skin.Models[0];
			return new OperatorAppearanceSelection(character, skin, model);
		}
	}

	public sealed class OperatorAppearanceDefinition {
		[JsonProperty("id")]
		public string Id { get; private set; }

		[JsonProperty("name")]
		public string Name { get; private set; }

		[JsonProperty("english_name")]
		public string EnglishName { get; private set; }

		[JsonProperty("japanese_name")]
		public string JapaneseName { get; private set; }

		[JsonProperty("aliases")]
		public List<string> Aliases { get; private set; }

		[JsonProperty("thumbnail_url", NullValueHandling = NullValueHandling.Ignore)]
		public string ThumbnailUrl { get; private set; }

		[JsonProperty("skins")]
		public List<OperatorSkinDefinition> Skins { get; private set; }

		internal static int CompareByDisplayName(OperatorAppearanceDefinition left,
			OperatorAppearanceDefinition right) {
			int byName = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
			return byName != 0 ? byName : string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
		}

		internal void Validate() {
			if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || Skins == null ||
				Skins.Count == 0)
				throw new InvalidDataException("Operator appearance entry is incomplete");
			if (!string.IsNullOrWhiteSpace(ThumbnailUrl)) {
				Uri thumbnailUri;
				if (!Uri.TryCreate(ThumbnailUrl, UriKind.Absolute, out thumbnailUri) ||
					thumbnailUri.Scheme != Uri.UriSchemeHttps)
					throw new InvalidDataException("Operator thumbnail URL must use HTTPS: " + Id);
			}
			for (int i = 0; i < Skins.Count; i++) Skins[i].Validate(Id);
			if (Aliases == null) Aliases = new List<string>();
			for (int i = 0; i < Aliases.Count; i++) {
				if (string.IsNullOrWhiteSpace(Aliases[i]))
					throw new InvalidDataException("Operator alias is empty: " + Id);
			}
		}

		public bool MatchesName(string query, bool exact) {
			if (Matches(Name, query, exact) || Matches(EnglishName, query, exact) ||
				Matches(JapaneseName, query, exact)) return true;
			for (int i = 0; i < Aliases.Count; i++) {
				if (Matches(Aliases[i], query, exact)) return true;
			}
			return false;
		}

		private static bool Matches(string candidate, string query, bool exact) {
			if (string.IsNullOrWhiteSpace(candidate)) return false;
			return exact ? string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase) :
				candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		public OperatorSkinDefinition FindSkin(string name) {
			if (string.IsNullOrWhiteSpace(name)) return null;
			for (int i = 0; i < Skins.Count; i++) {
				if (string.Equals(Skins[i].Name, name, StringComparison.OrdinalIgnoreCase))
					return Skins[i];
			}
			return null;
		}
	}

	public sealed class OperatorSkinDefinition {
		[JsonProperty("name")]
		public string Name { get; private set; }

		[JsonProperty("models")]
		public List<string> Models { get; private set; }

		internal void Validate(string characterId) {
			if (string.IsNullOrWhiteSpace(Name) || Models == null || Models.Count == 0)
				throw new InvalidDataException("Operator skin entry is incomplete: " + characterId);
			for (int i = 0; i < Models.Count; i++) {
				if (string.IsNullOrWhiteSpace(Models[i]))
					throw new InvalidDataException("Operator model name is empty: " + characterId);
			}
		}

		public string FindModel(string name) {
			if (string.IsNullOrWhiteSpace(name)) return null;
			for (int i = 0; i < Models.Count; i++) {
				if (string.Equals(Models[i], name, StringComparison.OrdinalIgnoreCase))
					return Models[i];
			}
			return null;
		}
	}

	public sealed class OperatorAppearanceSelection {
		public OperatorAppearanceDefinition Character { get; private set; }
		public OperatorSkinDefinition Skin { get; private set; }
		public string Model { get; private set; }

		public OperatorAppearanceSelection(OperatorAppearanceDefinition character,
			OperatorSkinDefinition skin, string model) {
			Character = character;
			Skin = skin;
			Model = model;
		}
	}
}
