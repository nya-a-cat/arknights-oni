using System;
using System.Collections.Generic;
using System.Reflection;
using PeterHan.PLib.Options;
using PeterHan.PLib.UI;
using UnityEngine;

namespace AmiyaDuplicantMod {
	public sealed class OperatorAppearanceOptionsEntry : OptionsEntry {
		private const int MaximumOperatorMatches = 60;
		private static readonly Type ComboComponentType = typeof(PComboBox<Choice>).Assembly.GetType(
			"PeterHan.PLib.UI.PComboBoxComponent"
		);
		private static readonly MethodInfo SetComboItems = ComboComponentType == null ? null :
			ComboComponentType.GetMethod("SetItems", BindingFlags.Public | BindingFlags.Instance);
		private static readonly PropertyInfo ComboContentContainer = ComboComponentType == null ? null :
			ComboComponentType.GetProperty("ContentContainer", BindingFlags.Public |
				BindingFlags.NonPublic | BindingFlags.Instance);

		private readonly OperatorAppearanceCatalog catalog;
		private OperatorAppearanceSelection selection;
		private GameObject operatorComboObject;
		private GameObject skinComboObject;
		private GameObject modelComboObject;
		private string value;

		public override string Name {
			get { return "OperatorAppearance"; }
		}

		public override object Value {
			get { return value; }
			set { this.value = value as string; }
		}

		public OperatorAppearanceOptionsEntry(ModConfig config) : base(null, new OptionAttribute(
			"明日方舟干员外观",
			"选择全局干员、皮肤和模型。保存后会更新当前存档内已有复制人。"
		)) {
			try {
				catalog = OperatorAppearanceCatalog.Load(ModAssets.OperatorCatalogPath);
			} catch (Exception error) {
				Debug.LogWarning("[AmiyaDuplicantMod] Operator catalog load failed: " + error.Message);
				catalog = OperatorAppearanceCatalog.FromJson(
					"{\"schema_version\":1,\"operators\":[{\"id\":\"char_002_amiya\"," +
					"\"name\":\"阿米娅\",\"skins\":[{\"name\":\"默认\",\"models\":[\"基建\"]}]}]}"
				);
			}
			selection = catalog.Normalize(config.DefaultCharacterId, config.PreferredSkin,
				config.PreferredModel);
			value = BuildValue(selection);
		}

		public override void CreateUIEntry(PGridPanel parent, ref int row) {
			PTextField search = new PTextField("OperatorSearch") {
				Text = selection.Character.Name,
				PlaceholderText = "输入中文名称或 char_id",
				ToolTip = "输入完整中文名或 char_id 可直接选中；多个匹配项会显示在下方列表。",
				MaxLength = 80,
				OnTextChanged = OnSearchChanged
			};
			search.SetMinWidthInCharacters(28);
			AddRow(parent, row++, "搜索干员", search, search.ToolTip);

			parent.AddRow(new GridRowSpec());
			List<Choice> operatorChoices = BuildOperatorChoices(selection.Character.Name);
			Choice selectedOperator = FindChoice(operatorChoices, selection.Character.Id);
			PComboBox<Choice> operators = new PComboBox<Choice>("OperatorChoice") {
				Content = operatorChoices,
				InitialItem = selectedOperator,
				MaxRowsShown = 8,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = "先搜索中文名或 char_id，再从匹配结果中选择。",
				OnOptionSelected = OnOperatorSelected
			};
			operators.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				operatorComboObject = realized;
			});
			AddRow(parent, row++, "干员", operators, operators.ToolTip);

