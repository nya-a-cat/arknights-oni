using System;
using System.Collections.Generic;
using PeterHan.PLib.Options;
using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class AppearanceOptionsHotkey : MonoBehaviour {
		private const int MaximumMatches = 60;
		private const float PickerWidth = 760f;
		private const float PickerHeight = 650f;
		private bool dialogOpen;
		private bool pickerOpen;
		private OperatorDuplicantOverlay target;
		private OperatorAppearanceCatalog catalog;
		private OperatorAppearanceSelection selection;
		private string searchText = string.Empty;
		private Vector2 operatorScroll;
		private string statusText;
		private float statusUntil;
		private string scaleAppearanceKey;
		private string scaleText = string.Empty;
		private string scaleError;

		private void Update() {
			if (dialogOpen) return;
			if (pickerOpen && (target == null || Input.GetKeyDown(KeyCode.Escape))) {
				ClosePicker();
				return;
			}
			bool controlPressed = Input.GetKey(KeyCode.LeftControl) ||
				Input.GetKey(KeyCode.RightControl);
			if (!controlPressed || !Input.GetKeyDown(KeyCode.F8)) return;
			if (pickerOpen) {
				ClosePicker();
				return;
			}
			bool shiftPressed = Input.GetKey(KeyCode.LeftShift) ||
				Input.GetKey(KeyCode.RightShift);
			if (shiftPressed) OpenGlobalDialog();
			else OpenForSelection();
		}

		private void OpenForSelection() {
			if (dialogOpen) return;
			KSelectable selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
			OperatorDuplicantOverlay overlay = selected == null ? null :
				selected.GetComponent<OperatorDuplicantOverlay>();
			if (overlay == null && selected != null)
				overlay = selected.GetComponentInParent<OperatorDuplicantOverlay>();
			if (overlay == null) {
				ShowStatus(ModLocalization.Text("请先选择一个复制人。", "Select a duplicant first."));
				return;
			}
			try {
				if (catalog == null) catalog = OperatorAppearanceCatalog.Load(ModAssets.OperatorCatalogPath);
				ModConfig current = overlay.CurrentAppearanceConfig;
				selection = catalog.Normalize(current.DefaultCharacterId, current.PreferredSkin,
					current.PreferredModel);
				target = overlay;
				searchText = string.Empty;
				operatorScroll = Vector2.zero;
				pickerOpen = true;
				RefreshScaleEditor();
			} catch (Exception error) {
				ShowStatus(ModLocalization.Text("无法打开单个复制人外观：", "Could not open duplicant appearance: ") +
					error.Message);
				Debug.LogError("[ArknightsOperatorsMod] Failed to open duplicant appearance: " + error);
			}
		}

		private void OpenGlobalDialog() {
			if (dialogOpen || pickerOpen) return;
			try {
				dialogOpen = true;
				POptions.ShowDialog(typeof(ModConfig), OnDialogClosed);
			} catch (Exception error) {
				dialogOpen = false;
				Debug.LogError("[ArknightsOperatorsMod] Failed to open appearance options: " + error);
			}
		}

		private void OnDialogClosed(object settings) {
			dialogOpen = false;
			ModConfig config = settings as ModConfig;
			if (config == null) return;
			config.Normalize();
			ModConfigStore.ApplySaved(config);
		}

		private void OnGUI() {
			if (!pickerOpen) {
				DrawStatus();
				return;
			}
			if (target == null || selection == null || catalog == null) return;
			GUI.depth = -1100;
			Rect panel = new Rect((Screen.width - PickerWidth) * 0.5f,
				(Screen.height - PickerHeight) * 0.5f, PickerWidth, PickerHeight);
			Color previousColor = GUI.color;
			GUI.color = new Color(0.07f, 0.09f, 0.13f, 0.97f);
			GUI.Box(panel, string.Empty);
			GUI.color = previousColor;

			string mode = target.HasIndividualAppearance ?
				ModLocalization.Text("单独外观", "Individual appearance") :
				ModLocalization.Text("继承全局默认", "Using global default");
			GUI.Box(new Rect(panel.x + 20f, panel.y + 16f, panel.width - 88f, 54f),
				ModLocalization.Text("复制人干员外观：", "Duplicant operator appearance: ") +
				target.DuplicantName + "\n" + mode + "  ·  Ctrl+F8 / Esc");
			if (GUI.Button(new Rect(panel.x + panel.width - 58f, panel.y + 16f, 38f, 38f), "×")) {
				ClosePicker();
				return;
			}

			GUI.Label(new Rect(panel.x + 20f, panel.y + 82f, 108f, 30f),
				ModLocalization.Text("搜索干员", "Search"));
			searchText = GUI.TextField(new Rect(panel.x + 125f, panel.y + 80f, 365f, 32f),
				searchText ?? string.Empty, 80);
			GUI.Label(new Rect(panel.x + 505f, panel.y + 82f, 225f, 30f),
				ModLocalization.Text("中文 / English / 日本語 / ID", "Name / alias / char_id"));

			IList<OperatorAppearanceDefinition> matches = catalog.Search(searchText, MaximumMatches);
			if (!string.IsNullOrWhiteSpace(searchText) && matches.Count == 1 &&
				!string.Equals(matches[0].Id, selection.Character.Id, StringComparison.Ordinal))
				selection = catalog.Normalize(matches[0].Id, selection.Skin.Name, selection.Model);
			Rect listRect = new Rect(panel.x + 20f, panel.y + 124f, 405f, 438f);
			GUI.Box(listRect, string.Empty);
			float contentHeight = Mathf.Max(listRect.height - 12f, matches.Count * 38f + 8f);
			operatorScroll = GUI.BeginScrollView(new Rect(listRect.x + 6f, listRect.y + 6f,
				listRect.width - 12f, listRect.height - 12f), operatorScroll,
				new Rect(0f, 0f, listRect.width - 34f, contentHeight));
			for (int i = 0; i < matches.Count; i++) {
				OperatorAppearanceDefinition item = matches[i];
				Color previousBackground = GUI.backgroundColor;
				if (string.Equals(item.Id, selection.Character.Id, StringComparison.Ordinal))
					GUI.backgroundColor = new Color(0.54f, 0.43f, 0.95f, 1f);
				if (GUI.Button(new Rect(2f, i * 38f + 2f, listRect.width - 42f, 34f),
					OperatorLabel(item))) {
					selection = catalog.Normalize(item.Id, selection.Skin.Name, selection.Model);
				}
				GUI.backgroundColor = previousBackground;
			}
			GUI.EndScrollView();

			float rightX = panel.x + 445f;
			GUI.Box(new Rect(rightX, panel.y + 124f, 295f, 94f),
				OperatorLabel(selection.Character) + "\n" +
				ModLocalization.Text("皮肤：", "Skin: ") + DisplayValue(selection.Skin.Name) +
				"  ·  " + ModLocalization.Text("模型：", "Model: ") + DisplayValue(selection.Model));

			GUI.Label(new Rect(rightX, panel.y + 238f, 295f, 28f),
				ModLocalization.Text("皮肤", "Skin"));
			if (GUI.Button(new Rect(rightX, panel.y + 268f, 42f, 38f), "‹")) CycleSkin(-1);
			GUI.Box(new Rect(rightX + 50f, panel.y + 268f, 195f, 38f), DisplayValue(selection.Skin.Name));
			if (GUI.Button(new Rect(rightX + 253f, panel.y + 268f, 42f, 38f), "›")) CycleSkin(1);

			GUI.Label(new Rect(rightX, panel.y + 326f, 295f, 28f),
				ModLocalization.Text("首选模型", "Preferred model"));
			if (GUI.Button(new Rect(rightX, panel.y + 356f, 42f, 38f), "‹")) CycleModel(-1);
			GUI.Box(new Rect(rightX + 50f, panel.y + 356f, 195f, 38f), DisplayValue(selection.Model));
			if (GUI.Button(new Rect(rightX + 253f, panel.y + 356f, 42f, 38f), "›")) CycleModel(1);

			DrawScaleEditor(rightX, panel.y + 414f);
			GUI.Box(new Rect(rightX, panel.y + 506f, 295f, 56f), ModLocalization.Text(
				"比例按当前实际模型保存，并由使用同一外观的复制人共享。",
				"The size is saved for the loaded model and shared by duplicants using that appearance."
			));

			if (GUI.Button(new Rect(panel.x + 20f, panel.y + 584f, 250f, 46f),
				ModLocalization.Text("应用到这个复制人", "Apply to this duplicant"))) {
				target.SetIndividualAppearance(selection.Character.Id, selection.Skin.Name, selection.Model);
				ShowStatus(ModLocalization.Text("已设置 ", "Assigned ") +
					DisplayName(selection.Character) + " → " + target.DuplicantName);
				ClosePicker();
				return;
			}
			if (GUI.Button(new Rect(panel.x + 285f, panel.y + 584f, 230f, 46f),
				ModLocalization.Text("恢复全局默认", "Use global default"))) {
				string targetName = target.DuplicantName;
				target.ClearIndividualAppearance();
				ShowStatus(ModLocalization.Text("已恢复全局默认：", "Global default restored: ") + targetName);
				ClosePicker();
				return;
			}
			if (GUI.Button(new Rect(panel.x + 530f, panel.y + 584f, 210f, 46f),
				ModLocalization.Text("取消", "Cancel"))) ClosePicker();
		}

		private void CycleSkin(int direction) {
			List<OperatorSkinDefinition> skins = selection.Character.Skins;
			int index = skins.IndexOf(selection.Skin);
			index = (index + direction + skins.Count) % skins.Count;
			selection = catalog.Normalize(selection.Character.Id, skins[index].Name, selection.Model);
		}

		private void CycleModel(int direction) {
			List<string> models = selection.Skin.Models;
			int index = models.IndexOf(selection.Model);
			index = (index + direction + models.Count) % models.Count;
			selection = catalog.Normalize(selection.Character.Id, selection.Skin.Name, models[index]);
		}

		private void ClosePicker() {
			pickerOpen = false;
			target = null;
			selection = null;
			scaleAppearanceKey = null;
			scaleText = string.Empty;
			scaleError = null;
		}

		private void DrawScaleEditor(float x, float y) {
			RefreshScaleEditorWhenModelChanges();
			bool canEdit = target != null && !string.IsNullOrEmpty(scaleAppearanceKey);
			string model = canEdit ? DisplayValue(target.ActiveModel) :
				ModLocalization.Text("正在加载", "Loading");
			GUI.Label(new Rect(x, y, 295f, 24f),
				ModLocalization.Text("当前模型比例：", "Loaded model size: ") + model);

			bool previousEnabled = GUI.enabled;
			GUI.enabled = canEdit;
			string nextText = GUI.TextField(new Rect(x, y + 28f, 78f, 32f),
				scaleText ?? string.Empty, 3);
			GUI.Label(new Rect(x + 84f, y + 32f, 24f, 24f), "%");
			if (!string.Equals(nextText, scaleText, StringComparison.Ordinal)) {
				scaleText = nextText;
				int scalePercent;
				if (int.TryParse(scaleText, out scalePercent) &&
					ModConfig.IsValidVisualScalePercent(scalePercent) &&
					target.SetActiveVisualScalePercent(scalePercent)) {
					scaleError = null;
				} else {
					scaleError = ModLocalization.Text("请输入 75–200 的整数", "Enter an integer from 75 to 200");
				}
			}
			if (GUI.Button(new Rect(x + 116f, y + 28f, 179f, 32f),
				ModLocalization.Text("恢复默认比例", "Restore default size")) &&
				target.ResetActiveVisualScalePercent()) {
				scaleText = target.ActiveVisualScalePercent.ToString();
				scaleError = null;
			}
			GUI.enabled = previousEnabled;
			if (!string.IsNullOrEmpty(scaleError))
				GUI.Label(new Rect(x, y + 63f, 295f, 24f), scaleError);
		}

		private void RefreshScaleEditorWhenModelChanges() {
			string currentKey = target == null ? null : target.ActiveAppearanceScaleKey;
			if (string.Equals(currentKey, scaleAppearanceKey, StringComparison.Ordinal)) return;
			RefreshScaleEditor();
		}

		private void RefreshScaleEditor() {
			scaleAppearanceKey = target == null ? null : target.ActiveAppearanceScaleKey;
			scaleText = string.IsNullOrEmpty(scaleAppearanceKey) ? string.Empty :
				target.ActiveVisualScalePercent.ToString();
			scaleError = null;
		}

		private void ShowStatus(string text) {
			statusText = text;
			statusUntil = Time.unscaledTime + 4f;
		}

		private void DrawStatus() {
			if (string.IsNullOrEmpty(statusText) || Time.unscaledTime > statusUntil) return;
			GUI.depth = -1100;
			GUI.Box(new Rect(Screen.width * 0.5f - 300f, 82f, 600f, 44f), statusText);
		}

		private static string DisplayName(OperatorAppearanceDefinition item) {
			if (ModLocalization.UseJapanese && !string.IsNullOrWhiteSpace(item.JapaneseName))
				return item.JapaneseName;
			return !ModLocalization.UseChinese && !string.IsNullOrWhiteSpace(item.EnglishName) ?
				item.EnglishName : item.Name;
		}

		private static string OperatorLabel(OperatorAppearanceDefinition item) {
			string primary = DisplayName(item);
			string secondary = ModLocalization.UseChinese ? item.EnglishName : item.Name;
			if (!string.IsNullOrWhiteSpace(secondary) &&
				!string.Equals(primary, secondary, StringComparison.OrdinalIgnoreCase))
				return primary + " / " + secondary + "  [" + item.Id + "]";
			return primary + "  [" + item.Id + "]";
		}

		private static string DisplayValue(string value) {
			switch (value) {
				case "默认": return ModLocalization.Text("默认", "Default");
				case "基建": return ModLocalization.Text("基建", "Base");
				case "正面": return ModLocalization.Text("正面", "Front");
				case "背面": return ModLocalization.Text("背面", "Back");
				case "战斗": return ModLocalization.Text("战斗", "Combat");
				default: return value;
			}
		}
	}
}
