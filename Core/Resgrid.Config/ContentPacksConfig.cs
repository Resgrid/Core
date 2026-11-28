namespace Resgrid.Config
{
	/// <summary>
	/// Jurisdiction content packs (industrial ERT gap plan G8 CA2, decision ERT-D10). Resgrid-authored, paraphrased content for a
	/// country or province ships in code but stays out of the template catalogs until a qualified reviewer for that jurisdiction
	/// signs it off (the entry is then moved to the published list in code). An operator or QA reviewer can show the unreviewed
	/// entries, labelled "Not yet reviewed", to review them in place. Environment key RESGRID:ContentPacksConfig:ShowUnreviewedPacks.
	/// </summary>
	public static class ContentPacksConfig
	{
		/// <summary>Off by default: unreviewed jurisdiction content is never offered to departments.</summary>
		public static bool ShowUnreviewedPacks = false;
	}
}
