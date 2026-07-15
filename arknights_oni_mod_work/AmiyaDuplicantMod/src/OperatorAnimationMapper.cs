using System;
using System.Collections.Generic;

namespace AmiyaDuplicantMod {
	internal enum OperatorActionKind {
		Idle,
		Move,
		Work,
		Combat,
		Sleep,
		Sit,
		Stress,
		Death
	}

	internal sealed class OperatorAnimationPlan {
		public readonly OperatorActionKind Action;
		public readonly string Target;
		public readonly string Begin;
		public readonly string End;
		public readonly bool Loop;

		public OperatorAnimationPlan(OperatorActionKind action, string target, string begin, string end, bool loop) {
			Action = action;
			Target = target;
			Begin = begin;
			End = end;
			Loop = loop;
		}
	}

	internal static class OperatorAnimationMapper {
		private static readonly string[] IdleAnimations = { "Relax", "Idle", "Default", "Wait", "Start", "Move" };
		private static readonly string[] MoveAnimations = { "Move", "Walk", "Run", "Idle", "Default", "Start" };
		private static readonly string[] WorkAnimations = { "Interact", "Attack", "Skill", "Special", "Idle", "Default", "Start" };
		private static readonly string[] CombatAnimations = { "Attack", "Skill", "Interact", "Special", "Idle", "Default", "Start" };
		private static readonly string[] SleepAnimations = { "Sleep", "Sit", "Relax", "Idle", "Default" };
		private static readonly string[] SitAnimations = { "Sit", "Relax", "Idle", "Default" };
		private static readonly string[] StressAnimations = { "Stun", "Die", "Idle", "Default" };
		private static readonly string[] DeathAnimations = { "Die", "Stun", "Idle", "Default" };

		public static OperatorActionKind Classify(string oniAnimation) {
			string value = (oniAnimation ?? string.Empty).ToLowerInvariant();
			if (ContainsAny(value, "die", "dead", "death")) return OperatorActionKind.Death;
			if (ContainsAny(value, "stress", "panic", "sick", "stun", "vomit")) return OperatorActionKind.Stress;
			if (ContainsAny(value, "sleep", "bed")) return OperatorActionKind.Sleep;
			if (ContainsAny(value, "sit", "eat", "toilet")) return OperatorActionKind.Sit;
			if (ContainsAny(value, "attack", "combat", "fight")) return OperatorActionKind.Combat;
			if (ContainsAny(value, "work", "dig", "build", "harvest", "farm", "cook", "research", "operate", "repair",
				"craft", "clean", "tending", "mop", "sweep")) return OperatorActionKind.Work;
			if (ContainsAny(value, "move", "walk", "run", "climb", "ladder", "swim", "jump", "fall", "landing")) return OperatorActionKind.Move;
			return OperatorActionKind.Idle;
		}

		public static string Pick(string oniAnimation, IList<string> availableAnimations) {
			if (availableAnimations == null || availableAnimations.Count == 0) return null;
			string[] preferences = PreferencesFor(Classify(oniAnimation));

			for (int p = 0; p < preferences.Length; p++) {
				for (int i = 0; i < availableAnimations.Count; i++) {
					string candidate = availableAnimations[i];
					if (string.Equals(candidate, preferences[p], StringComparison.OrdinalIgnoreCase)) return candidate;
				}
				string expected = preferences[p].ToLowerInvariant();
				for (int i = 0; i < availableAnimations.Count; i++) {
					string candidate = availableAnimations[i];
					if (!string.IsNullOrEmpty(candidate) && !IsPhaseAnimation(candidate) && candidate.ToLowerInvariant().Contains(expected)) return candidate;
				}
			}

			return availableAnimations[0];
		}

		public static OperatorAnimationPlan BuildPlan(string oniAnimation, IList<string> availableAnimations) {
			OperatorActionKind action = Classify(oniAnimation);
			string target = Pick(oniAnimation, availableAnimations);
			if (string.IsNullOrEmpty(target)) return null;

			bool phased = action == OperatorActionKind.Work || action == OperatorActionKind.Combat;
			string begin = phased ? FindPhase(target, "Begin", availableAnimations) : null;
			string end = phased ? FindPhase(target, "End", availableAnimations) : null;
			bool loop = action != OperatorActionKind.Death &&
				!target.EndsWith("_Begin", StringComparison.OrdinalIgnoreCase) &&
				!target.EndsWith("_End", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(target, "Start", StringComparison.OrdinalIgnoreCase);
			return new OperatorAnimationPlan(action, target, begin, end, loop);
		}

		private static string FindPhase(string target, string phase, IList<string> availableAnimations) {
			string root = target;
			int loop = root.IndexOf("_Loop", StringComparison.OrdinalIgnoreCase);
			if (loop >= 0) root = root.Substring(0, loop) + root.Substring(loop + 5);
			if (root.EndsWith("_Idle", StringComparison.OrdinalIgnoreCase)) root = root.Substring(0, root.Length - 5);
			if (root.EndsWith("_Begin", StringComparison.OrdinalIgnoreCase)) root = root.Substring(0, root.Length - 6);
			if (root.EndsWith("_End", StringComparison.OrdinalIgnoreCase)) root = root.Substring(0, root.Length - 4);
			string expected = root + "_" + phase;
			for (int i = 0; i < availableAnimations.Count; i++) {
				if (string.Equals(availableAnimations[i], expected, StringComparison.OrdinalIgnoreCase)) return availableAnimations[i];
			}
			return null;
		}

		private static bool IsPhaseAnimation(string animation) {
			return animation.EndsWith("_Begin", StringComparison.OrdinalIgnoreCase) ||
				animation.EndsWith("_End", StringComparison.OrdinalIgnoreCase);
		}

		private static string[] PreferencesFor(OperatorActionKind kind) {
			switch (kind) {
				case OperatorActionKind.Move: return MoveAnimations;
				case OperatorActionKind.Work: return WorkAnimations;
				case OperatorActionKind.Combat: return CombatAnimations;
				case OperatorActionKind.Sleep: return SleepAnimations;
				case OperatorActionKind.Sit: return SitAnimations;
				case OperatorActionKind.Stress: return StressAnimations;
				case OperatorActionKind.Death: return DeathAnimations;
				default: return IdleAnimations;
			}
		}

		private static bool ContainsAny(string value, params string[] fragments) {
			for (int i = 0; i < fragments.Length; i++) {
				if (value.Contains(fragments[i])) return true;
			}
			return false;
		}
	}
}
