using System;
using System.Collections.Generic;
using ArknightsOperatorsMod;

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
		Expect("hex animation hash stays idle", "Idle", OperatorAnimationMapper.Classify("0xEDEAD903").ToString());
		Expect("moving state overrides idle anim", "Move", OperatorAnimationMapper.ResolveSourceAnimation("idle_loop", true));
		Expect("moving state works without source anim", "Move", OperatorAnimationMapper.ResolveSourceAnimation(null, true));
		Expect("stationary state keeps source anim", "idle_loop", OperatorAnimationMapper.ResolveSourceAnimation("idle_loop", false));
		Expect("stationary null stays null", null, OperatorAnimationMapper.ResolveSourceAnimation(null, false));
		Expect("manual sleep overrides idle", "sleep", OperatorAnimationMapper.ResolveEffectiveAnimation(
			"idle_loop", OperatorActionKind.Sleep));
		Expect("real death overrides manual sleep", "death", OperatorAnimationMapper.ResolveEffectiveAnimation(
			"death", OperatorActionKind.Sleep));
		Expect("real stress overrides manual idle", "panic_loop", OperatorAnimationMapper.ResolveEffectiveAnimation(
			"panic_loop", OperatorActionKind.Idle));
		Expect("automatic sleep uses base model", "基建", OperatorAnimationMapper.PreferredModel(
			"sleep_loop", true, "正面"));
		Expect("automatic digging uses front model", "正面", OperatorAnimationMapper.PreferredModel(
			"dig_loop", true, "基建"));
		Expect("automatic combat uses front model", "正面", OperatorAnimationMapper.PreferredModel(
			"combat", true, "基建"));
		Expect("automatic death uses front model", "正面", OperatorAnimationMapper.PreferredModel(
			"death", true, "基建"));
		Expect("ordinary work stays on base model", "基建", OperatorAnimationMapper.PreferredModel(
			"cook_loop", true, "正面"));
		Expect("disabled switching preserves configured model", "背面", OperatorAnimationMapper.PreferredModel(
			"death", false, "背面"));

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
