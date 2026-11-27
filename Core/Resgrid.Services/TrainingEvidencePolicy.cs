using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Services.Records;

namespace Resgrid.Services
{
	/// <summary>The outcome of checking one evidence file: the file as it will be stored, or the refusal code.</summary>
	public sealed class TrainingEvidenceCheck
	{
		public TrainingEvidenceUpload File { get; set; }

		public string Refusal { get; set; }

		public string Sha256 { get; set; }

		/// <summary>The scanner's verdict (RmsAttachmentScanState), set by the service after this check.</summary>
		public int ScanState { get; set; } = (int)RmsAttachmentScanState.Skipped;
	}

	/// <summary>
	/// Evidence files of training hours submissions (round 9B): a certificate (PDF), a photo (JPEG, PNG, GIF, WebP), a document
	/// (PDF, DOC, DOCX, ODT) or a receipt. Only these types, by extension, and the bytes must start the way the type does (a renamed file is
	/// refused). Photos pass through the Records media hygiene (decoded and re-encoded: EXIF, GPS and device data never reach storage);
	/// documents are stored as uploaded. HEIC/HEIF is refused: nothing here can decode it to strip its location and device metadata (an
	/// iPhone hands a browser JPEG when the picker does not list HEIC; a mobile client must convert to JPEG before upload). Each file at
	/// most <see cref="LearningRecordFile.MaxBytes"/>, at most <see cref="TrainingSubmissionRules.MaxFilesPerUpload"/> files and <see cref="TrainingSubmissionRules.MaxUploadBytes"/> per upload. The
	/// attachment scanner (IRecordAttachmentScanner, ClamAV when configured) runs after this, in the service.
	/// </summary>
	public static class TrainingEvidencePolicy
	{
		public const string TypeRefused = "learning_record_file_type";
		public const string TooLarge = "learning_record_file_too_large";
		public const string ContentMismatch = "learning_record_file_content";
		public const string TooMany = "learning_record_too_many_files";
		public const string ScannerRejected = "learning_record_file_rejected";
		public const string Empty = "learning_record_file_empty";

		/// <summary>Accepted extensions and the content type each is stored as.</summary>
		public static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "pdf", "application/pdf" },
			{ "jpg", "image/jpeg" },
			{ "jpeg", "image/jpeg" },
			{ "png", "image/png" },
			{ "gif", "image/gif" },
			{ "webp", "image/webp" },
			{ "doc", "application/msword" },
			{ "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
			{ "odt", "application/vnd.oasis.opendocument.text" }
		};

		/// <summary>The accept attribute of the file inputs.</summary>
		public static string AcceptAttribute => string.Join(",", Types.Keys.Select(k => "." + k));

