using System;
using System.Collections.Generic;
using AmiyaDuplicantMod;

internal static class OperatorAnimationMapperTests {
	private static int failures;
	private static int assertions;

	private static void Expect(string label, string expected, string actual) {
		assertions++;
		if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
		failures++;
		Console.Error.WriteLine(label + ": expected=" + expected + " actual=" + (actual ?? "<null>"));
	}

	private static void Expect(string label, bool expected, bool actual) {
		assertions++;
		if (expected == actual) return;
		failures++;
		Console.Error.WriteLine(label + ": expected=" + expected + " actual=" + actual);
	}

	public static int Main() {
		List<string> build = new List<string> { "Default", "Interact", "Move", "Relax", "Sit", "Sleep" };
		Expect("build idle", "Relax", OperatorAnimationMapper.Pick("idle_loop", build));
		Expect("build move", "Move", OperatorAnimationMapper.Pick("move_loop", build));
		Expect("build work", "Interact", OperatorAnimationMapper.Pick("working_loop", build));
		Expect("build sleep", "Sleep", OperatorAnimationMapper.Pick("sleep_loop", build));
		Expect("build sit", "Sit", OperatorAnimationMapper.Pick("sit_loop", build));
		Expect("craft classified as work", "Work", OperatorAnimationMapper.Classify("CRAFT_LOOP").ToString());
		Expect("clean classified as work", "Work", OperatorAnimationMapper.Classify("clean_pre").ToString());
		Expect("crop tending classified as work", "Work", OperatorAnimationMapper.Classify("crop_tending_pst").ToString());
		Expect("falling classified as move", "Move", OperatorAnimationMapper.Classify("falling_pre").ToString());
		Expect("landing classified as move", "Move", OperatorAnimationMapper.Classify("landing_loop").ToString());

		List<string> combat = new List<string> {
			"Attack_Begin", "Attack", "Die", "Idle", "Skill_Loop_2", "Start", "Stun"
		};
		Expect("combat attack", "Attack", OperatorAnimationMapper.Pick("attack_loop", combat));
		Expect("combat work", "Attack", OperatorAnimationMapper.Pick("dig_loop", combat));
		Expect("combat stress", "Stun", OperatorAnimationMapper.Pick("panic_loop", combat));
		Expect("combat death", "Die", OperatorAnimationMapper.Pick("death", combat));
		Expect("combat idle", "Idle", OperatorAnimationMapper.Pick("idle_loop", combat));
		OperatorAnimationPlan combatPlan = OperatorAnimationMapper.BuildPlan("attack_loop", combat);
		Expect("combat begin", "Attack_Begin", combatPlan.Begin);
		Expect("combat target loops", true, combatPlan.Loop);
		OperatorAnimationPlan deathPlan = OperatorAnimationMapper.BuildPlan("death", combat);
		Expect("death does not loop", false, deathPlan.Loop);

		List<string> skillTwo = new List<string> { "Skill_2_Begin", "Skill_Loop_2", "Skill_2_End", "Idle" };
		OperatorAnimationPlan skillTwoPlan = OperatorAnimationMapper.BuildPlan("attack_loop", skillTwo);
		Expect("skill 2 target", "Skill_Loop_2", skillTwoPlan.Target);
		Expect("skill 2 begin", "Skill_2_Begin", skillTwoPlan.Begin);
		Expect("skill 2 end", "Skill_2_End", skillTwoPlan.End);

		List<string> actualSkillTwo = new List<string> { "Skill_2_Begin", "Skill_2_Idle", "Skill_2_End", "Idle" };
		OperatorAnimationPlan actualSkillTwoPlan = OperatorAnimationMapper.BuildPlan("combat_loop", actualSkillTwo);
		Expect("actual skill 2 target", "Skill_2_Idle", actualSkillTwoPlan.Target);
		Expect("actual skill 2 begin", "Skill_2_Begin", actualSkillTwoPlan.Begin);
		Expect("actual skill 2 end", "Skill_2_End", actualSkillTwoPlan.End);

		Expect("empty list", null, OperatorAnimationMapper.Pick("idle", new List<string>()));
		Expect("unknown ONI action", "Default", OperatorAnimationMapper.Pick("unknown", new List<string> { "Special", "Default" }));

		if (failures == 0) Console.WriteLine("OperatorAnimationMapperTests: " + assertions + " passed");
		return failures == 0 ? 0 : 1;
	}
}
