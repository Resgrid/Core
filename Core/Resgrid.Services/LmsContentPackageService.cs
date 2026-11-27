using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Reads an uploaded SCORM or cmi5 zip without unpacking it (T5 item 3): caps on size and file count, every entry path checked for
	/// zip-slip, declared sizes checked against the unpack cap and against zip-bomb compression ratios, then the manifest (imsmanifest.xml
	/// for SCORM 1.2 / 2004, cmi5.xml for cmi5) parsed with DTDs and external resolution off. Every launchable SCO or AU must name a file
	/// that is in the package (a cmi5 AU hosted elsewhere is refused: only packaged content runs inside Resgrid's sandbox).
	/// </summary>
	public static class LmsPackageInspector
	{
		private const int MaxManifestBytes = 5 * 1024 * 1024;
		private const long RatioCheckFloorBytes = 10L * 1024 * 1024;

		/// <summary>An entry path as it will be stored ("a/b/c.html"), or null when it could leave the package folder (zip-slip) or is malformed.</summary>
		public static string SafePath(string entryName)
		{
			if (string.IsNullOrEmpty(entryName) || entryName.Length > 400)
				return null;
			if (entryName.IndexOf('\\') >= 0 || entryName.IndexOf(':') >= 0 || entryName.StartsWith("/", StringComparison.Ordinal) || entryName.Any(c => c < 0x20 || c == 0x7f))
				return null;
			var trimmed = entryName.EndsWith("/", StringComparison.Ordinal) ? entryName.Substring(0, entryName.Length - 1) : entryName;
			var segments = trimmed.Split('/');
			if (segments.Any(s => s.Length == 0 || s == ".." || s == "."))
				return null;
			return string.Join("/", segments);
		}

		public static LmsPackageInspection Inspect(Stream zip, long length)
		{
			if (zip == null || length <= 0)
				return LmsPackageInspection.Fail(LmsPackageErrors.Empty);
			if (length > Config.LmsStandardsConfig.MaxPackageBytes)
				return LmsPackageInspection.Fail(LmsPackageErrors.TooLarge);

			try
			{
				using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
				var entries = archive.Entries;
				if (entries.Count > Config.LmsStandardsConfig.MaxPackageFiles)
					return LmsPackageInspection.Fail(LmsPackageErrors.TooManyFiles);

				var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
				long total = 0;
				foreach (var entry in entries)
				{
					var path = SafePath(entry.FullName);
					if (path == null)
						return LmsPackageInspection.Fail(LmsPackageErrors.UnsafePath, Shorten(entry.FullName));
					if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
						continue;
					if (entry.Length > Config.LmsStandardsConfig.MaxPackageFileBytes)
						return LmsPackageInspection.Fail(LmsPackageErrors.UncompressedTooLarge, path);
					if (entry.Length > RatioCheckFloorBytes && (entry.CompressedLength <= 0 || entry.Length / Math.Max(1, entry.CompressedLength) > Config.LmsStandardsConfig.MaxCompressionRatio))
						return LmsPackageInspection.Fail(LmsPackageErrors.SuspiciousCompression, path);
					total += entry.Length;
					if (total > Config.LmsStandardsConfig.MaxUncompressedBytes)
						return LmsPackageInspection.Fail(LmsPackageErrors.UncompressedTooLarge);
					if (files.ContainsKey(path))
						return LmsPackageInspection.Fail(LmsPackageErrors.UnsafePath, path);
					files[path] = entry;
				}

				LmsPackageInspection result;
				if (files.TryGetValue("imsmanifest.xml", out var scorm))
					result = ParseScorm(ReadManifest(scorm));
				else if (files.TryGetValue("cmi5.xml", out var cmi5))
					result = ParseCmi5(ReadManifest(cmi5));
				else
					return LmsPackageInspection.Fail(LmsPackageErrors.ManifestMissing);

				if (!result.Succeeded)
					return result;
				foreach (var item in result.Manifest.Items)
				{
					var file = LaunchPath(item.Href);
					if (file == null || !files.ContainsKey(file))
						return LmsPackageInspection.Fail(LmsPackageErrors.LaunchFileMissing, Shorten(item.Href));
				}
				result.FileCount = files.Count;
				result.UncompressedBytes = total;
				return result;
			}
			catch (InvalidDataException)
			{
				return LmsPackageInspection.Fail(LmsPackageErrors.NotZip);
			}
			catch (XmlException ex)
			{
				return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid, Shorten(ex.Message));
			}
			catch (ManifestTooLargeException)
			{
				return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid);
			}
		}

		/// <summary>The package file a launch href opens (its path without query or fragment, %-decoded and checked), or null.</summary>
		public static string LaunchPath(string href)
		{
			if (string.IsNullOrWhiteSpace(href) || href.Contains("://", StringComparison.Ordinal) || href.StartsWith("//", StringComparison.Ordinal))
				return null;
			var path = href;
			var cut = path.IndexOfAny(new[] { '?', '#' });
			if (cut >= 0)
				path = path.Substring(0, cut);
			try
			{
				path = Uri.UnescapeDataString(path);
			}
			catch (UriFormatException)
			{
				return null;
			}
			return SafePath(path);
		}

		private sealed class ManifestTooLargeException : Exception { }

		private static XDocument ReadManifest(ZipArchiveEntry entry)
		{
			if (entry.Length > MaxManifestBytes)
				throw new ManifestTooLargeException();
			using var stream = entry.Open();
			using var limited = new MemoryStream();
			var buffer = new byte[81920];
			int read;
			while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
			{
				limited.Write(buffer, 0, read);
				if (limited.Length > MaxManifestBytes)
					throw new ManifestTooLargeException();
			}
			limited.Position = 0;
			var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxManifestBytes * 2L, IgnoreComments = true };
			using var reader = XmlReader.Create(limited, settings);
			return XDocument.Load(reader, LoadOptions.None);
		}

		private static IEnumerable<XElement> Children(XElement parent, string localName) =>
			parent?.Elements().Where(e => e.Name.LocalName == localName) ?? Enumerable.Empty<XElement>();

		private static XElement Child(XElement parent, string localName) => Children(parent, localName).FirstOrDefault();

		private static string Attr(XElement element, string localName) =>
			element?.Attributes().FirstOrDefault(a => a.Name.LocalName == localName && (a.Name.Namespace == XNamespace.None || a.Name.LocalName != "base"))?.Value?.Trim();

		private static string XmlBase(XElement element) => element?.Attribute(XNamespace.Xml + "base")?.Value?.Trim() ?? string.Empty;

		private static string Text(XElement element) => element?.Value?.Trim();

		private static decimal? Decimal(string value) =>
			decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null;

		private static string Shorten(string value) => value == null ? null : value.Length <= 200 ? value : value.Substring(0, 200);

		internal static LmsPackageInspection ParseScorm(XDocument document)
		{
			var root = document?.Root;
			if (root == null || root.Name.LocalName != "manifest")
				return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid, "manifest");

			var schemaVersion = Text(Child(Child(root, "metadata"), "schemaversion")) ?? string.Empty;
			var namespaces = root.DescendantsAndSelf().SelectMany(e => e.Attributes()).Where(a => a.IsNamespaceDeclaration).Select(a => a.Value).ToList();
			int standard;
			if (schemaVersion.Contains("2004", StringComparison.OrdinalIgnoreCase) || schemaVersion.Contains("1.3", StringComparison.Ordinal) ||
				(schemaVersion.Length == 0 && namespaces.Any(n => n.Contains("adlcp_v1p3", StringComparison.OrdinalIgnoreCase))))
				standard = (int)LmsContentStandards.Scorm2004;
			else if (schemaVersion.Contains("1.2", StringComparison.Ordinal) || (schemaVersion.Length == 0 && namespaces.Any(n => n.Contains("adlcp_rootv1p2", StringComparison.OrdinalIgnoreCase))))
				standard = (int)LmsContentStandards.Scorm12;
			else
				return LmsPackageInspection.Fail(LmsPackageErrors.UnsupportedStandard, Shorten(schemaVersion));

			// 2004 2nd edition (CAM 1.3) and 3rd/4th edition share the runtime; only "1.2" and "2004 ..." / "CAM 1.3" are accepted.
			var organizations = Child(root, "organizations");
			var defaultId = Attr(organizations, "default");
			var organization = Children(organizations, "organization").FirstOrDefault(o => string.Equals(Attr(o, "identifier"), defaultId, StringComparison.Ordinal))
				?? Children(organizations, "organization").FirstOrDefault();
			if (organization == null)
				return LmsPackageInspection.Fail(LmsPackageErrors.NoLaunchableItem);

			var resourcesElement = Child(root, "resources");
			var manifestBase = XmlBase(root) + XmlBase(resourcesElement);
			var resources = new Dictionary<string, (string Href, bool IsSco)>(StringComparer.Ordinal);
			foreach (var resource in Children(resourcesElement, "resource"))
			{
				var id = Attr(resource, "identifier");
				if (string.IsNullOrWhiteSpace(id) || resources.ContainsKey(id))
					continue;
				var type = resource.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("scormtype", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
				var href = Attr(resource, "href");
				resources[id] = (string.IsNullOrWhiteSpace(href) ? null : manifestBase + XmlBase(resource) + href, string.Equals(type, "sco", StringComparison.OrdinalIgnoreCase));
			}

			var manifest = new LmsPackageManifest
			{
				Standard = standard,
				Identifier = Shorten(Attr(root, "identifier")),
				SchemaVersion = standard == (int)LmsContentStandards.Scorm12 ? "1.2" : (schemaVersion.Length == 0 ? "2004" : Shorten(schemaVersion)),
				Title = Shorten(Text(Child(organization, "title"))) ?? Shorten(Attr(root, "identifier"))
			};

			void Walk(XElement parent)
			{
				foreach (var item in Children(parent, "item"))
				{
					var reference = Attr(item, "identifierref");
					if (!string.IsNullOrWhiteSpace(reference) && resources.TryGetValue(reference, out var resource) && resource.IsSco && resource.Href != null &&
						!string.Equals(Attr(item, "isvisible"), "false", StringComparison.OrdinalIgnoreCase))
					{
						var parameters = Attr(item, "parameters");
						var href = resource.Href;
						if (!string.IsNullOrWhiteSpace(parameters))
							href += parameters.StartsWith("?", StringComparison.Ordinal) && href.Contains('?') ? "&" + parameters.Substring(1)
								: parameters.StartsWith("?", StringComparison.Ordinal) || parameters.StartsWith("#", StringComparison.Ordinal) ? parameters : "?" + parameters;
						var entry = new LmsPackageItem
						{
							Identifier = Shorten(Attr(item, "identifier")) ?? reference,
							Title = Shorten(Text(Child(item, "title"))) ?? manifest.Title,
							Href = href,
							LaunchData = Text(Child(item, standard == (int)LmsContentStandards.Scorm12 ? "datafromlms" : "dataFromLMS")) ?? Text(Child(item, "datafromlms"))
						};
						if (entry.LaunchData != null && entry.LaunchData.Length > 4096)
							entry.LaunchData = entry.LaunchData.Substring(0, 4096);
						if (standard == (int)LmsContentStandards.Scorm12)
						{
							var mastery = Decimal(Text(Child(item, "masteryscore")));
							entry.MasteryScore = mastery.HasValue && mastery.Value >= 0 && mastery.Value <= 100 ? mastery : null;
						}
						else
						{
							var threshold = Child(item, "completionThreshold");
							var measure = Decimal(Attr(threshold, "minProgressMeasure")) ?? Decimal(Text(threshold));
							entry.CompletionThreshold = measure.HasValue && measure.Value >= 0 && measure.Value <= 1 ? measure : null;
							var primary = Child(Child(Child(item, "sequencing"), "objectives"), "primaryObjective");
							if (primary != null && string.Equals(Attr(primary, "satisfiedByMeasure"), "true", StringComparison.OrdinalIgnoreCase))
							{
								var minimum = Decimal(Text(Child(primary, "minNormalizedMeasure"))) ?? 1m;
								entry.MasteryScore = minimum >= -1 && minimum <= 1 ? minimum : null;
							}
						}
						if (manifest.Items.All(i => i.Identifier != entry.Identifier))
							manifest.Items.Add(entry);
					}
					Walk(item);
				}
			}
			Walk(organization);

			if (manifest.Items.Count == 0)
				return LmsPackageInspection.Fail(LmsPackageErrors.NoLaunchableItem);
			return new LmsPackageInspection { Manifest = manifest };
		}

		internal static LmsPackageInspection ParseCmi5(XDocument document)
		{
			var root = document?.Root;
			if (root == null || root.Name.LocalName != "courseStructure")
				return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid, "courseStructure");
			var course = Child(root, "course");
			var courseId = Attr(course, "id");
			if (course == null || string.IsNullOrWhiteSpace(courseId) || !Uri.TryCreate(courseId, UriKind.Absolute, out _))
				return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid, "course id");

			string LangText(XElement parent, string name) => Shorten(Text(Child(Child(parent, name), "langstring")) ?? Text(Child(parent, name)));

			var manifest = new LmsPackageManifest
			{
				Standard = (int)LmsContentStandards.Cmi5,
				Identifier = Shorten(courseId),
				SchemaVersion = "cmi5",
				Title = LangText(course, "title") ?? Shorten(courseId)
			};

			var moveOnValues = new HashSet<string>(StringComparer.Ordinal) { "Passed", "Completed", "CompletedAndPassed", "CompletedOrPassed", "NotApplicable" };
			foreach (var au in root.Descendants().Where(e => e.Name.LocalName == "au"))
			{
				var id = Attr(au, "id");
				var url = Text(Child(au, "url"));
				if (string.IsNullOrWhiteSpace(id) || !Uri.TryCreate(id, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(url))
					return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid, Shorten(id ?? "au"));
				if (url.Contains("://", StringComparison.Ordinal) || url.StartsWith("//", StringComparison.Ordinal))
					return LmsPackageInspection.Fail(LmsPackageErrors.RemoteAuNotSupported, Shorten(url));
				var moveOn = Attr(au, "moveOn");
				if (!string.IsNullOrWhiteSpace(moveOn) && !moveOnValues.Contains(moveOn))
					return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid, "moveOn " + Shorten(moveOn));
				var mastery = Decimal(Attr(au, "masteryScore"));
				var parameters = Text(Child(au, "launchParameters"));
				if (manifest.Items.Any(i => i.Identifier == id))
					return LmsPackageInspection.Fail(LmsPackageErrors.ManifestInvalid, Shorten(id));
				manifest.Items.Add(new LmsPackageItem
				{
					Identifier = Shorten(id),
					Title = LangText(au, "title") ?? manifest.Title,
					Href = url,
					LaunchData = parameters != null && parameters.Length > 4096 ? parameters.Substring(0, 4096) : parameters,
					MoveOn = string.IsNullOrWhiteSpace(moveOn) ? "NotApplicable" : moveOn,
					MasteryScore = mastery.HasValue && mastery.Value >= 0 && mastery.Value <= 1 ? mastery : null,
					LaunchMethod = string.Equals(Attr(au, "launchMethod"), "OwnWindow", StringComparison.Ordinal) ? "OwnWindow" : "AnyWindow"
				});
			}

			if (manifest.Items.Count == 0)
				return LmsPackageInspection.Fail(LmsPackageErrors.NoLaunchableItem);
			return new LmsPackageInspection { Manifest = manifest };
		}

		/// <summary>The MIME type a package file is served with (unknown types download as octet-stream).</summary>
		public static string ContentTypeFor(string path)
		{
			switch (Path.GetExtension(path ?? string.Empty).ToLowerInvariant())
			{
				case ".html": case ".htm": return "text/html; charset=utf-8";
				case ".js": case ".mjs": return "text/javascript";
				case ".css": return "text/css";
				case ".json": return "application/json";
				case ".xml": case ".xsd": return "application/xml";
				case ".txt": return "text/plain; charset=utf-8";
				case ".vtt": return "text/vtt";
				case ".png": return "image/png";
				case ".jpg": case ".jpeg": return "image/jpeg";
				case ".gif": return "image/gif";
				case ".svg": return "image/svg+xml";
				case ".webp": return "image/webp";
				case ".ico": return "image/x-icon";
				case ".mp4": case ".m4v": return "video/mp4";
				case ".webm": return "video/webm";
				case ".mp3": return "audio/mpeg";
				case ".m4a": return "audio/mp4";
				case ".wav": return "audio/wav";
				case ".ogg": return "audio/ogg";
				case ".woff": return "font/woff";
				case ".woff2": return "font/woff2";
				case ".ttf": return "font/ttf";
				case ".otf": return "font/otf";
				case ".eot": return "application/vnd.ms-fontobject";
				case ".pdf": return "application/pdf";
				default: return "application/octet-stream";
			}
		}

		public static bool IsHtml(string path)
		{
			var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
			return extension == ".html" || extension == ".htm";
		}
	}

	/// <summary>A read-only stream that throws once more than its limit was read (an entry that unpacks past its declared size).</summary>
	internal sealed class LmsLimitedReadStream : Stream
	{
		private readonly Stream _inner;
		private readonly long _limit;
		private long _read;

		public LmsLimitedReadStream(Stream inner, long limit)
		{
			_inner = inner;
			_limit = limit;
		}

		public long BytesRead => _read;

		public override int Read(byte[] buffer, int offset, int count)
		{
			var n = _inner.Read(buffer, offset, count);
			_read += n;
			if (_read > _limit)
				throw new InvalidDataException("lms_package_entry_exceeds_declared_size");
			return n;
		}

		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			var n = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
			_read += n;
			if (_read > _limit)
				throw new InvalidDataException("lms_package_entry_exceeds_declared_size");
			return n;
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => _read; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_inner.Dispose();
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// SCORM and cmi5 package import (T5 item 3): authors with Manage Courses (or a course owner's edit right) upload a zip while the add-on
	/// is active (lapse rule 3); it is validated before anything is stored, kept as uploaded in the LMS media store and unpacked by worker 98
	/// under its own prefix ({S3Prefix}/{departmentId}/shared/packages/{key}/content/). Removing a package only hides it from new lessons.
	/// </summary>
	public class LmsContentPackageService : ILmsContentPackageService
	{
		/// <summary>A Processing claim older than this goes back to the queue (a worker that stopped mid-unpack).</summary>
		public static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(30);

		public const int MaxAttempts = 3;

		private readonly ILmsMediaStore _store;
		private readonly ILmsContentPackagesRepository _packages;
		private readonly ILmsCourseOwnersRepository _owners;
		private readonly IEventAggregator _eventAggregator;

		public LmsContentPackageService(ILmsMediaStore store, ILmsContentPackagesRepository packages, ILmsCourseOwnersRepository owners, IEventAggregator eventAggregator)
		{
			_store = store;
			_packages = packages;
			_owners = owners;
			_eventAggregator = eventAggregator;
		}

		public bool IsStoreConfigured => _store?.IsConfigured == true;

		/// <summary>Who may upload or remove packages: LMS managers and course owners who may edit a course (like hosted video).</summary>
		internal async Task<bool> MayAuthorAsync(LmsActor actor)
		{
			if (actor == null || !actor.IsCurrentMember)
				return false;
			if (actor.MayManage)
				return true;
			return (await _owners.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsCourseOwner>()).Any(o => o.CanEdit);
		}

		/// <summary>The error a refused inspection maps to (size problems are "too large", everything else "unsupported file").</summary>
		public static LmsErrors ErrorFor(LmsPackageErrors error)
		{
			switch (error)
			{
				case LmsPackageErrors.None: return LmsErrors.None;
				case LmsPackageErrors.TooLarge:
				case LmsPackageErrors.TooManyFiles:
				case LmsPackageErrors.UncompressedTooLarge:
					return LmsErrors.FileTooLarge;
				case LmsPackageErrors.Empty:
					return LmsErrors.InvalidInput;
				default:
					return LmsErrors.UnsupportedFile;
			}
		}

		public async Task<LmsResult> UploadAsync(LmsActor actor, string fileName, Stream content, long length, CancellationToken cancellationToken = default)
		{
			if (!await MayAuthorAsync(actor))
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanUploadMedia(actor.Gate))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			if (!IsStoreConfigured)
				return LmsResult.Fail(LmsErrors.MediaUnavailable);
			if (content == null || length <= 0)
				return new LmsResult { Error = LmsErrors.InvalidInput, Value = LmsPackageInspection.Fail(LmsPackageErrors.Empty) };
			if (length > Config.LmsStandardsConfig.MaxPackageBytes)
				return new LmsResult { Error = LmsErrors.FileTooLarge, Value = LmsPackageInspection.Fail(LmsPackageErrors.TooLarge) };

			Stream seekable = content;
			FileStream temp = null;
			try
			{
				if (!content.CanSeek)
				{
					temp = new FileStream(Path.Combine(Path.GetTempPath(), "resgrid-lms-pkg-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.ReadWrite,
						FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
					await CopyLimitedAsync(content, temp, Config.LmsStandardsConfig.MaxPackageBytes, cancellationToken);
					temp.Position = 0;
					seekable = temp;
					length = temp.Length;
				}

				var start = seekable.Position;
				var inspection = LmsPackageInspector.Inspect(seekable, length);
				if (!inspection.Succeeded)
					return new LmsResult { Error = ErrorFor(inspection.Error), Value = inspection };

				seekable.Position = start;
				string sha;
				using (var sha256 = SHA256.Create())
					sha = string.Concat((await sha256.ComputeHashAsync(seekable, cancellationToken)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
				seekable.Position = start;

				// The object key carries its own random part, so the row is written only once the zip is in the store (no half-uploaded row).
				var folder = "packages/" + Guid.NewGuid().ToString("N") + "/";
				var sourceKey = _store.KeyFor(actor.DepartmentId, 0, folder + "source.zip");
				await _store.PutAsync(sourceKey, seekable, "application/zip", cancellationToken);

				var package = new LmsContentPackage
				{
					DepartmentId = actor.DepartmentId,
					Standard = inspection.Manifest.Standard,
					Title = LmsCourseService.Truncate(inspection.Manifest.Title ?? Path.GetFileNameWithoutExtension(fileName ?? "package"), LmsContentPackage.TitleMaxLength),
					Identifier = LmsCourseService.Truncate(inspection.Manifest.Identifier, 500),
					SchemaVersion = LmsCourseService.Truncate(inspection.Manifest.SchemaVersion, 50),
					FileName = LmsCourseService.Truncate(Path.GetFileName(fileName ?? "package.zip"), 260),
					Sha256 = sha,
					Status = (int)LmsContentPackageStatuses.Queued,
					SourceKey = sourceKey,
					SourceBytes = length,
					ContentPrefix = _store.KeyFor(actor.DepartmentId, 0, folder + "content/"),
					FileCount = inspection.FileCount,
					ContentBytes = inspection.UncompressedBytes,
					ManifestJson = inspection.Manifest.ToJson(),
					CreatedOn = DateTime.UtcNow,
					CreatedByUserId = actor.UserId
				};
				await _packages.InsertAsync(package, cancellationToken, true);

				_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = actor.DepartmentId,
					UserId = actor.UserId,
					Type = AuditLogTypes.LmsContentPackageUploaded,
					After = JsonConvert.SerializeObject(new { package.LmsContentPackageId, package.Title, package.Standard, package.FileCount, package.SourceBytes, package.Sha256 }),
					Successful = true,
					ServerName = Environment.MachineName
				});
				return new LmsResult { Value = package, Count = inspection.Manifest.Items.Count };
			}
			catch (InvalidDataException)
			{
				return new LmsResult { Error = LmsErrors.FileTooLarge, Value = LmsPackageInspection.Fail(LmsPackageErrors.TooLarge) };
			}
			finally
			{
				temp?.Dispose();
			}
		}

		private static async Task CopyLimitedAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
		{
			var buffer = new byte[81920];
			long total = 0;
			int read;
			while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
			{
				total += read;
				if (total > limit)
					throw new InvalidDataException("lms_package_too_large");
				await target.WriteAsync(buffer, 0, read, cancellationToken);
			}
		}

		public async Task<List<LmsContentPackage>> GetPackagesAsync(int departmentId, bool includeDeleted = false) =>
			await _packages.GetForDepartmentAsync(departmentId, includeDeleted) ?? new List<LmsContentPackage>();

		public Task<LmsContentPackage> GetPackageAsync(int departmentId, int lmsContentPackageId) => _packages.GetByIdForDepartmentAsync(departmentId, lmsContentPackageId);

		public async Task<LmsResult> DeleteAsync(LmsActor actor, int lmsContentPackageId, CancellationToken cancellationToken = default)
		{
			if (!await MayAuthorAsync(actor))
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanUploadMedia(actor.Gate))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			var package = await _packages.GetByIdForDepartmentAsync(actor.DepartmentId, lmsContentPackageId);
			if (package == null || package.IsDeleted)
				return LmsResult.Fail(LmsErrors.NotFound);

			package.IsDeleted = true;
			package.DeletedOn = DateTime.UtcNow;
			package.DeletedByUserId = actor.UserId;
			await _packages.UpdateAsync(package, cancellationToken, true);
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsContentPackageDeleted,
				Before = JsonConvert.SerializeObject(new { package.LmsContentPackageId, package.Title }),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return new LmsResult { Value = package };
		}

		#region Worker 98

		public async Task<int> ProcessQueueAsync(int maxItems, CancellationToken cancellationToken = default)
		{
			if (!IsStoreConfigured)
				return 0;

			var finished = 0;
			for (var i = 0; i < Math.Max(1, maxItems); i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var now = DateTime.UtcNow;
				var package = await _packages.ClaimNextAsync(now, now - StaleClaim, cancellationToken);
				if (package == null)
					break;
				await UnpackAsync(package, cancellationToken);
				finished++;
			}
			return finished;
		}

		/// <summary>One claimed package: download the zip, inspect it again, write every file under the content prefix; Ready, or back to the queue / Failed.</summary>
		internal async Task UnpackAsync(LmsContentPackage package, CancellationToken cancellationToken)
		{
			var tempPath = Path.Combine(Path.GetTempPath(), "resgrid-lms-pkg-" + package.LmsContentPackageId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".zip");
			try
			{
				using (var input = await _store.OpenReadAsync(package.SourceKey, cancellationToken))
				using (var file = System.IO.File.Create(tempPath))
					await CopyLimitedAsync(input, file, Config.LmsStandardsConfig.MaxPackageBytes, cancellationToken);

				using (var zip = System.IO.File.OpenRead(tempPath))
				{
					var inspection = LmsPackageInspector.Inspect(zip, zip.Length);
					if (!inspection.Succeeded)
						throw new InvalidDataException("lms_package_invalid:" + inspection.Error);
					zip.Position = 0;

					long bytes = 0;
					var count = 0;
					using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
					foreach (var entry in archive.Entries)
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
							continue;
						var path = LmsPackageInspector.SafePath(entry.FullName) ?? throw new InvalidDataException("lms_package_unsafe_path");
						using var limited = new LmsLimitedReadStream(entry.Open(), Math.Min(entry.Length, Config.LmsStandardsConfig.MaxPackageFileBytes));
						using var buffered = new MemoryStream();
						await limited.CopyToAsync(buffered, 81920, cancellationToken);
						buffered.Position = 0;
						await _store.PutAsync(package.ContentPrefix + path, buffered, LmsPackageInspector.ContentTypeFor(path), cancellationToken);
						bytes += buffered.Length;
						count++;
					}

					package.FileCount = count;
					package.ContentBytes = bytes;
				}

				package.LastError = null;
				package.ReadyOn = DateTime.UtcNow;
				package.Status = (int)LmsContentPackageStatuses.Ready;
				await _packages.TryMoveAsync(package, LmsContentPackageStatuses.Processing, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Unpacking LMS content package {package.LmsContentPackageId} failed (attempt {package.Attempts}).");
				package.LastError = LmsCourseService.Truncate(ex.Message, 2000);
				package.Status = package.Attempts >= MaxAttempts || ex is InvalidDataException ? (int)LmsContentPackageStatuses.Failed : (int)LmsContentPackageStatuses.Queued;
				await _packages.TryMoveAsync(package, LmsContentPackageStatuses.Processing, CancellationToken.None);
			}
			finally
			{
				try
				{
					if (System.IO.File.Exists(tempPath))
						System.IO.File.Delete(tempPath);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "An LMS package work file could not be removed.");
				}
			}
		}

		#endregion Worker 98
	}

	/// <summary>The course builder's rules for Scorm, Cmi5 and LtiLink lessons (T5): the department's own, usable package or tool.</summary>
	public class LmsStandardsLessonValidator : ILmsStandardsLessonValidator
	{
		private readonly ILmsContentPackagesRepository _packages;
		private readonly ILmsLtiToolsRepository _tools;

		public LmsStandardsLessonValidator(ILmsContentPackagesRepository packages, ILmsLtiToolsRepository tools)
		{
			_packages = packages;
			_tools = tools;
		}

		public async Task<LmsErrors> ApplyAsync(LmsActor actor, LmsLesson lesson, LmsLessonInput input)
		{
			if (actor == null || lesson == null || input == null)
				return LmsErrors.InvalidInput;

			switch ((LmsLessonKinds)input.Kind)
			{
				case LmsLessonKinds.Scorm:
				case LmsLessonKinds.Cmi5:
					var package = input.LmsContentPackageId.HasValue ? await _packages.GetByIdForDepartmentAsync(actor.DepartmentId, input.LmsContentPackageId.Value) : null;
					if (package == null || package.DepartmentId != actor.DepartmentId || package.IsDeleted || package.Status == (int)LmsContentPackageStatuses.Failed)
						return LmsErrors.InvalidInput;
					var cmi5 = package.Standard == (int)LmsContentStandards.Cmi5;
					if (cmi5 != (input.Kind == (int)LmsLessonKinds.Cmi5))
						return LmsErrors.InvalidInput;
					lesson.LmsContentPackageId = package.LmsContentPackageId;
					return LmsErrors.None;

				case LmsLessonKinds.LtiLink:
					var tool = input.LmsLtiToolId.HasValue ? await _tools.GetByIdForDepartmentAsync(actor.DepartmentId, input.LmsLtiToolId.Value) : null;
					if (tool == null || tool.DepartmentId != actor.DepartmentId || tool.IsDeleted || !tool.IsActive)
						return LmsErrors.InvalidInput;
					var target = input.LtiTargetLinkUri?.Trim();
					if (!string.IsNullOrEmpty(target) && !LmsStandardsUrls.IsAcceptableHttpsUrl(target))
						return LmsErrors.InvalidInput;
					if (input.PassMark < 0 || input.PassMark > 100 || (input.LtiCustomParameters?.Length ?? 0) > 4000)
						return LmsErrors.InvalidInput;
					lesson.LmsLtiToolId = tool.LmsLtiToolId;
					lesson.LtiTargetLinkUri = string.IsNullOrEmpty(target) ? null : target;
					lesson.LtiCustomParameters = string.IsNullOrWhiteSpace(input.LtiCustomParameters) ? null : input.LtiCustomParameters.Trim();
					// Without grade passback the tool can never report a score, so completion cannot wait for one.
					lesson.PassMark = tool.AllowGradePassback ? input.PassMark : 0;
					return LmsErrors.None;

				default:
					return LmsErrors.InvalidInput;
			}
		}
	}
}
