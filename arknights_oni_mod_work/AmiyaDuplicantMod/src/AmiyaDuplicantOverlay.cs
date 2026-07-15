using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Spine;
using UnityEngine;

namespace AmiyaDuplicantMod {
	public sealed class AmiyaDuplicantOverlay : MonoBehaviour {
		private const float TargetVisualHeight = 1.65f;
		private const float GroundOffsetY = 0f;
		private const float MinimumWorldScale = 0.0015f;
		private const float MaximumWorldScale = 0.0080f;
		private const float FrameZStep = -0.0001f;
		private const string PreferredFrameSkin = "播种者";
		private const string PreferredFrameModel = "正面";

		private KBatchedAnimController sourceAnim;
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
		private CancellationTokenSource loadCancellation;
		private IDisposable resourceLease;
		private int loadGeneration;

		private void Start() {
			try {
				sourceAnim = GetComponent<KBatchedAnimController>();
				CreateVisualRoot();
				ModConfigStore.AppearanceChanged += OnAppearanceChanged;
				BeginAppearanceLoad(ModConfigStore.Current, true);
			} catch (Exception ex) {
				Debug.LogError("[AmiyaDuplicantMod] Failed to attach overlay: " + ex);
				enabled = false;
			}
		}

		private void OnDestroy() {
			ModConfigStore.AppearanceChanged -= OnAppearanceChanged;
			loadGeneration++;
			if (loadCancellation != null) {
				loadCancellation.Cancel();
				loadCancellation.Dispose();
				loadCancellation = null;
			}
			if (resourceLease != null) {
				resourceLease.Dispose();
				resourceLease = null;
			}
			if (sourceAnim != null && sourceHidden) sourceAnim.SetVisiblity(true);
			if (visualRoot != null) Destroy(visualRoot);
			if (textureLoader != null) textureLoader.Dispose();
			foreach (FrameSheetCache cached in frameSheetCache.Values) {
				if (cached.Material != null) Destroy(cached.Material);
				if (cached.Texture != null) Destroy(cached.Texture);
			}
			frameSheetCache.Clear();
		}

		private void LateUpdate() {
			if (frameFallbackMode) {
				UpdateFrameFallback();
				return;
			}
			if (skeleton == null || state == null) return;

			SyncFacingAndAnimation();
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
			BeginAppearanceLoad(config, !sourceHidden);
		}

		private void BeginAppearanceLoad(ModConfig config, bool allowFallback) {
			loadGeneration++;
			if (loadCancellation != null) {
				loadCancellation.Cancel();
				loadCancellation.Dispose();
			}
			loadCancellation = new CancellationTokenSource();
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
				while (!task.IsCompleted) {
					if (cancellationToken.IsCancellationRequested || generation != loadGeneration)
						yield break;
					yield return null;
				}
				if (task.IsCanceled || cancellationToken.IsCancellationRequested ||
					generation != loadGeneration) yield break;
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
					nextLease = service.Acquire(bundle.ResourceKeys);
					prepared = PrepareSkeleton(bundle.AtlasPath, bundle.SkeletonPath);
					if (!cancellationToken.IsCancellationRequested && generation == loadGeneration) {
						ApplySkeleton(prepared);
						prepared = null;
						IDisposable previousLease = resourceLease;
						resourceLease = nextLease;
						nextLease = null;
						if (previousLease != null) previousLease.Dispose();
						PlayBestAnimation(CurrentOniAnimation());
						HideSourceVisual();
						remoteLoaded = true;
						Debug.Log("[AmiyaDuplicantMod] Loaded operator " + bundle.CharacterId + " " +
							bundle.Skin + "/" + bundle.Model + " version=" + bundle.ResourceVersion);
						Debug.Log("[AmiyaDuplicantMod] Operator overlay attached to " + gameObject.name);
					}
				} catch (Exception error) {
					remoteError = error;
				} finally {
					if (prepared != null) prepared.Dispose();
					if (nextLease != null) nextLease.Dispose();
				}
			}
			if (remoteLoaded) yield break;

			if (cancellationToken.IsCancellationRequested || generation != loadGeneration)
				yield break;
			if (!allowFallback) {
				Debug.LogWarning("[AmiyaDuplicantMod] Appearance switch failed; keeping current operator: " +
					remoteError);
				yield break;
			}

