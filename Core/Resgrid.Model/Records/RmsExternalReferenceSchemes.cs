namespace Resgrid.Model
{
	/// <summary>
	/// Identifier schemes for <see cref="RmsExternalReference.IdentifierScheme"/>. The column is a free string by
	/// design — RMS never validates another system's identifier format — so this is the product's spelling, kept in
	/// one place so a Deployment, a pack template and a report all name the same source the same way.
	/// The incident-business schemes are the Back Office plan's E3 list; the first four already shipped with
	/// "Create Deployment from External Order" in RMS-1C.
	/// </summary>
	public static class RmsExternalReferenceSchemes
	{
		/// <summary>U.S. wildland Interagency Resource Ordering Capability order/request/fill identifiers.</summary>
		public const string Iroc = "iroc";
		/// <summary>Canadian Interagency Forest Fire Centre resource-exchange identifiers.</summary>
		public const string Ciffc = "ciffc";
		/// <summary>A member or home agency's own order identifier.</summary>
		public const string Agency = "agency";
		/// <summary>Generic all-hazard mutual-aid order identifier.</summary>
		public const string Generic = "generic";

		/// <summary>e-ISuite incident-business data exchange identifiers.</summary>
		public const string EIsuite = "e-isuite";
		/// <summary>EMAC mission order / REQ-A identifiers.</summary>
		public const string Emac = "emac";
		/// <summary>WebEOC resource-request and mission identifiers.</summary>
		public const string WebEoc = "webeoc";
		/// <summary>Logistics Supply Chain Management System supply-request identifiers.</summary>
		public const string Lscms = "lscms";
		/// <summary>NFES cache / ICLIP supply-request identifiers.</summary>
		public const string NfesIclip = "nfes-iclip";
		/// <summary>Lodging confirmation number from a property or booking service.</summary>
		public const string LodgingConfirmation = "lodging-confirmation";
		/// <summary>Vendor invoice number as printed by the vendor.</summary>
		public const string VendorInvoice = "vendor-invoice";
		/// <summary>Finance/accounting posting reference in the department's system of record.</summary>
		public const string FinancePosting = "finance-posting";

		public static readonly string[] All =
		{
			Iroc, Ciffc, Agency, Generic, EIsuite, Emac, WebEoc, Lscms, NfesIclip, LodgingConfirmation, VendorInvoice, FinancePosting
		};
	}
}