			parent.AddRow(new GridRowSpec());
			List<Choice> skinChoices = BuildSkinChoices();
			PComboBox<Choice> skins = new PComboBox<Choice>("SkinChoice") {
				Content = skinChoices,
				InitialItem = FindChoice(skinChoices, selection.Skin.Name),
				MaxRowsShown = 8,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = "皮肤列表随干员联动。",
				OnOptionSelected = OnSkinSelected
			};
			skins.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				skinComboObject = realized;
			});
			AddRow(parent, row++, "皮肤", skins, skins.ToolTip);

			parent.AddRow(new GridRowSpec());
			List<Choice> modelChoices = BuildModelChoices();
			PComboBox<Choice> models = new PComboBox<Choice>("ModelChoice") {
				Content = modelChoices,
				InitialItem = FindChoice(modelChoices, selection.Model),
				MaxRowsShown = 8,
				TextStyle = PUITuning.Fonts.UILightStyle,
				ToolTip = "模型列表随所选皮肤联动；基建模型通常最适合复制人日常动作。",
				OnOptionSelected = OnModelSelected
			};
			models.SetMinWidthInCharacters(28).AddOnRealize(realized => {
				modelComboObject = realized;
			});
			AddRow(parent, row++, "模型", models, models.ToolTip);
		}

		public override GameObject GetUIComponent() {
			return new PLabel("OperatorAppearancePlaceholder") { Text = "" }.Build();
		}

		public override void ReadFrom(object settings) {
			ModConfig config = settings as ModConfig;
			if (config == null) return;
			selection = catalog.Normalize(config.DefaultCharacterId, config.PreferredSkin,
				config.PreferredModel);
			value = BuildValue(selection);
		}

		public override bool WriteTo(object settings) {
			ModConfig config = settings as ModConfig;
			if (config == null || selection == null) return false;
			bool changed = !string.Equals(config.DefaultCharacterId, selection.Character.Id,
				StringComparison.Ordinal) || !string.Equals(config.PreferredSkin, selection.Skin.Name,
				StringComparison.Ordinal) || !string.Equals(config.PreferredModel, selection.Model,
				StringComparison.Ordinal);
			config.DefaultCharacterId = selection.Character.Id;
			config.PreferredSkin = selection.Skin.Name;
			config.PreferredModel = selection.Model;
			return changed;
		}

		private void OnSearchChanged(GameObject source, string text) {
			OperatorAppearanceDefinition exact = catalog.FindExact(text);
			if (exact != null) SelectOperator(exact.Id);
			RefreshOperatorCombo(BuildOperatorChoices(text));
		}

		private void OnOperatorSelected(GameObject source, Choice choice) {
			if (choice != null) SelectOperator(choice.Value);
		}

		private void OnSkinSelected(GameObject source, Choice choice) {
			if (choice == null) return;
			selection = catalog.Normalize(selection.Character.Id, choice.Value, selection.Model);
			value = BuildValue(selection);
			RefreshSkinAndModelCombos();
		}

		private void OnModelSelected(GameObject source, Choice choice) {
			if (choice == null) return;
			selection = catalog.Normalize(selection.Character.Id, selection.Skin.Name, choice.Value);
			value = BuildValue(selection);
		}

		private void SelectOperator(string characterId) {
			selection = catalog.Normalize(characterId, selection.Skin.Name, selection.Model);
			value = BuildValue(selection);
			RefreshSkinAndModelCombos();
		}

		private List<Choice> BuildOperatorChoices(string query) {
			IList<OperatorAppearanceDefinition> matches = catalog.Search(query, MaximumOperatorMatches);
			List<Choice> choices = new List<Choice>(matches.Count + 1);
			for (int i = 0; i < matches.Count; i++) {
				OperatorAppearanceDefinition item = matches[i];
				choices.Add(new Choice(item.Id, item.Name + "  [" + item.Id + "]", item.Id));
			}
			if (FindChoice(choices, selection.Character.Id) == null) {
				choices.Insert(0, new Choice(selection.Character.Id,
					selection.Character.Name + "  [" + selection.Character.Id + "]",
					selection.Character.Id));
			}
			return choices;
		}

		private List<Choice> BuildSkinChoices() {
			List<Choice> choices = new List<Choice>(selection.Character.Skins.Count);
			for (int i = 0; i < selection.Character.Skins.Count; i++) {
				string name = selection.Character.Skins[i].Name;
				choices.Add(new Choice(name, name, name));
			}
			return choices;
		}

		private List<Choice> BuildModelChoices() {
			List<Choice> choices = new List<Choice>(selection.Skin.Models.Count);
			for (int i = 0; i < selection.Skin.Models.Count; i++) {
				string name = selection.Skin.Models[i];
				choices.Add(new Choice(name, name, name));
			}
			return choices;
		}

		private void RefreshOperatorCombo(List<Choice> choices) {
			RefreshCombo(operatorComboObject, choices, selection.Character.Id);
		}

		private void RefreshSkinAndModelCombos() {
			RefreshCombo(skinComboObject, BuildSkinChoices(), selection.Skin.Name);
			RefreshCombo(modelComboObject, BuildModelChoices(), selection.Model);
		}

		private static void RefreshCombo(GameObject combo, List<Choice> choices, string selectedValue) {
			if (combo == null || ComboComponentType == null || SetComboItems == null) return;
			Component component = combo.GetComponent(ComboComponentType);
			if (component == null) return;
			RectTransform content = ComboContentContainer == null ? null :
				ComboContentContainer.GetValue(component, null) as RectTransform;
			if (content != null) {
				while (content.childCount > 0) {
					Transform row = content.GetChild(content.childCount - 1);
					row.gameObject.SetActive(false);
					row.SetParent(null, false);
					UnityEngine.Object.Destroy(row.gameObject);
				}
			}
			SetComboItems.Invoke(component, new object[] { choices });
			PComboBox<Choice>.SetSelectedItem(combo, FindChoice(choices, selectedValue), false);
		}

		private static Choice FindChoice(IList<Choice> choices, string value) {
			for (int i = 0; i < choices.Count; i++) {
				if (string.Equals(choices[i].Value, value, StringComparison.OrdinalIgnoreCase))
					return choices[i];
			}
			return null;
		}

		private static string BuildValue(OperatorAppearanceSelection current) {
			return current.Character.Id + "|" + current.Skin.Name + "|" + current.Model;
		}

		private static void AddRow(PGridPanel parent, int row, string label, IUIComponent component,
			string tooltip) {
			parent.AddChild(new PLabel("Label") {
				Text = label,
				ToolTip = tooltip,
				TextStyle = PUITuning.Fonts.TextLightStyle
			}, new GridComponentSpec(row, 0) {
				Margin = LABEL_MARGIN,
				Alignment = TextAnchor.MiddleLeft
			});
			parent.AddChild(component, new GridComponentSpec(row, 1) {
				Alignment = TextAnchor.MiddleRight,
				Margin = CONTROL_MARGIN
			});
		}

		private sealed class Choice : IListableOption, ITooltipListableOption {
			public readonly string Value;
			private readonly string label;
			private readonly string tooltip;

			public Choice(string value, string label, string tooltip) {
				Value = value;
				this.label = label;
				this.tooltip = tooltip;
			}

			public string GetProperName() {
				return label;
			}

			public string GetToolTipText() {
				return tooltip;
			}
		}
	}
}