		/// <summary>Raster types the media hygiene decodes and re-encodes (metadata stripped).</summary>
		private static readonly HashSet<string> Raster = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "gif", "webp" };

		/// <summary>Types a browser shows inline on the review page (the rest download).</summary>
		public static bool IsInlineImage(string contentType) =>
			contentType != null && (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) || contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
				contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase) || contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase));

		public static bool IsPdf(string contentType) => string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

		/// <summary>The stored content type of a file name's extension, or null when the type is not accepted.</summary>
		public static string ContentTypeOf(string fileName)
		{
			var extension = Path.GetExtension(FileHelper.GetSafeFileName(fileName ?? string.Empty) ?? string.Empty).TrimStart('.');
			return Types.TryGetValue(extension, out var type) ? type : null;
		}

		/// <summary>Checks the count and total size of one upload before any file is read further.</summary>
		public static string CheckUpload(IReadOnlyCollection<TrainingEvidenceUpload> uploads, int alreadyStored = 0)
		{
			var files = (uploads ?? Array.Empty<TrainingEvidenceUpload>()).Where(u => u != null && u.Data != null && u.Data.Length > 0).ToList();
			if (files.Count > TrainingSubmissionRules.MaxFilesPerUpload || files.Count + Math.Max(0, alreadyStored) > TrainingSubmissionRules.MaxFiles)
				return TooMany;
			if (files.Sum(f => (long)f.Data.Length) > TrainingSubmissionRules.MaxUploadBytes)
				return TooLarge;
			return null;
		}

		/// <summary>Checks and cleans one file: type, size, the leading bytes, and the media hygiene for photos.</summary>
		public static TrainingEvidenceCheck Check(TrainingEvidenceUpload upload)
		{
			if (upload?.Data == null || upload.Data.Length == 0)
				return new TrainingEvidenceCheck { Refusal = Empty };
			if (upload.Data.LongLength > LearningRecordFile.MaxBytes)
				return new TrainingEvidenceCheck { Refusal = TooLarge };

			var name = FileHelper.GetSafeFileName(upload.FileName ?? string.Empty);
			if (string.IsNullOrWhiteSpace(name))
				return new TrainingEvidenceCheck { Refusal = TypeRefused };
			if (name.Length > 200)
				name = Path.GetFileNameWithoutExtension(name).Substring(0, Math.Min(180, Path.GetFileNameWithoutExtension(name).Length)) + Path.GetExtension(name);

			var extension = Path.GetExtension(name).TrimStart('.');
			if (!Types.TryGetValue(extension, out var contentType))
				return new TrainingEvidenceCheck { Refusal = TypeRefused };
			if (!StartsLike(extension, upload.Data))
				return new TrainingEvidenceCheck { Refusal = ContentMismatch };

			var data = upload.Data;
			if (Raster.Contains(extension))
			{
				try
				{
					var clean = RecordAttachmentHygiene.Sanitize(name, contentType, data);
					if (!clean.IsImage || clean.Data == null || clean.Data.Length == 0)
						return new TrainingEvidenceCheck { Refusal = ContentMismatch };
					if (clean.Data.LongLength > LearningRecordFile.MaxBytes)
						return new TrainingEvidenceCheck { Refusal = TooLarge };
					data = clean.Data;
					contentType = clean.ContentType ?? contentType;
				}
				catch (RecordAttachmentRejectedException)
				{
					return new TrainingEvidenceCheck { Refusal = ContentMismatch };
				}
			}

			return new TrainingEvidenceCheck
			{
				File = new TrainingEvidenceUpload
				{
					FileName = name,
					ContentType = contentType,
					Data = data,
					Kind = (int)TrainingSubmissionRules.KindOf(upload.Kind, contentType)
				},
				Sha256 = Sha256Of(data)
			};
		}

		public static string Sha256Of(byte[] data)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(data ?? Array.Empty<byte>())).ToLowerInvariant();
		}

		/// <summary>True when the bytes start the way files of the extension do (the file signature).</summary>
		public static bool StartsLike(string extension, byte[] data)
		{
			if (data == null || data.Length < 4)
				return false;

			bool Has(int offset, params byte[] signature) => data.Length >= offset + signature.Length && !signature.Where((b, i) => data[offset + i] != b).Any();
			bool Ascii(int offset, string text) => Has(offset, text.Select(c => (byte)c).ToArray());

			switch ((extension ?? string.Empty).ToLowerInvariant())
			{
				case "pdf":
					// Some generators put a few bytes before the header; PDF readers accept it within the first kilobyte.
					var head = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(1024, data.Length));
					return head.IndexOf("%PDF-", StringComparison.Ordinal) >= 0;
				case "jpg":
				case "jpeg":
					return Has(0, 0xFF, 0xD8, 0xFF);
				case "png":
					return Has(0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
				case "gif":
					return Ascii(0, "GIF87a") || Ascii(0, "GIF89a");
				case "webp":
					return Ascii(0, "RIFF") && Ascii(8, "WEBP");
				case "doc":
					return Has(0, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1);
				case "docx":
				case "odt":
					return Has(0, 0x50, 0x4B, 0x03, 0x04);
				default:
					return false;
			}
		}
	}
}
