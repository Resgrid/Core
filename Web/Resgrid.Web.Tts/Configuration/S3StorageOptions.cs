using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Tts.Configuration
{
	public sealed class S3StorageOptions
	{
		public string? Endpoint { get; set; }

		[Required]
		public string AccessKey { get; set; } = string.Empty;

		[Required]
		public string SecretKey { get; set; } = string.Empty;

		[Required]
		public string Bucket { get; set; } = string.Empty;

		[Required]
		public string Region { get; set; } = "us-east-1";

		public bool UseSsl { get; set; } = true;

		public bool ForcePathStyle { get; set; } = true;

		public bool UsePresignedUrls { get; set; } = true;

		[Range(1, 1440)]
		public int PresignedUrlExpiryMinutes { get; set; } = 60;

		public string? PublicBaseUrl { get; set; }
	}
}
