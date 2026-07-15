using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AmiyaDuplicantMod {
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
				if (!string.Equals(item.Name, trimmed, StringComparison.OrdinalIgnoreCase)) continue;
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
				if (needle == null || item.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
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
			for (int i = 0; i < Skins.Count; i++) Skins[i].Validate(Id);
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