			bool loaded = false;
			if (!loaded) {
				Debug.LogWarning("[AmiyaDuplicantMod] PRTS asset load failed; trying bundled fallback: " + remoteError);
				try {
					LoadSkeleton(ModAssets.AmiyaAtlasPath, ModAssets.AmiyaSkeletonPath);
					PlayBestAnimation(CurrentOniAnimation());
					loaded = true;
				} catch (Exception spineError) {
					Debug.LogWarning("[AmiyaDuplicantMod] Bundled skeleton failed; trying frame fallback: " + spineError);
					try {
						LoadFrameFallback();
						loaded = true;
					} catch (Exception frameError) {
						Debug.LogError("[AmiyaDuplicantMod] All operator visuals failed; keeping vanilla duplicant: " + frameError);
					}
				}
			}

			if (loaded) {
				HideSourceVisual();
				Debug.Log("[AmiyaDuplicantMod] Operator overlay attached to " + gameObject.name);
			} else {
				enabled = false;
			}
		}

		private string CurrentOniAnimation() {
			return sourceAnim == null ? null : sourceAnim.currentAnim.ToString();
		}

		private void LoadSkeleton(string atlasPath, string skeletonPath) {
			ApplySkeleton(PrepareSkeleton(atlasPath, skeletonPath));
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

		private void ApplySkeleton(LoadedSpineVisual loaded) {
			UnityTextureLoader previousTextureLoader = textureLoader;
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
			if (previousTextureLoader != null) previousTextureLoader.Dispose();

			LogAvailableAnimations();
		}

		private void LoadFrameFallback() {
			LoadFrameLibrary();
			FrameAnimationDef initial = PickFrameAnimation(null);
			LoadFrameFallback(initial != null ? initial.ManifestPath : ModAssets.AmiyaFrameManifestPath);
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
			visualRoot.transform.localScale = Vector3.one;
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
			Debug.Log("[AmiyaDuplicantMod] Loaded frame fallback: " + sheetPath + " animation=" + currentFrameAnimation + " frames=" + frameCount + " fps=" + frameFps);
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
				Debug.Log("[AmiyaDuplicantMod] Frame library index not found; using root fallback only.");
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

			Debug.Log("[AmiyaDuplicantMod] Loaded frame library entries: " + frameLibrary.Count);
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
			SyncFrameFallbackFacingAndAnimation();
			frameElapsed += Time.deltaTime;
			int elapsedFrame = Mathf.FloorToInt(frameElapsed * frameFps);
			int frameIndex = frameLoop ? elapsedFrame % frameCount : Mathf.Min(elapsedFrame, frameCount - 1);
			ApplyFrameFallbackUv(frameIndex);
		}

		private void SyncFrameFallbackFacingAndAnimation() {
			if (sourceAnim == null) return;
			Vector3 scale = visualRoot.transform.localScale;
			scale.x = sourceAnim.FlipX ? -1f : 1f;
			visualRoot.transform.localScale = scale;

			FrameAnimationDef next = PickFrameAnimation(sourceAnim.currentAnim.ToString());
			if (next == null || next.Animation == currentFrameAnimation) return;
			try {
				LoadFrameFallback(next.ManifestPath);
			} catch (Exception ex) {
				Debug.LogWarning("[AmiyaDuplicantMod] Failed to switch frame animation to " + next.Animation + ": " + ex);
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
			if (sourceAnim == null) return;
			sourceAnim.SetVisiblity(false);
			sourceHidden = true;
		}

		private void SyncFacingAndAnimation() {
			if (sourceAnim == null) return;
			bool flipX = sourceAnim.FlipX;
			skeleton.ScaleX = flipX ? -1f : 1f;
			if (scaleInitialized) {
				Vector3 position = visualRoot.transform.localPosition;
				position.x = (flipX ? calibratedCenterX : -calibratedCenterX) * calibratedScale;
				visualRoot.transform.localPosition = position;
			}
			string oniAnim = sourceAnim.currentAnim.ToString();
			PlayBestAnimation(oniAnim);
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
			float scale = Mathf.Clamp(TargetVisualHeight / (rawMaxY - rawMinY), MinimumWorldScale, MaximumWorldScale);
			float centerX = (rawMinX + rawMaxX) * 0.5f;
			calibratedScale = scale;
			calibratedCenterX = centerX;
			visualRoot.transform.localScale = new Vector3(scale, scale, scale);
			visualRoot.transform.localPosition = new Vector3(-centerX * scale,
				GroundOffsetY - rawMinY * scale, -0.35f);
			scaleInitialized = true;
			Debug.Log("[AmiyaDuplicantMod] Auto scale=" + scale + " rawBounds=" +
				(rawMaxX - rawMinX) + "x" + (rawMaxY - rawMinY));
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
			Debug.Log("[AmiyaDuplicantMod] Scale reference=" + (reference ?? "setup"));
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
			Debug.Log("[AmiyaDuplicantMod] Loaded Spine " + skeletonData.Version + " animations: " + string.Join(", ", names.ToArray()));
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
