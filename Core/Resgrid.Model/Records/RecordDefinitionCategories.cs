namespace Resgrid.Model
{
	/// <summary>
	/// The category labels the product template packs ship (RMS plan section 4.1). A department may type any
	/// category it likes on its own definition — this is the product's spelling, not a constraint — but every
	/// shipped template uses one of these so the template browser groups them the same way each release.
	/// </summary>
	public static class RecordDefinitionCategories
	{
		public const string Operations = "Operations";
		public const string Security = "Security";
		public const string Delivery = "Delivery";
		public const string Transit = "Transit";
		public const string FieldService = "Field service";
		public const string Cert = "CERT";
		public const string Sar = "SAR";
		public const string Disaster = "Disaster";
		public const string Eoc = "EOC";
		public const string Hazmat = "HAZMAT";
		public const string Industrial = "Industrial";
		public const string Exercise = "Exercise";
		public const string MutualAid = "Mutual aid";

		/// <summary>
		/// Incident business (Back Office plan E6): the finance and administration half of a large incident —
		/// time, equipment use, expenses, agreements, delegations and cost share. These Records are documents with
		/// a lifecycle and a signature; the arithmetic behind them stays in the owning ledger and is referenced,
		/// never re-keyed into record values.
		/// </summary>
		public const string IncidentBusiness = "Incident business";

		/// <summary>Incident support: the ICS planning, resource, communications and safety products of an incident.</summary>
		public const string IncidentSupport = "Incident support";
	}
}
