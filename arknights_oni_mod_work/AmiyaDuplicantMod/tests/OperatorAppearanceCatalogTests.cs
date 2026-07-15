using System;
using AmiyaDuplicantMod;

internal static class OperatorAppearanceCatalogTests {
	private static int assertions;

	private static void Require(bool condition, string message) {
		assertions++;
		if (!condition) throw new InvalidOperationException(message);
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected the catalog path");
		OperatorAppearanceCatalog catalog = OperatorAppearanceCatalog.Load(args[0]);
		Require(catalog.Operators.Count == 449, "operator count mismatch");

		OperatorAppearanceDefinition amiya = catalog.FindExact("阿米娅");
		Require(amiya != null && amiya.Id == "char_002_amiya", "Chinese name lookup failed");
		OperatorAppearanceDefinition exusiai = catalog.FindExact("能天使");
		Require(exusiai != null && exusiai.Id == "char_103_angel", "Exusiai lookup failed");
		Require(catalog.FindExact("char_103_angel") == exusiai, "char_id lookup failed");
		Require(catalog.FindExact("暮落") == null, "duplicate Chinese name should require dropdown choice");
		Require(catalog.Search("阿米娅", 60).Count >= 3, "operator search result mismatch");

		OperatorAppearanceSelection selected = catalog.Normalize(
			"char_002_amiya", "播种者", "正面"
		);
		Require(selected.Character.Id == "char_002_amiya", "selected character mismatch");
		Require(selected.Skin.Name == "播种者", "selected skin mismatch");
		Require(selected.Model == "正面", "selected model mismatch");

		OperatorAppearanceSelection fallback = catalog.Normalize(
			"missing", "missing", "missing"
		);
		Require(fallback.Character.Id == "char_002_amiya", "character fallback mismatch");
		Require(fallback.Skin.Name == "默认", "skin fallback mismatch");
		Require(fallback.Model == "基建", "model fallback mismatch");

		Console.WriteLine("OperatorAppearanceCatalogTests: " + assertions + " passed; operators=" +
			catalog.Operators.Count + " amiyaSkins=" + amiya.Skins.Count);
		return 0;
	}
}
