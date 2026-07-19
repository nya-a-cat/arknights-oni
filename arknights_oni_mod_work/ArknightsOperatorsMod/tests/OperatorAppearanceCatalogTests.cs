using System;
using ArknightsOperatorsMod;

internal static class OperatorAppearanceCatalogTests {
	private static int assertions;

	private static void Require(bool condition, string message) {
		assertions++;
		if (!condition) throw new InvalidOperationException(message);
	}

	public static int Main(string[] args) {
		if (args.Length != 1) throw new ArgumentException("Expected the catalog path");
		OperatorAppearanceCatalog catalog = OperatorAppearanceCatalog.Load(args[0]);
		Require(catalog.Operators.Count == 420, "selectable operator count mismatch");
		int thumbnailCount = 0;
		int skinsWithoutMovementModel = 0;
		for (int i = 0; i < catalog.Operators.Count; i++) {
			if (!string.IsNullOrWhiteSpace(catalog.Operators[i].ThumbnailUrl)) thumbnailCount++;
			for (int skinIndex = 0; skinIndex < catalog.Operators[i].Skins.Count; skinIndex++) {
				if (catalog.Operators[i].Skins[skinIndex].FindModel("\u57fa\u5efa") == null)
					skinsWithoutMovementModel++;
			}
		}
		Require(thumbnailCount == 420, "selectable thumbnail URL snapshot mismatch");
		Require(skinsWithoutMovementModel == 0, "non-moving skin remained selectable");
		OperatorAppearanceDefinition raidian = catalog.FindExact("char_614_acsupo");
		Require(raidian == null, "combat-only Raidian should not be selectable");
		Require(catalog.FindById("char_510_amedic") == null,
			"combat-only Touch should not be selectable");
		OperatorAppearanceDefinition guardAmiya = catalog.FindById("char_1001_amiya2");
		Require(guardAmiya != null && guardAmiya.Skins.Count == 2,
			"Guard Amiya movement-compatible skins should remain selectable");
		Require(guardAmiya.FindSkin("\u9ed8\u8ba4") == null,
			"Guard Amiya combat-only default skin should be filtered");
		OperatorAppearanceDefinition amiya = catalog.FindExact("阿米娅");
		Require(amiya != null && amiya.Id == "char_002_amiya", "Chinese name lookup failed");
		Require(amiya.ThumbnailUrl.StartsWith("https://media.prts.wiki/", StringComparison.Ordinal),
			"Amiya thumbnail URL mismatch");
		Require(amiya.EnglishName == "Amiya", "English display name mismatch");
		Require(catalog.FindExact("Amiya") == null,
			"duplicate English name should require dropdown choice");
		Require(catalog.Search("Amiya", 60).Count >= 2, "English variant search failed");
		OperatorAppearanceDefinition exusiai = catalog.FindExact("能天使");
		Require(exusiai != null && exusiai.Id == "char_103_angel", "Exusiai lookup failed");
		Require(exusiai.EnglishName == "Exusiai", "Exusiai English name mismatch");
		Require(catalog.FindExact("Exusiai") == exusiai, "Exusiai English lookup failed");
		Require(catalog.FindExact("阿能") == exusiai, "PRTS alias lookup failed");
		Require(catalog.FindExact("char_103_angel") == exusiai, "char_id lookup failed");
		OperatorAppearanceDefinition dusk = catalog.FindExact("暮落");
		Require(dusk != null && dusk.Id == "char_4025_aprot2",
			"movement-compatible Chinese name should resolve after filtering");
		Require(catalog.Search("阿米娅", 60).Count >= 3, "operator search result mismatch");
		Require(catalog.Search("Texas", 60).Count >= 1, "English search result mismatch");
		OperatorAppearanceDefinition texas = catalog.FindExact("テキサス");
		Require(texas != null && texas.Id == "char_102_texas", "Japanese name lookup failed");
		Require(texas.JapaneseName == "テキサス", "Japanese display name mismatch");

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
