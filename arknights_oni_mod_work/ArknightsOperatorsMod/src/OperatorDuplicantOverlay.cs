using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Spine;
using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class OperatorDuplicantOverlay : MonoBehaviour {
		private const float TargetVisualHeight = 1.65f;
		private const float GroundOffsetY = 0f;
		private const float MinimumWorldScale = 0.0015f;
		private const float MaximumWorldScale = 0.0080f;
		private const float FrameZStep = -0.0001f;
		private const float ModelSwitchDelaySeconds = 0.35f;
		private const string PreferredFrameSkin = "播种者";
		private const string PreferredFrameModel = "正面";
		private static bool applicationQuitting;

		private KBatchedAnimController sourceAnim;
		private Navigator navigator;
		private GameObject visualRoot;
		private MeshFilter meshFilter;
		private MeshRenderer meshRenderer;
		private Mesh mesh;
		private UnityTextureLoader textureLoader;
		private Spine.Skeleton skeleton;
		private Spine.AnimationState state;
		private SkeletonData skeletonData;
		private string currentSpineAnimation;
		private bool sourceHidden;
		private bool frameFallbackMode;
		private int frameCount;
		private int frameColumns;
		private int frameWidth;
		private int frameHeight;
		private float frameFps;
		private float frameElapsed;
		private bool frameLoop;
		private string currentFrameAnimation;
		private OperatorAnimationPlan currentSpinePlan;
		private readonly List<FrameAnimationDef> frameLibrary = new List<FrameAnimationDef>();
		private readonly Dictionary<string, FrameSheetCache> frameSheetCache = new Dictionary<string, FrameSheetCache>();
		private readonly List<string> availableSpineAnimations = new List<string>();

		private readonly List<Vector3> vertices = new List<Vector3>(512);
		private readonly List<Vector2> uvs = new List<Vector2>(512);
		private readonly List<Color32> colors = new List<Color32>(512);
		private readonly List<int> frameTriangles = new List<int>(6);
		private readonly List<List<int>> submeshTriangleBuffers = new List<List<int>>();
		private readonly List<Material> submeshMaterials = new List<Material>();
		private Material[] assignedMaterials = new Material[0];
		private readonly SkeletonClipping clipper = new SkeletonClipping();
		private readonly float[] quad = new float[8];
		private float[] worldVertices = new float[8];
		private static readonly int[] QuadTriangles = { 0, 1, 2, 2, 3, 0 };
		private float rawMinX;
		private float rawMinY;
		private float rawMaxX;
		private float rawMaxY;
		private bool scaleInitialized;
		private float calibratedScale = 1f;
		private float calibratedCenterX;
		private float calibratedMinY;
		private CancellationTokenSource loadCancellation;
		private Task<OperatorAssetBundle> pendingBundleTask;
		private IDisposable resourceLease;
		private int loadGeneration;
		private OperatorAppearanceOverride appearanceOverride;
		private ModConfig previewAppearanceConfig;
		private ModConfig appearanceConfig;
		private string activeCharacterId;
		private string activeSkin;
		private string activeModel;
		private string loadingModel;
		private string modelCandidate;
		private string lastAttemptedModel;
		private float modelCandidateSince;
		private OperatorActionKind? manualAction;
		private readonly List<KBatchedAnimController> sourceAnimations = new List<KBatchedAnimController>();
		private readonly Dictionary<KBatchedAnimController, Color32> sourceTintBeforeSuppression =
			new Dictionary<KBatchedAnimController, Color32>();
		private readonly Dictionary<KBatchedAnimController, bool> sourceVisibilityBeforeSuppression =
			new Dictionary<KBatchedAnimController, bool>();
		private static readonly MethodInfo SuspendUpdatesMethod = typeof(KAnimControllerBase).GetMethod(
			"SuspendUpdates", BindingFlags.Instance | BindingFlags.NonPublic);

		private void Start() {
			try {
				sourceAnim = GetComponent<KBatchedAnimController>();
				navigator = GetComponent<Navigator>();
				appearanceOverride = GetComponent<OperatorAppearanceOverride>();
				if (appearanceOverride == null)
					appearanceOverride = gameObject.AddComponent<OperatorAppearanceOverride>();
				RefreshSourceAnimations();
				CreateVisualRoot();
				ModConfigStore.AppearanceChanged += OnAppearanceChanged;
				ModConfigStore.VisualScaleChanged += OnVisualScaleChanged;
				appearanceConfig = ResolveAppearanceConfig(ModConfigStore.Current);
				BeginAppearanceLoad(ConfigForEffectiveAnimation(CurrentEffectiveAnimation()), true);
			} catch (Exception ex) {
				Debug.LogError("[ArknightsOperatorsMod] Failed to attach overlay: " + ex);
				enabled = false;
			}
		}

		private void OnDisable() {
			loadGeneration++;
			CancelPendingAppearanceLoad();
		}

		private void OnApplicationQuit() {
			applicationQuitting = true;
		}

		private void OnDestroy() {
			ModConfigStore.AppearanceChanged -= OnAppearanceChanged;
			ModConfigStore.VisualScaleChanged -= OnVisualScaleChanged;
			loadGeneration++;
			CancelPendingAppearanceLoad();
			if (resourceLease != null) {
				resourceLease.Dispose();
				resourceLease = null;
			}
			if (applicationQuitting)
				ClearSourceVisualState();
			else
				RestoreSourceVisuals();
			if (visualRoot != null) Destroy(visualRoot);
			if (textureLoader != null) textureLoader.Dispose();
			foreach (FrameSheetCache cached in frameSheetCache.Values) {
				if (cached.Material != null) Destroy(cached.Material);
				if (cached.Texture != null) Destroy(cached.Texture);
			}
			frameSheetCache.Clear();
		}

		private void LateUpdate() {
			EnsureSourceVisualHidden();
			if (frameFallbackMode) {
				UpdateFrameFallback();
				return;
			}
			if (skeleton == null || state == null) return;

			string effectiveAnimation = CurrentEffectiveAnimation();
			UpdatePreferredModel(effectiveAnimation);
			SyncFacingAndAnimation(effectiveAnimation);
			float dt = Mathf.Min(Time.deltaTime, 0.05f);
			state.Update(dt);
			state.Apply(skeleton);
			skeleton.UpdateWorldTransform();
			RebuildMesh();
		}

		private void CreateVisualRoot() {
			visualRoot = new GameObject("ArknightsSpineOverlay");
			visualRoot.transform.SetParent(transform, false);
			visualRoot.transform.localPosition = new Vector3(0f, GroundOffsetY, -0.35f);
			visualRoot.transform.localRotation = Quaternion.identity;
			visualRoot.transform.localScale = Vector3.one;

			meshFilter = visualRoot.AddComponent<MeshFilter>();
			meshRenderer = visualRoot.AddComponent<MeshRenderer>();
			mesh = new Mesh();
			mesh.name = "ArknightsSpineOverlayMesh";
			mesh.MarkDynamic();
			meshFilter.sharedMesh = mesh;
		}

		private void OnAppearanceChanged(ModConfig config) {
			ApplyAppearanceConfig(ResolveAppearanceConfig(config));
		}

		private void OnVisualScaleChanged(ModConfig config) {
			if (config == null || appearanceConfig == null) return;
			appearanceConfig = ResolveAppearanceConfig(config);
			if (frameFallbackMode)
				ApplyFrameFallbackTransform();
			else
				ApplyCalibratedScale();
		}

		private void ApplyAppearanceConfig(ModConfig config) {
			if (config == null) return;
			if (appearanceConfig != null && string.Equals(
				ModConfigStore.AppearanceKey(appearanceConfig),
				ModConfigStore.AppearanceKey(config), StringComparison.Ordinal)) return;
			appearanceConfig = ModConfigStore.Clone(config);
			manualAction = null;
			lastAttemptedModel = null;
			modelCandidate = null;
			BeginAppearanceLoad(ConfigForEffectiveAnimation(CurrentEffectiveAnimation()), !sourceHidden);
		}

		private ModConfig ResolveAppearanceConfig(ModConfig globalConfig) {
			if (previewAppearanceConfig != null)
				return ModConfigStore.Clone(previewAppearanceConfig);
			return appearanceOverride == null ? ModConfigStore.Clone(globalConfig) :
				appearanceOverride.Resolve(globalConfig);
		}

		private void BeginAppearanceLoad(ModConfig config, bool allowFallback) {
			loadGeneration++;
			CancelPendingAppearanceLoad();
			loadCancellation = new CancellationTokenSource();
			loadingModel = config.PreferredModel;
			lastAttemptedModel = config.PreferredModel;
			StartCoroutine(LoadAppearance(config, allowFallback, loadGeneration,
				loadCancellation.Token));
		}

		private IEnumerator LoadAppearance(ModConfig config, bool allowFallback, int generation,
			CancellationToken cancellationToken) {
			OperatorAssetBundle bundle = null;
			Exception remoteError = null;
			PrtsResourceService service = PrtsResourceService.Instance;
			if (service != null) {
				Task<OperatorAssetBundle> task = new OperatorAssetResolver(service).ResolveAsync(
					config,
					cancellationToken
				);
				Task<OperatorAssetBundle> replaced = Interlocked.Exchange(ref pendingBundleTask, task);
				if (replaced != null && !object.ReferenceEquals(replaced, task))
					QueueBundleCleanup(replaced);
				while (!task.IsCompleted) {
					if (cancellationToken.IsCancellationRequested || generation != loadGeneration) {
						Interlocked.CompareExchange(ref pendingBundleTask, null, task);
						QueueBundleCleanup(task);
						yield break;
					}
					yield return null;
				}
				if (task.IsCanceled || cancellationToken.IsCancellationRequested ||
					generation != loadGeneration) {
					Interlocked.CompareExchange(ref pendingBundleTask, null, task);
					QueueBundleCleanup(task);
					yield break;
				}
				Interlocked.CompareExchange(ref pendingBundleTask, null, task);
				if (task.IsFaulted) remoteError = task.Exception.GetBaseException();
				else bundle = task.Result;
			} else {
				remoteError = new InvalidOperationException("PRTS resource service is not initialized");
			}

			bool remoteLoaded = false;
			if (bundle != null) {
				IDisposable nextLease = null;
				LoadedSpineVisual prepared = null;
				try {
					nextLease = bundle.TakeResourceLease();
					if (nextLease == null) nextLease = service.Acquire(bundle.ResourceKeys);
					prepared = PrepareSkeleton(bundle.AtlasPath, bundle.SkeletonPath);
					if (!cancellationToken.IsCancellationRequested && generation == loadGeneration) {
						ApplyPreparedAppearanceTransactional(prepared, bundle.CharacterId,
							bundle.Skin, bundle.Model);
						prepared = null;
						IDisposable previousLease = resourceLease;
						resourceLease = nextLease;
						nextLease = null;
						if (previousLease != null) {
							try {
								previousLease.Dispose();
							} catch (Exception error) {
								Debug.LogWarning("[ArknightsOperatorsMod] Previous resource lease cleanup failed: " +
									error.Message);
							}
						}
						remoteLoaded = true;
						Debug.Log("[ArknightsOperatorsMod] Loaded operator " + bundle.CharacterId + " " +
							bundle.Skin + "/" + bundle.Model + " version=" + bundle.ResourceVersion);
						Debug.Log("[ArknightsOperatorsMod] Operator overlay attached to " + gameObject.name);
					}
				} catch (Exception error) {
					remoteError = error;
				} finally {
					if (prepared != null) prepared.Dispose();
					if (nextLease != null) nextLease.Dispose();
					bundle.Dispose();
				}
			}
			if (remoteLoaded) yield break;

			if (cancellationToken.IsCancellationRequested || generation != loadGeneration)
				yield break;
			if (!allowFallback) {
				loadingModel = null;
				Debug.LogWarning("[ArknightsOperatorsMod] Appearance switch failed; keeping current operator: " +
					remoteError);
				yield break;
			}

			bool loaded = false;
			bool sourceAlreadyHidden = false;
			if (string.Equals(config.DefaultCharacterId, "char_002_amiya", StringComparison.OrdinalIgnoreCase)) {
				Debug.LogWarning("[ArknightsOperatorsMod] PRTS asset load failed; trying bundled fallback: " + remoteError);
				LoadedSpineVisual fallbackPrepared = null;
				try {
					fallbackPrepared = PrepareSkeleton(ModAssets.AmiyaAtlasPath, ModAssets.AmiyaSkeletonPath);
					ApplyPreparedAppearanceTransactional(fallbackPrepared, "char_002_amiya",
						"默认", "基建");
					fallbackPrepared = null;
					loaded = true;
					sourceAlreadyHidden = true;
				} catch (Exception spineError) {
					Debug.LogWarning("[ArknightsOperatorsMod] Bundled skeleton failed; trying frame fallback: " + spineError);
					try {
						LoadFrameFallback();
						loaded = true;
					} catch (Exception frameError) {
						Debug.LogError("[ArknightsOperatorsMod] All operator visuals failed; keeping vanilla duplicant: " + frameError);
					}
				} finally {
					if (fallbackPrepared != null) fallbackPrepared.Dispose();
				}
			} else {
				Debug.LogWarning("[ArknightsOperatorsMod] PRTS asset load failed for " +
					config.DefaultCharacterId + "; keeping vanilla duplicant instead of showing Amiya: " + remoteError);
			}

			if (loaded) {
				loadingModel = null;
				if (!sourceAlreadyHidden) HideSourceVisual();
				Debug.Log("[ArknightsOperatorsMod] Operator overlay attached to " + gameObject.name);
			} else {
				loadingModel = null;
				enabled = false;
			}
		}

		private void CancelPendingAppearanceLoad() {
			if (loadCancellation != null) {
				loadCancellation.Cancel();
				loadCancellation.Dispose();
				loadCancellation = null;
			}
			Task<OperatorAssetBundle> pending = Interlocked.Exchange(ref pendingBundleTask, null);
			if (pending != null)
				QueueBundleCleanup(pending);
		}

		private static void QueueBundleCleanup(Task<OperatorAssetBundle> task) {
			Task cleanup = OperatorAssetBundleLifecycle.DisposeWhenCompleteAsync(task);
			if (cleanup.IsFaulted)
				Debug.LogWarning("[ArknightsOperatorsMod] Abandoned bundle cleanup failed");
		}

		private string CurrentOniAnimation() {
			if (sourceAnim == null) return null;
			KAnim.Anim current = sourceAnim.GetCurrentAnim();
			if (current == null || string.IsNullOrEmpty(current.name)) {
				current = sourceAnim.GetAnim(sourceAnim.currentAnim);
			}
			string source = current != null && !string.IsNullOrEmpty(current.name)
				? current.name
				: sourceAnim.currentAnim.ToString();
			return OperatorAnimationMapper.ResolveSourceAnimation(
				source, IsSourceMoving());
		}

		private string CurrentEffectiveAnimation() {
			return OperatorAnimationMapper.ResolveEffectiveAnimation(CurrentOniAnimation(), manualAction);
		}

		internal void SetManualAction(OperatorActionKind? action) {
			manualAction = action;
			modelCandidate = null;
			lastAttemptedModel = activeModel;
			string effective = CurrentEffectiveAnimation();
			if (skeleton != null && state != null) PlayBestAnimation(effective);
			Debug.Log("[ArknightsOperatorsMod] Manual action " +
				(action.HasValue ? action.Value.ToString() : "Auto") + " for " + gameObject.name);
		}

		internal OperatorActionKind? ManualAction {
			get { return manualAction; }
		}

		internal string ActiveModel {
			get { return activeModel; }
		}

		internal string ActiveAppearanceScaleKey {
			get {
				return string.IsNullOrWhiteSpace(activeCharacterId) ||
					string.IsNullOrWhiteSpace(activeSkin) || string.IsNullOrWhiteSpace(activeModel) ?
					null : ModConfig.AppearanceScaleKey(activeCharacterId, activeSkin, activeModel);
			}
		}

		internal int ActiveVisualScalePercent {
			get {
				return appearanceConfig == null || string.IsNullOrEmpty(ActiveAppearanceScaleKey) ?
					ModConfig.DefaultVisualScalePercent : appearanceConfig.ResolveVisualScalePercent(
						activeCharacterId, activeSkin, activeModel);
			}
		}

		internal bool SetActiveVisualScalePercent(int scalePercent) {
			if (string.IsNullOrEmpty(ActiveAppearanceScaleKey) ||
				!ModConfig.IsValidVisualScalePercent(scalePercent)) return false;
			ModConfigStore.SetAppearanceVisualScale(activeCharacterId, activeSkin, activeModel,
				scalePercent);
			return true;
		}

		internal bool ResetActiveVisualScalePercent() {
			if (string.IsNullOrEmpty(ActiveAppearanceScaleKey)) return false;
			ModConfigStore.ResetAppearanceVisualScale(activeCharacterId, activeSkin, activeModel);
			return true;
		}

		internal string DuplicantName {
			get {
				MinionIdentity identity = GetComponent<MinionIdentity>();
				return identity == null ? gameObject.name : identity.GetProperName();
			}
		}

		internal bool HasIndividualAppearance {
			get { return appearanceOverride != null && appearanceOverride.HasOverride; }
		}

		internal ModConfig CurrentAppearanceConfig {
			get { return ResolveAppearanceConfig(ModConfigStore.Current); }
		}

		internal void SetIndividualAppearance(string characterId, string skin, string model) {
			if (appearanceOverride == null)
				appearanceOverride = gameObject.AddComponent<OperatorAppearanceOverride>();
			previewAppearanceConfig = null;
			appearanceOverride.Set(characterId, skin, model);
			ApplyAppearanceConfig(ResolveAppearanceConfig(ModConfigStore.Current));
			Debug.Log("[ArknightsOperatorsMod] Individual appearance " + characterId + " " +
				skin + "/" + model + " for " + DuplicantName);
		}

		internal void ClearIndividualAppearance() {
			previewAppearanceConfig = null;
			if (appearanceOverride == null || !appearanceOverride.HasOverride) return;
			appearanceOverride.Clear();
			ApplyAppearanceConfig(ResolveAppearanceConfig(ModConfigStore.Current));
			Debug.Log("[ArknightsOperatorsMod] Global appearance restored for " + DuplicantName);
		}

		internal void PreviewAppearance(string characterId, string skin, string model) {
			ModConfig preview = ModConfigStore.Clone(ModConfigStore.Current);
			preview.DefaultCharacterId = characterId;
			preview.PreferredSkin = skin;
			preview.PreferredModel = model;
			preview.Normalize();
			previewAppearanceConfig = preview;
			ApplyAppearanceConfig(preview);
		}

		internal void ClearPreviewAppearance() {
			if (previewAppearanceConfig == null) return;
			previewAppearanceConfig = null;
			ApplyAppearanceConfig(ResolveAppearanceConfig(ModConfigStore.Current));
		}

		private ModConfig ConfigForEffectiveAnimation(string effectiveAnimation) {
			ModConfig config = ModConfigStore.Clone(appearanceConfig);
			config.PreferredModel = OperatorAnimationMapper.PreferredModel(effectiveAnimation,
				config.AutomaticModelSwitching, config.PreferredModel);
			return config;
		}

		private void UpdatePreferredModel(string effectiveAnimation) {
			if (appearanceConfig == null) return;
			string desired = OperatorAnimationMapper.PreferredModel(effectiveAnimation,
				appearanceConfig.AutomaticModelSwitching, appearanceConfig.PreferredModel);
			if (ModelMatches(desired, activeModel)) {
				if (!string.IsNullOrEmpty(loadingModel) && !ModelMatches(desired, loadingModel))
					CancelPendingModelLoad();
				lastAttemptedModel = activeModel;
				modelCandidate = null;
				return;
			}
			if (ModelMatches(desired, loadingModel) ||
				string.Equals(desired, lastAttemptedModel, StringComparison.Ordinal)) {
				modelCandidate = null;
				return;
			}
			if (!string.Equals(desired, modelCandidate, StringComparison.Ordinal)) {
				modelCandidate = desired;
				modelCandidateSince = Time.unscaledTime;
				return;
			}
			if (Time.unscaledTime - modelCandidateSince < ModelSwitchDelaySeconds) return;

			ModConfig next = ModConfigStore.Clone(appearanceConfig);
			next.PreferredModel = desired;
			modelCandidate = null;
			BeginAppearanceLoad(next, false);
		}

		private void CancelPendingModelLoad() {
			loadGeneration++;
			CancelPendingAppearanceLoad();
			loadingModel = null;
		}

		private static bool ModelMatches(string desired, string actual) {
			if (string.IsNullOrEmpty(desired) || string.IsNullOrEmpty(actual)) return false;
			if (string.Equals(desired, actual, StringComparison.OrdinalIgnoreCase)) return true;
			return string.Equals(desired, "正面", StringComparison.Ordinal) &&
				string.Equals(actual, "战斗", StringComparison.Ordinal);
		}

		private LoadedSpineVisual PrepareSkeleton(string atlasPath, string skeletonPath) {
			if (string.IsNullOrEmpty(ModAssets.ModPath)) {
				throw new InvalidOperationException("Mod path is not initialized.");
			}
			if (!File.Exists(atlasPath)) {
				throw new FileNotFoundException("Missing operator atlas", atlasPath);
			}
			if (!File.Exists(skeletonPath)) {
				throw new FileNotFoundException("Missing operator skeleton", skeletonPath);
			}

			LoadedSpineVisual loaded = new LoadedSpineVisual();
			try {
				loaded.TextureLoader = new UnityTextureLoader();
				loaded.Atlas = new Atlas(atlasPath, loaded.TextureLoader);
				SkeletonBinary binary = new SkeletonBinary(new AtlasAttachmentLoader(loaded.Atlas));
				binary.Scale = 1f;
				using (FileStream stream = File.OpenRead(skeletonPath)) {
					loaded.SkeletonData = binary.ReadSkeletonData(stream);
				}
				if (loaded.SkeletonData == null || loaded.SkeletonData.Bones.Count == 0 ||
					loaded.SkeletonData.Slots.Count == 0 || loaded.SkeletonData.Animations.Count == 0)
					throw new InvalidDataException("Operator skeleton parsed to an empty structure");

				loaded.Skeleton = new Spine.Skeleton(loaded.SkeletonData);
				loaded.Skeleton.SetToSetupPose();
				loaded.State = new Spine.AnimationState(new AnimationStateData(loaded.SkeletonData));
				if (loaded.TextureLoader.FirstMaterial == null)
					throw new InvalidOperationException("No material was created from the atlas.");
				return loaded;
			} catch {
				loaded.Dispose();
				throw;
			}
		}

		private void ApplyPreparedAppearanceTransactional(LoadedSpineVisual loaded, string characterId,
			string skin, string model) {
			if (loaded == null) throw new ArgumentNullException("loaded");
			Mesh previousMesh = mesh;
			Mesh nextMesh = new Mesh { name = "ArknightsSpineOverlayMesh" };
			nextMesh.MarkDynamic();
			UnityTextureLoader previousTextureLoader = textureLoader;
			Spine.Skeleton previousSkeleton = skeleton;
			Spine.AnimationState previousState = state;
			SkeletonData previousSkeletonData = skeletonData;
			Material[] previousRendererMaterials = meshRenderer.sharedMaterials;
			Material[] previousAssignedMaterials = assignedMaterials;
			int previousSortingOrder = meshRenderer.sortingOrder;
			bool previousFrameFallbackMode = frameFallbackMode;
			string previousSpineAnimation = currentSpineAnimation;
			OperatorAnimationPlan previousSpinePlan = currentSpinePlan;
			string[] previousAvailableAnimations = availableSpineAnimations.ToArray();
			bool previousScaleInitialized = scaleInitialized;
			float previousCalibratedScale = calibratedScale;
			float previousCalibratedCenterX = calibratedCenterX;
			float previousCalibratedMinY = calibratedMinY;
			float previousRawMinX = rawMinX;
			float previousRawMinY = rawMinY;
			float previousRawMaxX = rawMaxX;
			float previousRawMaxY = rawMaxY;
			Vector3 previousVisualPosition = visualRoot.transform.localPosition;
			Vector3 previousVisualScale = visualRoot.transform.localScale;
			string previousActiveCharacterId = activeCharacterId;
			string previousActiveSkin = activeSkin;
			string previousActiveModel = activeModel;
			string previousLoadingModel = loadingModel;
			bool previousSourceHidden = sourceHidden;
			try {
				mesh = nextMesh;
				meshFilter.sharedMesh = nextMesh;
				activeCharacterId = characterId;
				activeSkin = skin;
				activeModel = model;
				ApplySkeleton(loaded);
				loadingModel = null;
				PlayBestAnimation(CurrentEffectiveAnimation());
				HideSourceVisual();
			} catch {
				textureLoader = previousTextureLoader;
				skeleton = previousSkeleton;
				state = previousState;
				skeletonData = previousSkeletonData;
				frameFallbackMode = previousFrameFallbackMode;
				currentSpineAnimation = previousSpineAnimation;
				currentSpinePlan = previousSpinePlan;
				availableSpineAnimations.Clear();
				availableSpineAnimations.AddRange(previousAvailableAnimations);
				scaleInitialized = previousScaleInitialized;
				calibratedScale = previousCalibratedScale;
				calibratedCenterX = previousCalibratedCenterX;
				calibratedMinY = previousCalibratedMinY;
				rawMinX = previousRawMinX;
				rawMinY = previousRawMinY;
				rawMaxX = previousRawMaxX;
				rawMaxY = previousRawMaxY;
				activeCharacterId = previousActiveCharacterId;
				activeSkin = previousActiveSkin;
				activeModel = previousActiveModel;
				loadingModel = previousLoadingModel;
				assignedMaterials = previousAssignedMaterials;
				mesh = previousMesh;
				meshFilter.sharedMesh = previousMesh;
				meshRenderer.sharedMaterials = previousRendererMaterials;
				meshRenderer.sortingOrder = previousSortingOrder;
				visualRoot.transform.localPosition = previousVisualPosition;
				visualRoot.transform.localScale = previousVisualScale;
				if (!previousSourceHidden) {
					try {
						RestoreSourceVisuals();
					} catch (Exception restoreError) {
						Debug.LogWarning("[ArknightsOperatorsMod] Source visual rollback failed: " +
							restoreError.Message);
					}
				} else {
					sourceHidden = true;
				}
				Destroy(nextMesh);
				throw;
			}

			if (previousTextureLoader != null && previousTextureLoader != textureLoader) {
				try {
					previousTextureLoader.Dispose();
				} catch (Exception error) {
					Debug.LogWarning("[ArknightsOperatorsMod] Previous texture cleanup failed: " +
						error.Message);
				}
			}
			if (previousMesh != null && previousMesh != mesh) Destroy(previousMesh);
		}

		private void ApplySkeleton(LoadedSpineVisual loaded) {
			textureLoader = loaded.TextureLoader;
			skeletonData = loaded.SkeletonData;
			skeleton = loaded.Skeleton;
			state = loaded.State;
			Material material = textureLoader.FirstMaterial;
			meshRenderer.sharedMaterial = material;
			meshRenderer.sortingOrder = 100;
			mesh.Clear();
			frameFallbackMode = false;
			availableSpineAnimations.Clear();
			currentSpineAnimation = null;
			currentSpinePlan = null;
			scaleInitialized = false;
			InitializeScaleFromReferenceAnimation();

			LogAvailableAnimations();
		}

		private void LoadFrameFallback() {
			activeCharacterId = "char_002_amiya";
			LoadFrameLibrary();
			FrameAnimationDef initial = PickFrameAnimation(null);
			if (initial != null) {
				LoadFrameFallback(initial);
			} else {
				activeSkin = PreferredFrameSkin;
				activeModel = PreferredFrameModel;
				LoadFrameFallback(ModAssets.AmiyaFrameManifestPath);
			}
		}

		private void LoadFrameFallback(FrameAnimationDef definition) {
			string previousSkin = activeSkin;
			string previousModel = activeModel;
			activeSkin = definition.Skin;
			activeModel = definition.Model;
			try {
				LoadFrameFallback(definition.ManifestPath);
			} catch {
				activeSkin = previousSkin;
				activeModel = previousModel;
				throw;
			}
		}

		private void LoadFrameFallback(string manifestPath) {
			if (!File.Exists(manifestPath)) {
				throw new FileNotFoundException("Missing Amiya frame fallback manifest", manifestPath);
			}

			string json = File.ReadAllText(manifestPath);
			string sheetName = ReadJsonString(json, "sheet", "sheet.png");
			frameFps = ReadJsonFloat(json, "fps", 12f);
			frameCount = ReadJsonInt(json, "frame_count", 1);
			frameColumns = ReadJsonInt(json, "columns", frameCount);
			frameWidth = ReadJsonInt(json, "frame_width", 1000);
			frameHeight = ReadJsonInt(json, "frame_height", 1000);

			string sheetPath = Path.Combine(Path.GetDirectoryName(manifestPath), sheetName);
			if (!File.Exists(sheetPath)) {
				throw new FileNotFoundException("Missing Amiya frame fallback sheet", sheetPath);
			}

			FrameSheetCache sheet = GetFrameSheet(sheetPath);
			visualRoot.transform.localPosition = new Vector3(0f, GroundOffsetY, -0.35f);
			ApplyFrameFallbackTransform();
			meshRenderer.sharedMaterial = sheet.Material;
			meshRenderer.sortingOrder = 100;

			frameFallbackMode = true;
			frameElapsed = 0f;
			currentFrameAnimation = ReadJsonString(json, "animation", Path.GetFileName(Path.GetDirectoryName(manifestPath)));
			frameLoop = !string.Equals(currentFrameAnimation, "Die", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(currentFrameAnimation, "Start", StringComparison.OrdinalIgnoreCase) &&
				!currentFrameAnimation.EndsWith("_Begin", StringComparison.OrdinalIgnoreCase) &&
				!currentFrameAnimation.EndsWith("_End", StringComparison.OrdinalIgnoreCase);
			BuildFrameFallbackQuad();
			ApplyFrameFallbackUv(0);
			Debug.Log("[ArknightsOperatorsMod] Loaded frame fallback: " + sheetPath + " animation=" + currentFrameAnimation + " frames=" + frameCount + " fps=" + frameFps);
		}

		private FrameSheetCache GetFrameSheet(string sheetPath) {
			FrameSheetCache cached;
			if (frameSheetCache.TryGetValue(sheetPath, out cached)) return cached;

			byte[] bytes = File.ReadAllBytes(sheetPath);
			Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
			texture.name = Path.GetFileName(sheetPath);
			texture.LoadImage(bytes);
			texture.filterMode = FilterMode.Bilinear;
			texture.wrapMode = TextureWrapMode.Clamp;

			Shader shader = Shader.Find("Klei/Unlit");
			if (shader == null) shader = Shader.Find("Sprites/Default");
			if (shader == null) shader = Shader.Find("Unlit/Transparent");
			Material material = new Material(shader);
			material.name = "AmiyaFrameFallbackMaterial";
			material.mainTexture = texture;

			cached = new FrameSheetCache(texture, material);
			frameSheetCache[sheetPath] = cached;
			return cached;
		}

		private void LoadFrameLibrary() {
			frameLibrary.Clear();
			if (!File.Exists(ModAssets.AmiyaFrameLibraryIndexPath)) {
				Debug.Log("[ArknightsOperatorsMod] Frame library index not found; using root fallback only.");
				return;
			}

			JObject root = JObject.Parse(File.ReadAllText(ModAssets.AmiyaFrameLibraryIndexPath));
			JArray animations = root["animations"] as JArray;
			if (animations == null) return;

			for (int i = 0; i < animations.Count; i++) {
				JObject item = animations[i] as JObject;
				if (item == null) continue;
				string skin = (string)item["skin"];
				string model = (string)item["model"];
				string animation = (string)item["animation"];
				string path = (string)item["path"];
				if (string.IsNullOrEmpty(skin) || string.IsNullOrEmpty(model) || string.IsNullOrEmpty(animation) || string.IsNullOrEmpty(path)) continue;
				string manifestPath = Path.Combine(ModAssets.ModPath, path.Replace('/', Path.DirectorySeparatorChar), "manifest.json");
				if (File.Exists(manifestPath)) frameLibrary.Add(new FrameAnimationDef(skin, model, animation, manifestPath));
			}

			Debug.Log("[ArknightsOperatorsMod] Loaded frame library entries: " + frameLibrary.Count);
		}

		private void BuildFrameFallbackQuad() {
			float height = 3.0f;
			float width = height * ((float)frameWidth / Mathf.Max(1f, frameHeight));
			vertices.Clear();
			uvs.Clear();
			colors.Clear();
			frameTriangles.Clear();

			vertices.Add(new Vector3(-width * 0.5f, 0f, 0f));
			vertices.Add(new Vector3(width * 0.5f, 0f, 0f));
			vertices.Add(new Vector3(width * 0.5f, height, 0f));
			vertices.Add(new Vector3(-width * 0.5f, height, 0f));
			for (int i = 0; i < 4; i++) colors.Add(Color.white);
			uvs.Add(Vector2.zero);
			uvs.Add(Vector2.zero);
			uvs.Add(Vector2.zero);
			uvs.Add(Vector2.zero);
			frameTriangles.Add(0);
			frameTriangles.Add(1);
			frameTriangles.Add(2);
			frameTriangles.Add(2);
			frameTriangles.Add(3);
			frameTriangles.Add(0);

			mesh.Clear();
			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.SetColors(colors);
			mesh.SetTriangles(frameTriangles, 0);
			mesh.RecalculateBounds();
		}

		private void UpdateFrameFallback() {
			if (frameCount <= 0 || frameFps <= 0f) return;
			SyncFrameFallbackFacingAndAnimation(CurrentEffectiveAnimation());
			frameElapsed += Time.deltaTime;
			int elapsedFrame = Mathf.FloorToInt(frameElapsed * frameFps);
			int frameIndex = frameLoop ? elapsedFrame % frameCount : Mathf.Min(elapsedFrame, frameCount - 1);
			ApplyFrameFallbackUv(frameIndex);
		}

		private void SyncFrameFallbackFacingAndAnimation(string effectiveAnimation) {
			if (sourceAnim == null) return;
			ApplyFrameFallbackTransform();

			FrameAnimationDef next = PickFrameAnimation(effectiveAnimation);
			if (next == null || next.Animation == currentFrameAnimation) return;
			try {
				LoadFrameFallback(next);
			} catch (Exception ex) {
				Debug.LogWarning("[ArknightsOperatorsMod] Failed to switch frame animation to " + next.Animation + ": " + ex);
			}
		}

		private FrameAnimationDef PickFrameAnimation(string oniAnim) {
			if (frameLibrary.Count == 0) return null;
			List<string> names = new List<string>();
			for (int i = 0; i < frameLibrary.Count; i++) {
				FrameAnimationDef entry = frameLibrary[i];
				if (entry.Skin == PreferredFrameSkin && entry.Model == PreferredFrameModel && !names.Contains(entry.Animation)) {
					names.Add(entry.Animation);
				}
			}
			if (names.Count == 0) {
				for (int i = 0; i < frameLibrary.Count; i++) {
					if (!names.Contains(frameLibrary[i].Animation)) names.Add(frameLibrary[i].Animation);
				}
			}
			string selected = OperatorAnimationMapper.Pick(oniAnim, names);
			FrameAnimationDef match = FindFrameAnimationByName(selected, true);
			return match ?? frameLibrary[0];
		}

		private FrameAnimationDef FindFrameAnimationByName(string name, bool exact) {
			string loweredName = name.ToLowerInvariant();
			FrameAnimationDef fallback = null;
			for (int i = 0; i < frameLibrary.Count; i++) {
				FrameAnimationDef entry = frameLibrary[i];
				bool modelMatch = entry.Skin == PreferredFrameSkin && entry.Model == PreferredFrameModel;
				string animation = entry.Animation.ToLowerInvariant();
				bool animationMatch = exact ? animation == loweredName : animation.Contains(loweredName);
				if (!animationMatch) continue;
				if (modelMatch) return entry;
				if (fallback == null) fallback = entry;
			}
			return fallback;
		}

		private void ApplyFrameFallbackUv(int frameIndex) {
			if (frameColumns <= 0) frameColumns = frameCount;
			int col = frameIndex % frameColumns;
			int row = frameIndex / frameColumns;
			float u0 = (float)(col * frameWidth) / Mathf.Max(1, meshRenderer.sharedMaterial.mainTexture.width);
			float vTop = (float)(row * frameHeight) / Mathf.Max(1, meshRenderer.sharedMaterial.mainTexture.height);
			float u1 = (float)((col + 1) * frameWidth) / Mathf.Max(1, meshRenderer.sharedMaterial.mainTexture.width);
			float vBottom = (float)((row + 1) * frameHeight) / Mathf.Max(1, meshRenderer.sharedMaterial.mainTexture.height);
			float v0 = 1f - vBottom;
			float v1 = 1f - vTop;

			uvs[0] = new Vector2(u0, v0);
			uvs[1] = new Vector2(u1, v0);
			uvs[2] = new Vector2(u1, v1);
			uvs[3] = new Vector2(u0, v1);
			mesh.SetUVs(0, uvs);
		}

		private static string ReadJsonString(string json, string key, string fallback) {
			Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
			return match.Success ? match.Groups[1].Value : fallback;
		}

		private static int ReadJsonInt(string json, string key, int fallback) {
			Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
			int value;
			return match.Success && int.TryParse(match.Groups[1].Value, out value) ? value : fallback;
		}

		private static float ReadJsonFloat(string json, string key, float fallback) {
			Match match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+(?:\\.\\d+)?)");
			float value;
			return match.Success && float.TryParse(match.Groups[1].Value, out value) ? value : fallback;
		}

		private void HideSourceVisual() {
			sourceHidden = true;
			SuppressSourceVisuals();
		}

		private void EnsureSourceVisualHidden() {
			if (sourceHidden) SuppressSourceVisuals();
		}

		private void RefreshSourceAnimations() {
			sourceAnimations.Clear();
			KBatchedAnimController[] controllers = GetComponentsInChildren<KBatchedAnimController>(true);
			for (int i = 0; i < controllers.Length; i++) {
				KBatchedAnimController controller = controllers[i];
				if (controller == null) continue;
				Pickupable pickupable = controller.GetComponentInParent<Pickupable>();
				if (pickupable != null && pickupable.gameObject != gameObject) continue;
				if (!sourceAnimations.Contains(controller)) sourceAnimations.Add(controller);
			}
		}

		private void SuppressSourceVisuals() {
			RefreshSourceAnimations();
			for (int i = 0; i < sourceAnimations.Count; i++) {
				KBatchedAnimController controller = sourceAnimations[i];
				if (controller == null) continue;
				if (!sourceVisibilityBeforeSuppression.ContainsKey(controller)) {
					sourceVisibilityBeforeSuppression[controller] = controller.IsVisible();
				}
				controller.SetVisiblity(false);
				if (SuspendUpdatesMethod != null) SuspendUpdatesMethod.Invoke(controller, new object[] { false });
				Color32 tint = controller.TintColour;
				Color32 originalTint;
				if (!sourceTintBeforeSuppression.TryGetValue(controller, out originalTint) || tint.a != 0) {
					sourceTintBeforeSuppression[controller] = tint;
				}
				if (tint.a == 0) continue;
				tint.a = 0;
				controller.TintColour = tint;
			}
		}

		private void RestoreSourceVisuals() {
			try {
				foreach (KeyValuePair<KBatchedAnimController, Color32> entry in sourceTintBeforeSuppression) {
					if (entry.Key == null) continue;
					try {
						entry.Key.TintColour = entry.Value;
					} catch (Exception error) {
						Debug.LogWarning("[ArknightsOperatorsMod] Skipped source tint restore: " +
							error.Message);
					}
				}
				foreach (KeyValuePair<KBatchedAnimController, bool> entry in sourceVisibilityBeforeSuppression) {
					if (entry.Key == null) continue;
					try {
						entry.Key.SetVisiblity(entry.Value);
					} catch (Exception error) {
						Debug.LogWarning("[ArknightsOperatorsMod] Skipped source visibility restore: " +
							error.Message);
					}
				}
			} finally {
				ClearSourceVisualState();
			}
		}

		private void ClearSourceVisualState() {
			sourceHidden = false;
			sourceTintBeforeSuppression.Clear();
			sourceVisibilityBeforeSuppression.Clear();
			sourceAnimations.Clear();
		}

		private bool IsSourceMoving() {
			if (navigator != null && navigator.IsMoving()) return true;
			for (int i = 0; i < sourceAnimations.Count; i++) {
				KBatchedAnimController controller = sourceAnimations[i];
				if (controller != null && controller.IsMoving) return true;
			}
			return false;
		}

		private void SyncFacingAndAnimation(string effectiveAnimation) {
			if (sourceAnim == null) return;
			bool flipX = sourceAnim.FlipX;
			skeleton.ScaleX = flipX ? -1f : 1f;
			if (scaleInitialized) {
				Vector3 position = visualRoot.transform.localPosition;
				position.x = (flipX ? calibratedCenterX : -calibratedCenterX) *
					CurrentWorldScale();
				visualRoot.transform.localPosition = position;
			}
			PlayBestAnimation(effectiveAnimation);
		}

		private void PlayBestAnimation(string oniAnim) {
			OperatorAnimationPlan next = OperatorAnimationMapper.BuildPlan(oniAnim, GetAvailableSpineAnimations());
			if (next == null || (next.Target == currentSpineAnimation && currentSpinePlan != null && next.Action == currentSpinePlan.Action)) return;

			bool leavingPhasedAction = currentSpinePlan != null && !string.IsNullOrEmpty(currentSpinePlan.End) &&
				(currentSpinePlan.Action == OperatorActionKind.Work || currentSpinePlan.Action == OperatorActionKind.Combat) &&
				next.Action != currentSpinePlan.Action;
			bool urgentAction = next.Action == OperatorActionKind.Death || next.Action == OperatorActionKind.Stress;
			if (urgentAction && !string.IsNullOrEmpty(next.Begin)) {
				state.SetAnimation(0, next.Begin, false);
				state.AddAnimation(0, next.Target, next.Loop, 0f);
			} else if (urgentAction) {
				state.SetAnimation(0, next.Target, next.Loop);
			} else if (leavingPhasedAction) {
				state.SetAnimation(0, currentSpinePlan.End, false);
				if (!string.IsNullOrEmpty(next.Begin)) state.AddAnimation(0, next.Begin, false, 0f);
				state.AddAnimation(0, next.Target, next.Loop, 0f);
			} else if (!string.IsNullOrEmpty(next.Begin)) {
				state.SetAnimation(0, next.Begin, false);
				state.AddAnimation(0, next.Target, next.Loop, 0f);
			} else {
				state.SetAnimation(0, next.Target, next.Loop);
			}
			currentSpinePlan = next;
			currentSpineAnimation = next.Target;
			Debug.Log("[ArknightsOperatorsMod] Animation source=" + (oniAnim ?? "<none>") +
				" action=" + next.Action + " target=" + next.Target);
		}

		private IList<string> GetAvailableSpineAnimations() {
			if (skeletonData == null || skeletonData.Animations.Count == 0) return null;
			if (availableSpineAnimations.Count != skeletonData.Animations.Count) {
				availableSpineAnimations.Clear();
				for (int i = 0; i < skeletonData.Animations.Count; i++) {
					availableSpineAnimations.Add(skeletonData.Animations.Items[i].Name);
				}
			}
			return availableSpineAnimations;
		}

		private void RebuildMesh() {
			vertices.Clear();
			uvs.Clear();
			colors.Clear();
			submeshMaterials.Clear();
			for (int i = 0; i < submeshTriangleBuffers.Count; i++) submeshTriangleBuffers[i].Clear();
			rawMinX = rawMinY = float.PositiveInfinity;
			rawMaxX = rawMaxY = float.NegativeInfinity;
			clipper.ClipEnd();

			ExposedList<Slot> drawOrder = skeleton.DrawOrder;
			for (int i = 0; i < drawOrder.Count; i++) {
				Slot slot = drawOrder.Items[i];
				if (slot == null || slot.Attachment == null) {
					if (slot != null) clipper.ClipEnd(slot);
					continue;
				}

				ClippingAttachment clippingAttachment = slot.Attachment as ClippingAttachment;
				if (clippingAttachment != null) {
					clipper.ClipStart(slot, clippingAttachment);
					continue;
				}

				RegionAttachment regionAttachment = slot.Attachment as RegionAttachment;
				if (regionAttachment != null) {
					AddRegion(slot, regionAttachment, i);
				} else {
					MeshAttachment meshAttachment = slot.Attachment as MeshAttachment;
					if (meshAttachment != null) AddMesh(slot, meshAttachment, i);
				}
				clipper.ClipEnd(slot);
			}
			clipper.ClipEnd();

			mesh.Clear();
			if (vertices.Count == 0) {
				mesh.subMeshCount = 0;
				return;
			}
			ConfigureScaleFromBounds();
			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.SetColors(colors);
			mesh.subMeshCount = submeshMaterials.Count;
			for (int i = 0; i < submeshMaterials.Count; i++) mesh.SetTriangles(submeshTriangleBuffers[i], i);
			ApplyMaterials();
			mesh.RecalculateBounds();
		}

		private void AddRegion(Slot slot, RegionAttachment attachment, int drawIndex) {
			attachment.ComputeWorldVertices(slot.Bone, quad, 0, 2);
			Material material = ResolveMaterial(attachment.RendererObject, slot.Data.BlendMode);
			AddAttachment(slot, quad, 8, attachment.UVs, QuadTriangles, QuadTriangles.Length,
				attachment.R, attachment.G, attachment.B, attachment.A, drawIndex, material);
		}

		private void AddMesh(Slot slot, MeshAttachment attachment, int drawIndex) {
			int length = attachment.WorldVerticesLength;
			if (worldVertices.Length < length) worldVertices = new float[length];
			attachment.ComputeWorldVertices(slot, 0, length, worldVertices, 0, 2);
			Material material = ResolveMaterial(attachment.RendererObject, slot.Data.BlendMode);
			AddAttachment(slot, worldVertices, length, attachment.UVs, attachment.Triangles, attachment.Triangles.Length,
				attachment.R, attachment.G, attachment.B, attachment.A, drawIndex, material);
		}

		private void AddAttachment(Slot slot, float[] attachmentVertices, int verticesLength, float[] attachmentUvs,
			int[] attachmentTriangles, int trianglesLength, float ar, float ag, float ab, float aa,
			int drawIndex, Material material) {
			if (material == null) return;
			if (clipper.IsClipping) {
				clipper.ClipTriangles(attachmentVertices, verticesLength, attachmentTriangles, trianglesLength, attachmentUvs);
				attachmentVertices = clipper.ClippedVertices.Items;
				verticesLength = clipper.ClippedVertices.Count;
				attachmentUvs = clipper.ClippedUVs.Items;
				attachmentTriangles = clipper.ClippedTriangles.Items;
				trianglesLength = clipper.ClippedTriangles.Count;
			}
			if (verticesLength == 0 || trianglesLength == 0) return;

			int baseIndex = vertices.Count;
			for (int i = 0; i < verticesLength; i += 2) {
				AddVertex(attachmentVertices[i], attachmentVertices[i + 1], attachmentUvs[i], attachmentUvs[i + 1],
					slot, ar, ag, ab, aa, drawIndex);
			}
			List<int> triangles = GetTriangleBuffer(material);
			for (int i = 0; i < trianglesLength; i++) triangles.Add(baseIndex + attachmentTriangles[i]);
		}

		private List<int> GetTriangleBuffer(Material material) {
			int index = submeshMaterials.Count - 1;
			if (index >= 0 && submeshMaterials[index] == material) return submeshTriangleBuffers[index];
			int next = submeshMaterials.Count;
			if (submeshTriangleBuffers.Count <= next) submeshTriangleBuffers.Add(new List<int>(256));
			submeshMaterials.Add(material);
			return submeshTriangleBuffers[next];
		}

		private Material ResolveMaterial(object rendererObject, Spine.BlendMode blendMode) {
			AtlasRegion region = rendererObject as AtlasRegion;
			return region == null || textureLoader == null ? null : textureLoader.GetMaterial(region.page, blendMode);
		}

		private void AddVertex(float x, float y, float u, float v, Slot slot, float ar, float ag, float ab, float aa, int drawIndex) {
			vertices.Add(new Vector3(x, y, drawIndex * FrameZStep));
			uvs.Add(new Vector2(u, 1f - v));
			colors.Add(new Color(
				skeleton.R * slot.R * ar,
				skeleton.G * slot.G * ag,
				skeleton.B * slot.B * ab,
				skeleton.A * slot.A * aa
			));
			if (x < rawMinX) rawMinX = x;
			if (x > rawMaxX) rawMaxX = x;
			if (y < rawMinY) rawMinY = y;
			if (y > rawMaxY) rawMaxY = y;
		}

		private void ConfigureScaleFromBounds() {
			if (scaleInitialized || float.IsInfinity(rawMinY) || rawMaxY <= rawMinY) return;
			float centerX = (rawMinX + rawMaxX) * 0.5f;
			calibratedScale = Mathf.Clamp(TargetVisualHeight / (rawMaxY - rawMinY),
				MinimumWorldScale, MaximumWorldScale);
			calibratedCenterX = centerX;
			calibratedMinY = rawMinY;
			scaleInitialized = true;
			ApplyCalibratedScale();
			Debug.Log("[ArknightsOperatorsMod] Auto scale=" + CurrentWorldScale() + " rawBounds=" +
				(rawMaxX - rawMinX) + "x" + (rawMaxY - rawMinY));
		}

		private float VisualScaleMultiplier() {
			int percent = appearanceConfig == null ? ModConfig.DefaultVisualScalePercent :
				string.IsNullOrEmpty(ActiveAppearanceScaleKey) ? appearanceConfig.VisualScalePercent :
				appearanceConfig.ResolveVisualScalePercent(activeCharacterId, activeSkin, activeModel);
			if (!ModConfig.IsValidVisualScalePercent(percent))
				percent = ModConfig.DefaultVisualScalePercent;
			return percent / 100f;
		}

		private float CurrentWorldScale() {
			return calibratedScale * VisualScaleMultiplier();
		}

		private void ApplyCalibratedScale() {
			if (!scaleInitialized || visualRoot == null) return;
			float scale = CurrentWorldScale();
			bool flipX = sourceAnim != null && sourceAnim.FlipX;
			visualRoot.transform.localScale = new Vector3(scale, scale, scale);
			visualRoot.transform.localPosition = new Vector3(
				(flipX ? calibratedCenterX : -calibratedCenterX) * scale,
				GroundOffsetY - calibratedMinY * scale,
				-0.35f
			);
		}

		private void ApplyFrameFallbackTransform() {
			if (visualRoot == null) return;
			float scale = VisualScaleMultiplier();
			bool flipX = sourceAnim != null && sourceAnim.FlipX;
			visualRoot.transform.localScale = new Vector3(flipX ? -scale : scale, scale, scale);
			visualRoot.transform.localPosition = new Vector3(0f, GroundOffsetY, -0.35f);
		}

		private void InitializeScaleFromReferenceAnimation() {
			skeleton.SetToSetupPose();
			string reference = FindReferenceAnimation();
			if (!string.IsNullOrEmpty(reference)) {
				state.SetAnimation(0, reference, false);
				state.Apply(skeleton);
			}
			skeleton.UpdateWorldTransform();
			RebuildMesh();
			state.ClearTracks();
			skeleton.SetToSetupPose();
			Debug.Log("[ArknightsOperatorsMod] Scale reference=" + (reference ?? "setup"));
		}

		private string FindReferenceAnimation() {
			string[] preferred = { "Default", "Idle", "Relax", "Move" };
			IList<string> available = GetAvailableSpineAnimations();
			if (available == null) return null;
			for (int p = 0; p < preferred.Length; p++) {
				for (int i = 0; i < available.Count; i++) {
					if (string.Equals(available[i], preferred[p], StringComparison.OrdinalIgnoreCase))
						return available[i];
				}
			}
			return null;
		}

		private void ApplyMaterials() {
			bool changed = assignedMaterials.Length != submeshMaterials.Count;
			if (!changed) {
				for (int i = 0; i < assignedMaterials.Length; i++) {
					if (assignedMaterials[i] != submeshMaterials[i]) {
						changed = true;
						break;
					}
				}
			}
			if (!changed) return;
			assignedMaterials = submeshMaterials.ToArray();
			meshRenderer.sharedMaterials = assignedMaterials;
		}

		private void LogAvailableAnimations() {
			if (skeletonData == null) return;
			List<string> names = new List<string>();
			for (int i = 0; i < skeletonData.Animations.Count; i++) {
				names.Add(skeletonData.Animations.Items[i].Name);
			}
			Debug.Log("[ArknightsOperatorsMod] Loaded Spine " + skeletonData.Version + " animations: " + string.Join(", ", names.ToArray()));
		}

		private sealed class LoadedSpineVisual : IDisposable {
			public Atlas Atlas;
			public UnityTextureLoader TextureLoader;
			public Spine.Skeleton Skeleton;
			public Spine.AnimationState State;
			public SkeletonData SkeletonData;

			public void Dispose() {
				if (TextureLoader != null) {
					TextureLoader.Dispose();
					TextureLoader = null;
				}
			}
		}

		private sealed class UnityTextureLoader : TextureLoader {
			public Material FirstMaterial;
			private readonly List<Texture2D> textures = new List<Texture2D>();
			private readonly List<Material> materials = new List<Material>();
			private readonly List<MaterialVariant> variants = new List<MaterialVariant>();

			public void Load(AtlasPage page, string path) {
				byte[] bytes = File.ReadAllBytes(path);
				Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
				texture.name = Path.GetFileName(path);
				texture.LoadImage(bytes);
				texture.filterMode = FilterMode.Bilinear;
				texture.wrapMode = TextureWrapMode.Clamp;

				Shader shader = Shader.Find("Klei/Unlit");
				if (shader == null) shader = Shader.Find("Sprites/Default");
				if (shader == null) shader = Shader.Find("Unlit/Transparent");
				Material material = new Material(shader);
				material.name = "ArknightsSpineMaterial_" + Path.GetFileNameWithoutExtension(path);
				material.mainTexture = texture;
				ConfigureBlend(material, Spine.BlendMode.Normal);
				page.rendererObject = material;
				textures.Add(texture);
				materials.Add(material);
				if (FirstMaterial == null) FirstMaterial = material;
			}

			public Material GetMaterial(AtlasPage page, Spine.BlendMode blendMode) {
				Material baseMaterial = page == null ? null : page.rendererObject as Material;
				if (baseMaterial == null || blendMode == Spine.BlendMode.Normal) return baseMaterial;
				for (int i = 0; i < variants.Count; i++) {
					MaterialVariant variant = variants[i];
					if (variant.Base == baseMaterial && variant.Blend == blendMode) return variant.Material;
				}
				Material material = new Material(baseMaterial);
				material.name = baseMaterial.name + "_" + blendMode;
				ConfigureBlend(material, blendMode);
				materials.Add(material);
				variants.Add(new MaterialVariant(baseMaterial, blendMode, material));
				return material;
			}

			private static void ConfigureBlend(Material material, Spine.BlendMode blendMode) {
				UnityEngine.Rendering.BlendMode source = UnityEngine.Rendering.BlendMode.One;
				UnityEngine.Rendering.BlendMode destination = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
				switch (blendMode) {
					case Spine.BlendMode.Additive:
						destination = UnityEngine.Rendering.BlendMode.One;
						break;
					case Spine.BlendMode.Multiply:
						source = UnityEngine.Rendering.BlendMode.DstColor;
						break;
					case Spine.BlendMode.Screen:
						destination = UnityEngine.Rendering.BlendMode.OneMinusSrcColor;
						break;
				}
				material.SetInt("_SrcBlend", (int)source);
				material.SetInt("_DstBlend", (int)destination);
				material.SetInt("_ZWrite", 0);
				material.SetOverrideTag("RenderType", "Transparent");
				material.renderQueue = 3000;
			}

			public void Dispose() {
				for (int i = 0; i < materials.Count; i++) {
					if (materials[i] != null) UnityEngine.Object.Destroy(materials[i]);
				}
				for (int i = 0; i < textures.Count; i++) {
					if (textures[i] != null) UnityEngine.Object.Destroy(textures[i]);
				}
				materials.Clear();
				textures.Clear();
				variants.Clear();
				FirstMaterial = null;
			}

			public void Unload(object texture) {
			}

			private sealed class MaterialVariant {
				public readonly Material Base;
				public readonly Spine.BlendMode Blend;
				public readonly Material Material;

				public MaterialVariant(Material baseMaterial, Spine.BlendMode blend, Material material) {
					Base = baseMaterial;
					Blend = blend;
					Material = material;
				}
			}
		}

		private sealed class FrameAnimationDef {
			public readonly string Skin;
			public readonly string Model;
			public readonly string Animation;
			public readonly string ManifestPath;

			public FrameAnimationDef(string skin, string model, string animation, string manifestPath) {
				Skin = skin;
				Model = model;
				Animation = animation;
				ManifestPath = manifestPath;
			}
		}

		private sealed class FrameSheetCache {
			public readonly Texture2D Texture;
			public readonly Material Material;

			public FrameSheetCache(Texture2D texture, Material material) {
				Texture = texture;
				Material = material;
			}
		}
	}
}
