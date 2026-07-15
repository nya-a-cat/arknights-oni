using UnityEngine;

namespace ArknightsOperatorsMod {
	public sealed class OperatorActionWheelController : MonoBehaviour {
		private const float WheelRadius = 170f;
		private const float ButtonWidth = 118f;
		private const float ButtonHeight = 48f;
		private bool wheelOpen;
		private OperatorDuplicantOverlay target;
		private string statusText;
		private float statusUntil;

		private static readonly OperatorActionKind[] Actions = {
			OperatorActionKind.Idle,
			OperatorActionKind.Move,
			OperatorActionKind.Work,
			OperatorActionKind.Combat,
			OperatorActionKind.Sleep,
			OperatorActionKind.Sit,
			OperatorActionKind.Stress,
			OperatorActionKind.Death
		};

		private void Update() {
			bool controlPressed = Input.GetKey(KeyCode.LeftControl) ||
				Input.GetKey(KeyCode.RightControl);
			if (controlPressed && Input.GetKeyDown(KeyCode.F9)) {
				if (wheelOpen) CloseWheel();
				else OpenForSelection();
			}
			if (wheelOpen && Input.GetKeyDown(KeyCode.Escape)) CloseWheel();
			if (wheelOpen && target == null) CloseWheel();
		}

		private void OpenForSelection() {
			KSelectable selected = SelectTool.Instance == null ? null : SelectTool.Instance.selected;
			OperatorDuplicantOverlay overlay = selected == null ? null :
				selected.GetComponent<OperatorDuplicantOverlay>();
			if (overlay == null && selected != null)
				overlay = selected.GetComponentInParent<OperatorDuplicantOverlay>();
			if (overlay == null) {
				ShowStatus(ModLocalization.Text(
					"请先选中一个已经应用干员外观的复制人。",
					"Select a duplicant with an operator appearance first."
				));
				return;
			}
			target = overlay;
			wheelOpen = true;
		}

		private void CloseWheel() {
			wheelOpen = false;
			target = null;
		}

		private void OnGUI() {
			if (!wheelOpen) {
				DrawStatus();
				return;
			}
			if (target == null) return;

			GUI.depth = -1000;
			Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
			Color previous = GUI.color;
			GUI.color = new Color(0.08f, 0.10f, 0.14f, 0.92f);
			GUI.Box(new Rect(center.x - 270f, center.y - 275f, 540f, 550f), "");
			GUI.color = previous;

			string model = ModelLabelFor(target.ActiveModel);
			GUI.Box(new Rect(center.x - 210f, center.y - 252f, 420f, 54f),
				ModLocalization.Text("干员动作转盘", "Operator action wheel") +
				"  ·  " + model + "\nCtrl+F9 / Esc");

			for (int i = 0; i < Actions.Length; i++) {
				float angle = (-90f + i * 45f) * Mathf.Deg2Rad;
				float x = center.x + Mathf.Cos(angle) * WheelRadius - ButtonWidth * 0.5f;
				float y = center.y + Mathf.Sin(angle) * WheelRadius - ButtonHeight * 0.5f;
				OperatorActionKind action = Actions[i];
				if (GUI.Button(new Rect(x, y, ButtonWidth, ButtonHeight), LabelFor(action))) {
					target.SetManualAction(action);
					ShowStatus(ModLocalization.Text("已切换为手动表演：", "Manual performance: ") +
						LabelFor(action));
					CloseWheel();
					return;
				}
			}

			string autoLabel = target.ManualAction.HasValue ?
				ModLocalization.Text("恢复自动", "Resume auto") :
				ModLocalization.Text("自动中", "Automatic");
			if (GUI.Button(new Rect(center.x - 68f, center.y - 30f, 136f, 60f), autoLabel)) {
				target.SetManualAction(null);
				ShowStatus(ModLocalization.Text("已恢复 ONI 自动动作。", "ONI automatic animation restored."));
				CloseWheel();
			}
		}

		private void ShowStatus(string text) {
			statusText = text;
			statusUntil = Time.unscaledTime + 3f;
		}

		private void DrawStatus() {
			if (string.IsNullOrEmpty(statusText) || Time.unscaledTime > statusUntil) return;
			GUI.depth = -1000;
			GUI.Box(new Rect(Screen.width * 0.5f - 250f, 34f, 500f, 42f), statusText);
		}

		private static string LabelFor(OperatorActionKind action) {
			switch (action) {
				case OperatorActionKind.Move:
					return ModLocalization.Text("移动", "Move");
				case OperatorActionKind.Work:
					return ModLocalization.Text("挖矿 / 工作", "Dig / Work");
				case OperatorActionKind.Combat:
					return ModLocalization.Text("攻击 / 技能", "Attack / Skill");
				case OperatorActionKind.Sleep:
					return ModLocalization.Text("睡觉", "Sleep");
				case OperatorActionKind.Sit:
					return ModLocalization.Text("坐下", "Sit");
				case OperatorActionKind.Stress:
					return ModLocalization.Text("眩晕", "Stun");
				case OperatorActionKind.Death:
					return ModLocalization.Text("死亡", "Death");
				default:
					return ModLocalization.Text("待机", "Idle");
			}
		}

		private static string ModelLabelFor(string model) {
			if (string.IsNullOrEmpty(model)) return "-";
			switch (model) {
				case "基建": return ModLocalization.Text("基建", "Base");
				case "正面": return ModLocalization.Text("正面", "Front");
				case "背面": return ModLocalization.Text("背面", "Back");
				case "战斗": return ModLocalization.Text("战斗", "Combat");
				default: return model;
			}
		}
	}
}
