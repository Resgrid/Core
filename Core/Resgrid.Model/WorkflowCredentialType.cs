namespace Resgrid.Model
{
	public enum WorkflowCredentialType
	{
		Smtp = 0,
		Twilio = 1,
		Ftp = 2,
		Sftp = 3,
		AwsS3 = 4,
		HttpBearer = 5,
		HttpBasic = 6,
		HttpApiKey = 7,
		MicrosoftTeams = 8,
		Slack = 9,
		Discord = 10,
		AzureBlobStorage = 11,
		Box = 12,
		Dropbox = 13,

		/// <summary>
		/// OAuth2 client credentials grant (token URL, client id, client secret, scope, optional audience). The executor
		/// fetches a bearer token and caches it until 60 seconds before it expires. Needed for Dataverse and
		/// Entra-protected endpoints; in a protected step the token URL host is pinned on the release (TokenHost).
		/// </summary>
		OAuth2ClientCredentials = 14
	}
}

