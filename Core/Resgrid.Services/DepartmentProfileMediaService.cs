using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Department Profile elevation (RMS plan section 4.10.1). The profile row is the identity source; logo bytes and
	/// renditions live in DepartmentProfileMedia under one per-department MediaKey. A logo migrated from the legacy
	/// DepartmentProfile.Logo column arrives as a PrimaryLogo row without renditions; they are generated on first
	/// read, and an undecodable legacy blob is simply ignored rather than served.
	/// </summary>
	public class DepartmentProfileMediaService : IDepartmentProfileMediaService
	{
		private readonly IDepartmentProfileRepository _profiles;
		private readonly IDepartmentProfileMediaRepository _media;
		private readonly IDepartmentsService _departments;
		private readonly IUnitOfWork _unitOfWork;
		private readonly ICacheProvider _cacheProvider;

		private static string EmailBrandingCacheKey = "DepartmentEmailBranding_{0}";
		private static TimeSpan EmailBrandingCacheLength = TimeSpan.FromDays(1);

		public DepartmentProfileMediaService(IDepartmentProfileRepository profiles, IDepartmentProfileMediaRepository media, IDepartmentsService departments, IUnitOfWork unitOfWork,
			ICacheProvider cacheProvider)
		{
			_profiles = profiles;
			_media = media;
			_departments = departments;
			_unitOfWork = unitOfWork;
			_cacheProvider = cacheProvider;
		}

		public async Task<DepartmentProfile> GetOrCreateProfileAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var existing = await _profiles.GetByDepartmentIdAsync(departmentId);
			if (existing != null)
				return existing;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var profile = new DepartmentProfile
			{
				DepartmentId = departmentId,
				Name = department?.Name,
				Code = department?.Code,
				AddressId = department?.AddressId,
				Disabled = false
			};

			return await _profiles.SaveOrUpdateAsync(profile, cancellationToken, true);
		}

		public Task<DepartmentProfile> SaveProfileAsync(DepartmentProfile profile, CancellationToken cancellationToken = default)
		{
			if (profile == null)
				throw new ArgumentNullException(nameof(profile));

			// The legacy Logo column is never written again; the media table owns the bytes now.
			profile.Logo = null;
			return SaveProfileAndInvalidateAsync(profile, cancellationToken);
		}

		private async Task<DepartmentProfile> SaveProfileAndInvalidateAsync(DepartmentProfile profile, CancellationToken cancellationToken)
		{
			var saved = await _profiles.SaveOrUpdateAsync(profile, cancellationToken, true);
			await InvalidateEmailBrandingAsync(profile.DepartmentId);
			return saved;
		}

		public async Task<DepartmentBranding> GetBrandingAsync(int departmentId)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var profile = await _profiles.GetByDepartmentIdAsync(departmentId);
			var media = (await _media.GetMetadataForDepartmentAsync(departmentId))?.Where(m => m != null).ToList() ?? new List<DepartmentProfileMedia>();

			media = await EnsureRenditionsAsync(departmentId, media);

			var name = !string.IsNullOrWhiteSpace(profile?.Name) ? profile.Name : department?.Name;
			return new DepartmentBranding
			{
				DepartmentId = departmentId,
				Profile = profile,
				DisplayName = name,
				ShortName = !string.IsNullOrWhiteSpace(profile?.ShortName) ? profile.ShortName : name,
				Code = !string.IsNullOrWhiteSpace(profile?.Code) ? profile.Code : department?.Code,
				AddressText = department?.Address?.FormatAddress(),
				PhoneNumber = profile?.PhoneNumber,
				Website = profile?.Website,
				UseDepartmentBrandingInEmails = profile?.UseDepartmentBrandingInEmails ?? false,
				MediaKey = media.FirstOrDefault()?.MediaKey,
				Media = media
			};
		}

		public async Task<DepartmentBranding> UploadLogoAsync(int departmentId, string userId, string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default)
		{
			var renditions = DepartmentLogoRenditions.Build(data);
			var existing = (await _media.GetMetadataForDepartmentAsync(departmentId))?.FirstOrDefault(m => m != null);
			var mediaKey = existing?.MediaKey ?? NewMediaKey();

			await InTransactionAsync(async () =>
			{
				await _media.DeleteForDepartmentAsync(departmentId, cancellationToken);
				foreach (var rendition in renditions)
					await _media.InsertAsync(ToRow(departmentId, userId, mediaKey, rendition), cancellationToken, true);
			});

			// Plan section 4.10.1: email branding "defaults on when a logo exists". Only the first logo flips it,
			// so an admin who turned it off keeps that choice when they replace the artwork.
			if (existing == null)
			{
				var profile = await GetOrCreateProfileAsync(departmentId, cancellationToken);
				if (!profile.UseDepartmentBrandingInEmails)
				{
					profile.UseDepartmentBrandingInEmails = true;
					await SaveProfileAsync(profile, cancellationToken);
				}
			}

			await InvalidateEmailBrandingAsync(departmentId);
			return await GetBrandingAsync(departmentId);
		}

		public async Task<bool> RemoveLogoAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var removed = await _media.DeleteForDepartmentAsync(departmentId, cancellationToken);
			await InvalidateEmailBrandingAsync(departmentId);
			return removed > 0;
		}

		public async Task<string> RegenerateMediaKeyAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var key = NewMediaKey();
			await _media.UpdateMediaKeyAsync(departmentId, key, cancellationToken);
			await InvalidateEmailBrandingAsync(departmentId);
			return key;
		}

		public async Task<DepartmentEmailBranding> GetEmailBrandingAsync(int departmentId)
		{
			async Task<DepartmentEmailBranding> build()
			{
				var branding = await GetBrandingAsync(departmentId);
				var result = new DepartmentEmailBranding
				{
					DepartmentId = departmentId,
					DisplayName = branding.DisplayName,
					Website = DepartmentEmailBranding.NormalizeWebsite(branding.Website)
				};

				// Both conditions from the plan: a logo uploaded (its masthead rendition exists) and the opt-in.
				var masthead = branding.Rendition(DepartmentProfileMediaKind.EmailMasthead);
				if (branding.UseDepartmentBrandingInEmails && masthead != null && !string.IsNullOrWhiteSpace(masthead.MediaKey))
				{
					result.Enabled = true;
					result.LogoUrl = PublicMastheadUrl(masthead.MediaKey);
				}

				return result;
			}

			try
			{
				if (Config.SystemBehaviorConfig.CacheEnabled)
					return await _cacheProvider.RetrieveAsync(string.Format(EmailBrandingCacheKey, departmentId), build, EmailBrandingCacheLength);

				return await build();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Email branding lookup failed for department {departmentId}; sending with Resgrid chrome.");
				return DepartmentEmailBranding.Disabled(departmentId);
			}
		}

		/// <summary>The anonymous EmailMasthead URL that goes into emails; the key is the only credential.</summary>
		public static string PublicMastheadUrl(string mediaKey)
		{
			return $"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Department/PublicMasthead?key={mediaKey}";
		}

		private async Task InvalidateEmailBrandingAsync(int departmentId)
		{
			if (!Config.SystemBehaviorConfig.CacheEnabled)
				return;

			try
			{
				await _cacheProvider.RemoveAsync(string.Format(EmailBrandingCacheKey, departmentId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not invalidate cached email branding for department {departmentId}.");
			}
		}

		public Task<DepartmentProfileMedia> GetMediaAsync(int departmentId, DepartmentProfileMediaKind kind)
		{
			return _media.GetAsync(departmentId, (int)kind);
		}

		public Task<DepartmentProfileMedia> GetPublicMastheadAsync(string mediaKey)
		{
			if (string.IsNullOrWhiteSpace(mediaKey) || mediaKey.Length < 16)
				return Task.FromResult<DepartmentProfileMedia>(null);

			return _media.GetByMediaKeyAsync(mediaKey.Trim(), (int)DepartmentProfileMediaKind.EmailMasthead);
		}

		/// <summary>A legacy PrimaryLogo without renditions (migration M0172) gets them generated once; undecodable bytes stay unserved.</summary>
		private async Task<List<DepartmentProfileMedia>> EnsureRenditionsAsync(int departmentId, List<DepartmentProfileMedia> media)
		{
			var primary = media.FirstOrDefault(m => m.Kind == (int)DepartmentProfileMediaKind.PrimaryLogo);
			if (primary == null || media.Any(m => m.Kind == (int)DepartmentProfileMediaKind.PrintHeader))
				return media;

			try
			{
				var full = await _media.GetAsync(departmentId, (int)DepartmentProfileMediaKind.PrimaryLogo);
				if (full?.Data == null || full.Data.Length == 0)
					return media;

				var renditions = DepartmentLogoRenditions.Build(full.Data);
				var mediaKey = full.MediaKey ?? NewMediaKey();
				await InTransactionAsync(async () =>
				{
					await _media.DeleteForDepartmentAsync(departmentId, CancellationToken.None);
					foreach (var rendition in renditions)
						await _media.InsertAsync(ToRow(departmentId, full.UploadedByUserId, mediaKey, rendition), CancellationToken.None, true);
				});

				return (await _media.GetMetadataForDepartmentAsync(departmentId))?.Where(m => m != null).ToList() ?? new List<DepartmentProfileMedia>();
			}
			catch (DepartmentLogoRejectedException ex)
			{
				Logging.LogError($"Legacy department logo for department {departmentId} could not be converted and is not served: {ex.Message}");
				return media.Where(m => m.Kind != (int)DepartmentProfileMediaKind.PrimaryLogo).ToList();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Rendition generation failed for department {departmentId}.");
				return media;
			}
		}

		private static DepartmentProfileMedia ToRow(int departmentId, string userId, string mediaKey, LogoRendition rendition)
		{
			var now = DateTime.UtcNow;
			return new DepartmentProfileMedia
			{
				DepartmentProfileMediaId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				Kind = (int)rendition.Kind,
				ContentType = rendition.ContentType,
				Width = rendition.Width,
				Height = rendition.Height,
				ByteSize = rendition.Data.LongLength,
				Checksum = Checksum(rendition.Data),
				Data = rendition.Data,
				UploadedByUserId = userId,
				UploadedOn = now,
				MediaKey = mediaKey,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};
		}

		public static string NewMediaKey()
		{
			var bytes = new byte[24];
			using (var rng = RandomNumberGenerator.Create())
				rng.GetBytes(bytes);
			return Convert.ToHexString(bytes).ToLowerInvariant();
		}

		private static string Checksum(byte[] data)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
		}

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await work();
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}
	}
}
