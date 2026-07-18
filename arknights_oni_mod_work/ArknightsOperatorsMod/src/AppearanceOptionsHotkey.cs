using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PeterHan.PLib.Options;
using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class AppearanceOptionsHotkey : MonoBehaviour {
		private const int GalleryPageSize = 20;
		private const int GalleryColumns = 5;
		private const float PickerWidth = 900f;
		private const float PickerHeight = 730f;
		private const float GalleryThumbnailSize = 72f;
		private const float ThumbnailSearchDebounceSeconds = 0.35f;
		private bool dialogOpen;
		private bool pickerOpen;
		private OperatorDuplicantOverlay target;
		private OperatorAppearanceCatalog catalog;
		private OperatorAppearanceSelection selection;
		private string searchText = string.Empty;
		private string cachedSearchText;
		private IList<OperatorAppearanceDefinition> cachedMatches;
		private float lastSearchEditTime;
		private int operatorPage;
		private string thumbnailPageKey;
		private OperatorThumbnailLoader thumbnailLoader;
		private CancellationTokenSource thumbnailPageCancellation;
		private readonly Dictionary<string, ThumbnailView> thumbnailViews =
			new Dictionary<string, ThumbnailView>(StringComparer.OrdinalIgnoreCase);
		private string statusText;
		private float statusUntil;
		private string scaleAppearanceKey;
		private string scaleText = string.Empty;
		private string scaleError;
		private bool appearanceOperationPending;
		private bool applyWhenAppearanceReady;
		private string operationAppearanceKey;
		private string operationStatus;
		private GUIStyle galleryCardLabel;
		private GUIStyle galleryCenteredLabel;

		private sealed class ThumbnailView {
			public Task<OperatorThumbnailAsset> Pending;
			public OperatorThumbnailAsset Asset;
			public Texture2D Texture;
			public string Error;
		}

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

		private void OnDestroy() {
			EndThumbnailSession();
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
				cachedSearchText = null;
				cachedMatches = null;
				lastSearchEditTime = Time.unscaledTime - ThumbnailSearchDebounceSeconds;
				operatorPage = 0;
				pickerOpen = true;
				BeginThumbnailSession();
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
			if (UpdateAppearanceOperation()) return;
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
			string previousSearch = searchText;
			searchText = GUI.TextField(new Rect(panel.x + 125f, panel.y + 80f, 420f, 32f),
				searchText ?? string.Empty, 80);
			if (!string.Equals(previousSearch, searchText, StringComparison.Ordinal)) {
				operatorPage = 0;
				cachedSearchText = null;
				cachedMatches = null;
				lastSearchEditTime = Time.unscaledTime;
				ResetThumbnailPage();
			}
			GUI.Label(new Rect(panel.x + 560f, panel.y + 82f, 320f, 30f),
				ModLocalization.Text("中文 / English / 日本語 / ID", "Name / alias / char_id"));

			IList<OperatorAppearanceDefinition> matches = SearchMatches();
			if (!appearanceOperationPending && !string.IsNullOrWhiteSpace(searchText) && matches.Count == 1 &&
				!string.Equals(matches[0].Id, selection.Character.Id, StringComparison.Ordinal)) {
				selection = catalog.Normalize(matches[0].Id, selection.Skin.Name, selection.Model);
				operationStatus = null;
				RefreshScaleEditor();
			}
			int pageCount = Math.Max(1, (matches.Count + GalleryPageSize - 1) / GalleryPageSize);
			operatorPage = Mathf.Clamp(operatorPage, 0, pageCount - 1);
			Rect listRect = new Rect(panel.x + 20f, panel.y + 124f, 520f, 500f);
			DrawOperatorGallery(listRect, matches, pageCount);

			float rightX = panel.x + 560f;
			const float rightWidth = 320f;
			GUI.Box(new Rect(rightX, panel.y + 124f, rightWidth, 64f),
				OperatorLabel(selection.Character) + "\n" +
				ModLocalization.Text("皮肤：", "Skin: ") + DisplayValue(selection.Skin.Name) +
				"  ·  " + ModLocalization.Text("模型：", "Model: ") + DisplayValue(selection.Model));
			DrawSelectedThumbnail(new Rect(rightX, panel.y + 196f, rightWidth, 142f));

			GUI.Label(new Rect(rightX, panel.y + 344f, rightWidth, 28f),
				ModLocalization.Text("皮肤", "Skin"));
			if (!appearanceOperationPending &&
				GUI.Button(new Rect(rightX, panel.y + 374f, 42f, 38f), "‹")) CycleSkin(-1);
			GUI.Box(new Rect(rightX + 50f, panel.y + 374f, 220f, 38f), DisplayValue(selection.Skin.Name));
			if (!appearanceOperationPending &&
				GUI.Button(new Rect(rightX + 278f, panel.y + 374f, 42f, 38f), "›")) CycleSkin(1);

			GUI.Label(new Rect(rightX, panel.y + 422f, rightWidth, 28f),
				ModLocalization.Text("首选模型", "Preferred model"));
			if (!appearanceOperationPending &&
				GUI.Button(new Rect(rightX, panel.y + 452f, 42f, 38f), "‹")) CycleModel(-1);
			GUI.Box(new Rect(rightX + 50f, panel.y + 452f, 220f, 38f), DisplayValue(selection.Model));
			if (!appearanceOperationPending &&
				GUI.Button(new Rect(rightX + 278f, panel.y + 452f, 42f, 38f), "›")) CycleModel(1);

			bool previousEnabled = GUI.enabled;
			GUI.enabled = previousEnabled && !appearanceOperationPending;
			if (GUI.Button(new Rect(rightX, panel.y + 500f, rightWidth, 38f),
				ModLocalization.Text("预览当前皮肤", "Preview selected appearance")))
				PreviewSelection();
			GUI.enabled = previousEnabled;

			DrawScaleEditor(rightX, panel.y + 546f);
			GUI.Box(new Rect(rightX, panel.y + 636f, rightWidth, 30f),
				string.IsNullOrEmpty(operationStatus) ? ModLocalization.Text(
					"头像用于识别；皮肤请看场景预览。",
					"Avatar ID only; skin preview is in-world.") : operationStatus);

			GUI.enabled = previousEnabled && !appearanceOperationPending;
			if (GUI.Button(new Rect(panel.x + 20f, panel.y + 674f, 280f, 46f),
				ModLocalization.Text("应用到这个复制人", "Apply to this duplicant")))
				ApplySelection();
			GUI.enabled = previousEnabled;
			if (GUI.Button(new Rect(panel.x + 315f, panel.y + 674f, 270f, 46f),
				ModLocalization.Text("恢复全局默认", "Use global default"))) {
				string targetName = target.DuplicantName;
				target.ClearIndividualAppearance();
				ShowStatus(ModLocalization.Text("已恢复全局默认：", "Global default restored: ") + targetName);
				ClosePicker();
				return;
			}
			if (GUI.Button(new Rect(panel.x + 600f, panel.y + 674f, 280f, 46f),
				ModLocalization.Text("取消", "Cancel"))) ClosePicker();
		}

		private void DrawOperatorGallery(Rect rect, IList<OperatorAppearanceDefinition> matches,
			int pageCount) {
			GUI.Box(rect, string.Empty);
			int start = operatorPage * GalleryPageSize;
			int end = Math.Min(start + GalleryPageSize, matches.Count);
			EnsureThumbnailPage(matches, start, end);

			const float gap = 5f;
			const float cardHeight = 104f;
			float innerX = rect.x + 6f;
			float innerWidth = rect.width - 12f;
			int columns = rect.width >= 500f ? GalleryColumns : GalleryColumns - 1;
			float cardWidth = (innerWidth - gap * (columns - 1)) / columns;
			GUIStyle cardLabel = GalleryCardLabel();
			for (int index = start; index < end; index++) {
				int localIndex = index - start;
				int row = localIndex / columns;
				int column = localIndex % columns;
				Rect card = new Rect(innerX + column * (cardWidth + gap),
					rect.y + 6f + row * (cardHeight + gap), cardWidth, cardHeight);
				OperatorAppearanceDefinition item = matches[index];
				Color previousBackground = GUI.backgroundColor;
				if (string.Equals(item.Id, selection.Character.Id, StringComparison.OrdinalIgnoreCase))
					GUI.backgroundColor = new Color(0.54f, 0.43f, 0.95f, 1f);
				if (!appearanceOperationPending && GUI.Button(card, string.Empty)) SelectOperator(item);
				GUI.backgroundColor = previousBackground;

				Rect imageRect = new Rect(card.x + (card.width - GalleryThumbnailSize) * 0.5f,
					card.y + 5f, GalleryThumbnailSize, GalleryThumbnailSize);
				DrawThumbnail(item, imageRect, cardLabel);
				GUI.Label(new Rect(card.x + 3f, card.y + 79f, card.width - 6f, 21f),
					DisplayName(item), cardLabel);
			}

			float footerY = rect.y + rect.height - 40f;
			bool previousEnabled = GUI.enabled;
			GUI.enabled = previousEnabled && operatorPage > 0 && !appearanceOperationPending;
			if (GUI.Button(new Rect(rect.x + 8f, footerY, 80f, 32f),
				ModLocalization.Text("上一页", "Previous"))) {
				operatorPage--;
				ResetThumbnailPage();
			}
			GUI.enabled = previousEnabled;
			GUI.Box(new Rect(rect.x + 96f, footerY, rect.width - 192f, 32f),
				(operatorPage + 1) + " / " + pageCount + "  ·  " + matches.Count);
			GUI.enabled = previousEnabled && operatorPage + 1 < pageCount && !appearanceOperationPending;
			if (GUI.Button(new Rect(rect.x + rect.width - 88f, footerY, 80f, 32f),
				ModLocalization.Text("下一页", "Next"))) {
				operatorPage++;
				ResetThumbnailPage();
			}
			GUI.enabled = previousEnabled;
		}

		private void DrawSelectedThumbnail(Rect rect) {
			GUI.Box(rect, string.Empty);
			GUIStyle centered = GalleryCenteredLabel();
			const float selectedSize = 112f;
			Rect imageRect = new Rect(rect.x + (rect.width - selectedSize) * 0.5f,
				rect.y + 4f, selectedSize, selectedSize);
			DrawThumbnail(selection.Character, imageRect, centered);
			string caption = ModLocalization.Text("96px PRTS 干员头像", "96px PRTS operator avatar");
			ThumbnailView selectedView;
			if (thumbnailViews.TryGetValue(selection.Character.Id, out selectedView) &&
				!string.IsNullOrEmpty(selectedView.Error))
				caption = ModLocalization.Text("头像失败：", "Avatar failed: ") +
					ShortError(selectedView.Error, 42);
			GUI.Label(new Rect(rect.x + 4f, rect.y + 116f, rect.width - 8f, 22f), caption, centered);
		}

		private void DrawThumbnail(OperatorAppearanceDefinition item, Rect rect, GUIStyle placeholderStyle) {
			EnsureThumbnail(item);
			ThumbnailView view;
			if (!thumbnailViews.TryGetValue(item.Id, out view)) {
				GUI.Box(rect, DisplayName(item), placeholderStyle);
				return;
			}
			CompleteThumbnail(view, item);
			if (view.Texture != null) {
				GUI.DrawTexture(rect, view.Texture, ScaleMode.ScaleToFit, true);
				return;
			}
			string placeholder;
			if (string.IsNullOrEmpty(view.Error))
				placeholder = ModLocalization.Text("加载中…", "Loading...");
			else if (string.IsNullOrWhiteSpace(item.ThumbnailUrl))
				placeholder = ModLocalization.Text("暂无头像", "No avatar");
			else
				placeholder = ModLocalization.Text("点击重试", "Click to retry");
			GUI.Box(rect, new GUIContent(placeholder, view.Error ?? string.Empty), placeholderStyle);
		}

		private void SelectOperator(OperatorAppearanceDefinition item) {
			selection = catalog.Normalize(item.Id, selection.Skin.Name, selection.Model);
			operationStatus = null;
			RefreshScaleEditor();
			RetryThumbnailIfFailed(item);
			EnsureThumbnail(item);
		}

		private void RetryThumbnailIfFailed(OperatorAppearanceDefinition item) {
			ThumbnailView view;
			if (item == null || string.IsNullOrWhiteSpace(item.ThumbnailUrl) ||
				!thumbnailViews.TryGetValue(item.Id, out view) || string.IsNullOrEmpty(view.Error))
				return;
			if (view.Texture != null) Destroy(view.Texture);
			if (view.Asset != null) view.Asset.Dispose();
			if (view.Pending != null) DisposeThumbnailWhenComplete(view.Pending);
			thumbnailViews.Remove(item.Id);
		}

		private void CycleSkin(int direction) {
			if (appearanceOperationPending) return;
			List<OperatorSkinDefinition> skins = selection.Character.Skins;
			int index = skins.IndexOf(selection.Skin);
			index = (index + direction + skins.Count) % skins.Count;
			selection = catalog.Normalize(selection.Character.Id, skins[index].Name, selection.Model);
			RefreshScaleEditor();
			BeginAppearanceOperation(false);
		}

		private void CycleModel(int direction) {
			if (appearanceOperationPending) return;
			List<string> models = selection.Skin.Models;
			int index = models.IndexOf(selection.Model);
			index = (index + direction + models.Count) % models.Count;
			selection = catalog.Normalize(selection.Character.Id, selection.Skin.Name, models[index]);
			RefreshScaleEditor();
			BeginAppearanceOperation(false);
		}

		private void BeginThumbnailSession() {
			EndThumbnailSession();
			PrtsResourceService resources = PrtsResourceService.Instance;
			if (resources == null) return;
			thumbnailLoader = new OperatorThumbnailLoader(resources);
			thumbnailPageCancellation = new CancellationTokenSource();
		}

		private void EnsureThumbnailPage(IList<OperatorAppearanceDefinition> matches,
			int start, int end) {
			string key = (searchText ?? string.Empty).Trim().ToLowerInvariant() + "\u001f" + operatorPage;
			if (!string.Equals(key, thumbnailPageKey, StringComparison.Ordinal)) {
				ResetThumbnailPage();
				thumbnailPageKey = key;
			}
			if (selection != null) EnsureThumbnail(selection.Character);
			for (int i = start; i < end; i++) EnsureThumbnail(matches[i]);
		}

		private void EnsureThumbnail(OperatorAppearanceDefinition item) {
			if (item == null || thumbnailViews.ContainsKey(item.Id) ||
				Time.unscaledTime - lastSearchEditTime < ThumbnailSearchDebounceSeconds) return;
			ThumbnailView view = new ThumbnailView();
			thumbnailViews[item.Id] = view;
			if (thumbnailLoader == null || string.IsNullOrWhiteSpace(item.ThumbnailUrl)) {
				view.Error = ModLocalization.Text("暂无头像", "No avatar");
				return;
			}
			if (thumbnailPageCancellation == null)
				thumbnailPageCancellation = new CancellationTokenSource();
			view.Pending = thumbnailLoader.LoadAsync(item, thumbnailPageCancellation.Token);
		}

		private void CompleteThumbnail(ThumbnailView view, OperatorAppearanceDefinition item) {
			Task<OperatorThumbnailAsset> pending = view.Pending;
			if (pending == null || !pending.IsCompleted) return;
			view.Pending = null;
			if (pending.IsCanceled) {
				view.Error = ModLocalization.Text("已取消", "Canceled");
				return;
			}
			if (pending.IsFaulted) {
				Exception error = pending.Exception.GetBaseException();
				view.Error = error.Message;
				Debug.LogWarning("[ArknightsOperatorsMod] Thumbnail failed for " + item.Id + ": " +
					error.Message);
				return;
			}

			Texture2D texture = null;
			try {
				view.Asset = pending.Result;
				byte[] bytes = File.ReadAllBytes(view.Asset.LocalPath);
				texture = new Texture2D(2, 2, TextureFormat.ARGB32, false) {
					name = "ArknightsOperatorThumbnail_" + item.Id,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp
				};
				if (!texture.LoadImage(bytes))
					throw new InvalidDataException("Unity could not decode the operator thumbnail");
				view.Texture = texture;
				texture = null;
			} catch (Exception error) {
				if (texture != null) Destroy(texture);
				if (view.Asset != null) {
					view.Asset.Dispose();
					view.Asset = null;
				}
				view.Error = error.Message;
				Debug.LogWarning("[ArknightsOperatorsMod] Thumbnail decode failed for " + item.Id +
					": " + error.Message);
			}
		}

		private void ResetThumbnailPage() {
			if (thumbnailPageCancellation != null) {
				thumbnailPageCancellation.Cancel();
				thumbnailPageCancellation.Dispose();
				thumbnailPageCancellation = null;
			}
			ReleaseThumbnailViews();
			thumbnailPageKey = null;
			if (thumbnailLoader != null)
				thumbnailPageCancellation = new CancellationTokenSource();
		}

		private void EndThumbnailSession() {
			if (thumbnailPageCancellation != null) {
				thumbnailPageCancellation.Cancel();
				thumbnailPageCancellation.Dispose();
				thumbnailPageCancellation = null;
			}
			ReleaseThumbnailViews();
			if (thumbnailLoader != null) {
				thumbnailLoader.Dispose();
				thumbnailLoader = null;
			}
			thumbnailPageKey = null;
		}

		private void ReleaseThumbnailViews() {
			foreach (ThumbnailView view in thumbnailViews.Values) {
				if (view.Texture != null) Destroy(view.Texture);
				if (view.Asset != null) view.Asset.Dispose();
				if (view.Pending != null) DisposeThumbnailWhenComplete(view.Pending);
			}
			thumbnailViews.Clear();
		}

		private static void DisposeThumbnailWhenComplete(Task<OperatorThumbnailAsset> pending) {
			pending.ContinueWith(task => {
				if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
				else if (task.IsFaulted) task.Exception.Handle(error => true);
			}, TaskScheduler.Default);
		}

		private IList<OperatorAppearanceDefinition> SearchMatches() {
			string normalized = searchText ?? string.Empty;
			if (cachedMatches != null &&
				string.Equals(cachedSearchText, normalized, StringComparison.Ordinal))
				return cachedMatches;
			cachedSearchText = normalized;
			cachedMatches = catalog.Search(normalized, catalog.Operators.Count);
			return cachedMatches;
		}

		private GUIStyle GalleryCardLabel() {
			if (galleryCardLabel == null) {
				galleryCardLabel = new GUIStyle(GUI.skin.label) {
					alignment = TextAnchor.MiddleCenter,
					clipping = TextClipping.Clip,
					fontSize = 11
				};
			}
			return galleryCardLabel;
		}

		private GUIStyle GalleryCenteredLabel() {
			if (galleryCenteredLabel == null) {
				galleryCenteredLabel = new GUIStyle(GUI.skin.label) {
					alignment = TextAnchor.MiddleCenter,
					clipping = TextClipping.Clip
				};
			}
			return galleryCenteredLabel;
		}

		private void ClosePicker() {
			if (target != null)
				target.ClearPreviewAppearance();
			EndThumbnailSession();
			pickerOpen = false;
			target = null;
			selection = null;
			scaleAppearanceKey = null;
			scaleText = string.Empty;
			scaleError = null;
			appearanceOperationPending = false;
			applyWhenAppearanceReady = false;
			operationAppearanceKey = null;
			operationStatus = null;
		}

		private void DrawScaleEditor(float x, float y) {
			RefreshScaleEditorWhenModelChanges();
			bool canEdit = selection != null && !string.IsNullOrEmpty(scaleAppearanceKey);
			string model = canEdit ? DisplayValue(selection.Model) :
				ModLocalization.Text("正在加载", "Loading");
			GUI.Label(new Rect(x, y, 295f, 24f),
				ModLocalization.Text("选中模型比例：", "Selected model size: ") + model);

			bool previousEnabled = GUI.enabled;
			GUI.enabled = canEdit;
			string nextText = GUI.TextField(new Rect(x, y + 28f, 78f, 32f),
				scaleText ?? string.Empty, 3);
			GUI.Label(new Rect(x + 84f, y + 32f, 24f, 24f), "%");
			if (!string.Equals(nextText, scaleText, StringComparison.Ordinal)) {
				int scalePercent;
				if (int.TryParse(nextText, out scalePercent) &&
					ModConfig.IsValidVisualScalePercent(scalePercent)) {
					SaveScale(nextText, scalePercent);
				} else {
					scaleText = nextText;
					scaleError = ModLocalization.Text("请输入 75–200 的整数", "Enter an integer from 75 to 200");
				}
			}
			if (GUI.Button(new Rect(x + 116f, y + 28f, 179f, 32f),
				ModLocalization.Text("恢复默认比例", "Restore default size")) &&
				ResetScale()) {
				scaleText = ModConfigStore.Current.ResolveVisualScalePercent(
					selection.Character.Id, selection.Skin.Name, selection.Model).ToString();
				scaleError = null;
			}
			GUI.enabled = previousEnabled;
			if (!string.IsNullOrEmpty(scaleError))
				GUI.Label(new Rect(x, y + 63f, 295f, 24f), scaleError);
		}

		private void SaveScale(string nextText, int scalePercent) {
			string previousText = scaleText;
			ModConfig previousConfig = null;
			try {
				previousConfig = ModConfigStore.Current;
				ModConfigStore.SetAppearanceVisualScale(selection.Character.Id,
					selection.Skin.Name, selection.Model, scalePercent);
				scaleText = nextText;
				scaleError = null;
			} catch (Exception error) {
				RestoreScaleAfterFailure(previousConfig, previousText, error);
			}
		}

		private bool ResetScale() {
			string previousText = scaleText;
			ModConfig previousConfig = null;
			try {
				previousConfig = ModConfigStore.Current;
				ModConfigStore.ResetAppearanceVisualScale(selection.Character.Id,
					selection.Skin.Name, selection.Model);
				scaleText = ModConfigStore.Current.ResolveVisualScalePercent(
					selection.Character.Id, selection.Skin.Name, selection.Model).ToString();
				return true;
			} catch (Exception error) {
				RestoreScaleAfterFailure(previousConfig, previousText, error);
				return false;
			}
		}

		private void RestoreScaleAfterFailure(ModConfig previousConfig, string previousText,
			Exception error) {
			if (previousConfig != null) {
				try {
					ModConfigStore.SaveAndApply(previousConfig);
				} catch (Exception rollbackError) {
					Debug.LogWarning("[ArknightsOperatorsMod] Size rollback reported an error: " +
						rollbackError.Message);
				}
			}
			scaleText = previousText;
			scaleError = ModLocalization.Text("比例保存失败，请重试", "Could not save size; try again");
			Debug.LogWarning("[ArknightsOperatorsMod] Could not save appearance size: " + error.Message);
		}

		private void RefreshScaleEditorWhenModelChanges() {
			string currentKey = SelectionScaleKey();
			if (string.Equals(currentKey, scaleAppearanceKey, StringComparison.Ordinal)) return;
			RefreshScaleEditor();
		}

		private void RefreshScaleEditor() {
			scaleAppearanceKey = SelectionScaleKey();
			scaleText = selection == null || string.IsNullOrEmpty(scaleAppearanceKey) ? string.Empty :
				ModConfigStore.Current.ResolveVisualScalePercent(
					selection.Character.Id, selection.Skin.Name, selection.Model).ToString();
			scaleError = null;
		}

		private string SelectionScaleKey() {
			return selection == null ? null : ModConfig.AppearanceScaleKey(
				selection.Character.Id, selection.Skin.Name, selection.Model);
		}

		private void PreviewSelection() {
			if (target == null || selection == null) return;
			BeginAppearanceOperation(false);
		}

		private void ApplySelection() {
			if (target == null || selection == null) return;
			BeginAppearanceOperation(true);
		}

		private void BeginAppearanceOperation(bool applyWhenReady) {
			appearanceOperationPending = true;
			applyWhenAppearanceReady = applyWhenReady;
			operationAppearanceKey = SelectionScaleKey();
			operationStatus = applyWhenReady ?
				ModLocalization.Text("正在加载并应用外观……", "Loading and applying appearance...") :
				ModLocalization.Text("正在加载预览……", "Loading preview...");
			target.PreviewAppearance(selection.Character.Id, selection.Skin.Name, selection.Model);
		}

		private bool UpdateAppearanceOperation() {
			if (!appearanceOperationPending || target == null || selection == null) return false;
			if (!string.Equals(operationAppearanceKey, SelectionScaleKey(), StringComparison.Ordinal)) {
				appearanceOperationPending = false;
				applyWhenAppearanceReady = false;
				operationStatus = ModLocalization.Text("选择已改变，请重试。",
					"Selection changed; try again.");
				return false;
			}

			string characterId = selection.Character.Id;
			string skin = selection.Skin.Name;
			string model = selection.Model;
			if (target.IsAppearanceActive(characterId, skin, model)) {
				appearanceOperationPending = false;
				if (applyWhenAppearanceReady) {
					string targetName = target.DuplicantName;
					string displayName = DisplayName(selection.Character);
					target.SetIndividualAppearance(characterId, skin, model);
					applyWhenAppearanceReady = false;
					ShowStatus(ModLocalization.Text("已设置 ", "Assigned ") +
						displayName + " → " + targetName);
					ClosePicker();
					return true;
				}
				applyWhenAppearanceReady = false;
				operationStatus = ModLocalization.Text("预览已加载。", "Preview ready.");
				return false;
			}
			if (target.IsAppearanceLoading(characterId, skin, model)) return false;

			appearanceOperationPending = false;
			applyWhenAppearanceReady = false;
			string error = target.LastAppearanceLoadError;
			operationStatus = string.IsNullOrEmpty(error) ?
				ModLocalization.Text("外观未能加载，请重试。", "Appearance did not load; try again.") :
				ModLocalization.Text("加载失败：", "Load failed: ") + ShortError(error);
			return false;
		}

		private static string ShortError(string value) {
			return ShortError(value, 90);
		}

		private static string ShortError(string value, int maximumLength) {
			return value.Length <= maximumLength ? value : value.Substring(0, maximumLength - 1) + "…";
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
