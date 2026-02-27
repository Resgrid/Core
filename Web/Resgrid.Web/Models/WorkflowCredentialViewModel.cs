using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Models
{
	/// <summary>
	/// View model for creating/editing a WorkflowCredential with per-type fields
	/// instead of a raw JSON textarea. The controller builds the JSON before storage.
	/// </summary>
	public sealed class WorkflowCredentialViewModel : IValidatableObject
	{
		// ── Core fields ────────────────────────────────────────────────────────

		public string WorkflowCredentialId { get; set; }

		[Required]
		[MaxLength(250)]
		public string Name { get; set; }

		[Required]
		public WorkflowCredentialType CredentialType { get; set; }

		// ── SMTP ──────────────────────────────────────────────────────────────
		public string SmtpHost { get; set; }
		public int? SmtpPort { get; set; }
		public string SmtpUsername { get; set; }
		public string SmtpPassword { get; set; }
		public bool SmtpUseSsl { get; set; } = true;
		public string SmtpFromAddress { get; set; }

		// ── Twilio ────────────────────────────────────────────────────────────
		public string TwilioAccountSid { get; set; }
		public string TwilioAuthToken { get; set; }
		public string TwilioFromNumber { get; set; }

		// ── FTP ───────────────────────────────────────────────────────────────
		public string FtpHost { get; set; }
		public int? FtpPort { get; set; }
		public string FtpUsername { get; set; }
		public string FtpPassword { get; set; }
		public bool FtpPassive { get; set; } = true;

		// ── SFTP ──────────────────────────────────────────────────────────────
		public string SftpHost { get; set; }
		public int? SftpPort { get; set; }
		public string SftpUsername { get; set; }
		public string SftpPassword { get; set; }
		public string SftpPrivateKey { get; set; }

		// ── AWS S3 ────────────────────────────────────────────────────────────
		public string AwsAccessKeyId { get; set; }
		public string AwsSecretAccessKey { get; set; }
		public string AwsRegion { get; set; }
		public string AwsBucketName { get; set; }

		// ── HTTP Bearer ───────────────────────────────────────────────────────
		public string HttpBearerToken { get; set; }

		// ── HTTP Basic ────────────────────────────────────────────────────────
		public string HttpBasicUsername { get; set; }
		public string HttpBasicPassword { get; set; }

		// ── HTTP API Key ──────────────────────────────────────────────────────
		public string ApiKeyHeaderName { get; set; }
		public string ApiKeyValue { get; set; }

		// ── Microsoft Teams ───────────────────────────────────────────────────
		public string TeamsWebhookUrl { get; set; }

		// ── Slack ─────────────────────────────────────────────────────────────
		public string SlackWebhookUrl { get; set; }
		public string SlackBotToken { get; set; }

		// ── Discord ───────────────────────────────────────────────────────────
		public string DiscordWebhookUrl { get; set; }
		public string DiscordBotToken { get; set; }

		// ── Azure Blob Storage ────────────────────────────────────────────────
		public string AzureConnectionString { get; set; }
		public string AzureContainerName { get; set; }

		// ── Box ───────────────────────────────────────────────────────────────
		public string BoxClientId { get; set; }
		public string BoxClientSecret { get; set; }
		public string BoxEnterpriseId { get; set; }
		public string BoxPrivateKey { get; set; }
		public string BoxPrivateKeyPassword { get; set; }
		public string BoxPublicKeyId { get; set; }

		// ── Dropbox ───────────────────────────────────────────────────────────
		public string DropboxRefreshToken { get; set; }
		public string DropboxAppKey { get; set; }
		public string DropboxAppSecret { get; set; }

		// ── Per-type validation ────────────────────────────────────────────────
		/// <summary>
		/// Validates that all required fields for the selected <see cref="CredentialType"/>
		/// are populated. Applied identically on both create and edit — editing always
		/// requires a full set of credentials to be supplied.
		/// </summary>
		public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
		{
			switch (CredentialType)
			{
				case WorkflowCredentialType.Smtp:
					if (string.IsNullOrWhiteSpace(SmtpHost))
						yield return new ValidationResult("SMTP host is required.", [nameof(SmtpHost)]);
					if (string.IsNullOrWhiteSpace(SmtpPassword))
						yield return new ValidationResult("SMTP password is required.", [nameof(SmtpPassword)]);
					if (string.IsNullOrWhiteSpace(SmtpFromAddress))
						yield return new ValidationResult("SMTP from address is required.", [nameof(SmtpFromAddress)]);
					break;

				case WorkflowCredentialType.Twilio:
					if (string.IsNullOrWhiteSpace(TwilioAccountSid))
						yield return new ValidationResult("Twilio Account SID is required.", [nameof(TwilioAccountSid)]);
					if (string.IsNullOrWhiteSpace(TwilioAuthToken))
						yield return new ValidationResult("Twilio Auth Token is required.", [nameof(TwilioAuthToken)]);
					break;

				case WorkflowCredentialType.Ftp:
					if (string.IsNullOrWhiteSpace(FtpHost))
						yield return new ValidationResult("FTP host is required.", [nameof(FtpHost)]);
					if (string.IsNullOrWhiteSpace(FtpUsername))
						yield return new ValidationResult("FTP username is required.", [nameof(FtpUsername)]);
					if (string.IsNullOrWhiteSpace(FtpPassword))
						yield return new ValidationResult("FTP password is required.", [nameof(FtpPassword)]);
					break;

				case WorkflowCredentialType.Sftp:
					if (string.IsNullOrWhiteSpace(SftpHost))
						yield return new ValidationResult("SFTP host is required.", [nameof(SftpHost)]);
					if (string.IsNullOrWhiteSpace(SftpUsername))
						yield return new ValidationResult("SFTP username is required.", [nameof(SftpUsername)]);
					if (string.IsNullOrWhiteSpace(SftpPassword) && string.IsNullOrWhiteSpace(SftpPrivateKey))
						yield return new ValidationResult("Either an SFTP password or a private key is required.", [nameof(SftpPassword), nameof(SftpPrivateKey)]);
					break;

				case WorkflowCredentialType.AwsS3:
					if (string.IsNullOrWhiteSpace(AwsAccessKeyId))
						yield return new ValidationResult("AWS Access Key ID is required.", [nameof(AwsAccessKeyId)]);
					if (string.IsNullOrWhiteSpace(AwsSecretAccessKey))
						yield return new ValidationResult("AWS Secret Access Key is required.", [nameof(AwsSecretAccessKey)]);
					break;

				case WorkflowCredentialType.HttpBearer:
					if (string.IsNullOrWhiteSpace(HttpBearerToken))
						yield return new ValidationResult("Bearer token is required.", [nameof(HttpBearerToken)]);
					break;

				case WorkflowCredentialType.HttpBasic:
					if (string.IsNullOrWhiteSpace(HttpBasicUsername))
						yield return new ValidationResult("Username is required.", [nameof(HttpBasicUsername)]);
					if (string.IsNullOrWhiteSpace(HttpBasicPassword))
						yield return new ValidationResult("Password is required.", [nameof(HttpBasicPassword)]);
					break;

				case WorkflowCredentialType.HttpApiKey:
					if (string.IsNullOrWhiteSpace(ApiKeyHeaderName))
						yield return new ValidationResult("API key header name is required.", [nameof(ApiKeyHeaderName)]);
					if (string.IsNullOrWhiteSpace(ApiKeyValue))
						yield return new ValidationResult("API key value is required.", [nameof(ApiKeyValue)]);
					break;

				case WorkflowCredentialType.MicrosoftTeams:
					if (string.IsNullOrWhiteSpace(TeamsWebhookUrl))
						yield return new ValidationResult("Teams webhook URL is required.", [nameof(TeamsWebhookUrl)]);
					break;

				case WorkflowCredentialType.Slack:
					if (string.IsNullOrWhiteSpace(SlackWebhookUrl) && string.IsNullOrWhiteSpace(SlackBotToken))
						yield return new ValidationResult("Either a Slack webhook URL or bot token is required.", [nameof(SlackWebhookUrl), nameof(SlackBotToken)]);
					break;

				case WorkflowCredentialType.Discord:
					if (string.IsNullOrWhiteSpace(DiscordWebhookUrl) && string.IsNullOrWhiteSpace(DiscordBotToken))
						yield return new ValidationResult("Either a Discord webhook URL or bot token is required.", [nameof(DiscordWebhookUrl), nameof(DiscordBotToken)]);
					break;

				case WorkflowCredentialType.AzureBlobStorage:
					if (string.IsNullOrWhiteSpace(AzureConnectionString))
						yield return new ValidationResult("Azure connection string is required.", [nameof(AzureConnectionString)]);
					break;

				case WorkflowCredentialType.Box:
					if (string.IsNullOrWhiteSpace(BoxClientId))
						yield return new ValidationResult("Box client ID is required.", [nameof(BoxClientId)]);
					if (string.IsNullOrWhiteSpace(BoxClientSecret))
						yield return new ValidationResult("Box client secret is required.", [nameof(BoxClientSecret)]);
					if (string.IsNullOrWhiteSpace(BoxEnterpriseId))
						yield return new ValidationResult("Box enterprise ID is required.", [nameof(BoxEnterpriseId)]);
					if (string.IsNullOrWhiteSpace(BoxPrivateKey))
						yield return new ValidationResult("Box private key is required.", [nameof(BoxPrivateKey)]);
					if (string.IsNullOrWhiteSpace(BoxPublicKeyId))
						yield return new ValidationResult("Box public key ID is required.", [nameof(BoxPublicKeyId)]);
					break;

				case WorkflowCredentialType.Dropbox:
					if (string.IsNullOrWhiteSpace(DropboxRefreshToken))
						yield return new ValidationResult("Dropbox refresh token is required.", [nameof(DropboxRefreshToken)]);
					if (string.IsNullOrWhiteSpace(DropboxAppKey))
						yield return new ValidationResult("Dropbox app key is required.", [nameof(DropboxAppKey)]);
					if (string.IsNullOrWhiteSpace(DropboxAppSecret))
						yield return new ValidationResult("Dropbox app secret is required.", [nameof(DropboxAppSecret)]);
					break;
			}
		}
	}
}

