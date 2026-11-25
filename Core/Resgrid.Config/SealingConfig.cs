namespace Resgrid.Config
{
	/// <summary>
	/// L3 sealing (e-Signature S1, plan §22.2/§22.4): the resgrid-sealing EU DSS REST sidecar, the RFC 3161 timestamp authority,
	/// the signing certificate source and the sealed-document bucket. Everything is blank by default, so sealing fails closed:
	/// documents are recorded "Signed, sealing pending" and worker 90 retries until an operator configures all four. The CA,
	/// HSM/remote-signing and TSA choices are open (§25.5). Environment keys follow RESGRID__SealingConfig__{Field}.
	/// </summary>
	public static class SealingConfig
	{
		// ---- The resgrid-sealing sidecar (EU DSS webapp REST) -------------------------------------------------------

		/// <summary>DSS REST base, e.g. http://resgrid-sealing:8080/services/rest. Blank = sealing not configured.</summary>
		public static string SidecarBaseUrl = "";

		/// <summary>Optional shared key the sidecar's reverse proxy expects (header X-Resgrid-Sealing-Key). Blank sends none.</summary>
		public static string SidecarApiKey = "";

		public static int SidecarTimeoutSeconds = 60;

		/// <summary>
		/// The RFC 3161 TSA the sidecar is configured with (DSS takes its TSP source from its own configuration). Blank = no TSA,
		/// so nothing is sealed: Resgrid never seals without a timestamp. Every sealed result is also validated and refused unless
		/// DSS reports a timestamp.
		/// </summary>
		public static string TsaUrl = "";

		// ---- Signing certificate -------------------------------------------------------------------------------------

		/// <summary>"" (none: fail closed), "pkcs12" (a .p12/.pfx file, development and self-hosting) or "remote" (an HSM or CA
		/// remote hash signer registered as IRemoteHashSigner).</summary>
		public static string CertificateSource = "";

		/// <summary>pkcs12: path to the .p12/.pfx file (key + certificate + chain).</summary>
		public static string Pkcs12Path = "";

		/// <summary>pkcs12: the file as base64, instead of a path (a Kubernetes Secret value).</summary>
		public static string Pkcs12Base64 = "";

		public static string Pkcs12Password = "";

		// ---- Sealed-document store (RustFS / any S3-compatible endpoint; its own bucket) --------------------------------

		/// <summary>S3-compatible endpoint. Blank = no store: sealing stays pending (a sealed file must be kept to be re-served).</summary>
		public static string S3Endpoint = "";

		public static string S3AccessKey = "";

		public static string S3SecretKey = "";

		/// <summary>Sealed-document bucket; separate from imports, search and map packs so retention policies stay independent.</summary>
		public static string S3Bucket = "resgrid-sealed";

		public static string S3Region = "us-east-1";

		public static bool S3UseSsl = true;

		public static bool S3ForcePathStyle = true;

		/// <summary>Objects live under {S3Prefix}/{departmentId}/{subjectKind}/{sealedDocumentId}-{sha256 prefix}.pdf.</summary>
		public static string S3Prefix = "sealed";
	}

	/// <summary>
	/// L4 external signers (e-Signature S2, plan §22.3): Documenso Community Edition, headless, one per cluster (decision 16), and
	/// the webhook hint allowlist. Documenso is optional (decision 16b): a blank base URL turns external-link signing off while
	/// in-person signing keeps working. Environment keys follow RESGRID__ESignConfig__{Field}.
	/// </summary>
	public static class ESignConfig
	{
		/// <summary>Documenso API base reachable from the cluster, e.g. http://documenso:3000. Blank = remote signing unavailable.</summary>
		public static string DocumensoBaseUrl = "";

		/// <summary>The public origin the signing page embeds (https://sign.resgrid.com). Blank = same as DocumensoBaseUrl.</summary>
		public static string DocumensoPublicUrl = "";

		public static int DocumensoTimeoutSeconds = 60;

		/// <summary>Recipients without an e-mail address get a placeholder at this domain (Documenso needs one; it sends no mail).</summary>
		public static string PlaceholderEmailDomain = "signers.resgrid.invalid";

		/// <summary>
		/// Networks the webhook hints may come from, comma separated CIDRs (the cluster network). Blank = every hint is refused:
		/// Documenso's webhook secret is a plain shared value, not an HMAC, so the network is the boundary (plan §22.3).
		/// </summary>
		public static string WebhookAllowedNetworks = "";

		/// <summary>The shared secret Documenso sends in X-Documenso-Secret (checked as well as the network). Blank = not checked.</summary>
		public static string WebhookSecret = "";

		/// <summary>Signing links expire after this many days unless the request says less.</summary>
		public static int LinkExpiryDays = 14;

		/// <summary>A signing session (after the link is exchanged) lasts this long.</summary>
		public static int SessionMinutes = 60;

		/// <summary>One-time codes sent per signer per hour.</summary>
		public static int MaxCodesPerHour = 5;

		/// <summary>Wrong codes before the code is wiped.</summary>
		public static int MaxCodeAttempts = 5;

		/// <summary>Signing page requests per address per minute.</summary>
		public static int SigningPageRateLimitPerMinute = 30;

		/// <summary>Worker 90 re-fetches an open remote envelope at most this often without a webhook hint.</summary>
		public static int ReconcileIntervalMinutes = 30;
	}
}
