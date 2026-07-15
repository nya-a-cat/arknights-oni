using System;
using PeterHan.PLib.Options;
using UnityEngine;

namespace AmiyaDuplicantMod {
	public sealed class AppearanceOptionsHotkey : MonoBehaviour {
		private bool dialogOpen;

		private void Update() {
			bool controlPressed = Input.GetKey(KeyCode.LeftControl) ||
				Input.GetKey(KeyCode.RightControl);
			if (dialogOpen || !controlPressed || !Input.GetKeyDown(KeyCode.F8)) return;
			try {
				dialogOpen = true;
				POptions.ShowDialog(typeof(ModConfig), OnDialogClosed);
			} catch (Exception error) {
				dialogOpen = false;
				Debug.LogError("[AmiyaDuplicantMod] Failed to open appearance options: " + error);
			}
		}

		private void OnDialogClosed(object settings) {
			dialogOpen = false;
		}
	}
}
