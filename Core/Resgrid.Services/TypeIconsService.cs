using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class TypeIconsService : ITypeIconsService
	{
		// IconKeys are Guid "N" strings; anything else is refused before it reaches the database.
		private static readonly Regex IconKeyFormat = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private static string VariantsCacheKey = "TypeIconVariants_{0}";
		private static TimeSpan VariantsCacheLength = TimeSpan.FromDays(1);

		private readonly ITypeIconsRepository _typeIconsRepository;
		private readonly ICacheProvider _cacheProvider;

		public TypeIconsService(ITypeIconsRepository typeIconsRepository, ICacheProvider cacheProvider)
		{
			_typeIconsRepository = typeIconsRepository;
			_cacheProvider = cacheProvider;
		}

		public TypeIconRejections ValidateIcon(byte[] data)
		{
			try
			{
				TypeIconRenditions.Build(data);
				return TypeIconRejections.None;
			}
			catch (TypeIconRejectedException ex)
			{
				return ex.Reason;
			}
		}

		public async Task<string> UploadIconAsync(int departmentId, TypeIconKinds kind, int typeId, string userId, byte[] data, CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(TypeIconKinds), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));

			var icon = BuildIcon(departmentId, kind, typeId, null, userId, TypeIconRenditions.Build(data));

			// New row first, then point the type at the newest icon of its slot (the update is scoped to the department, so it
			// also proves ownership), then drop the older rows: a failure part way leaves the type on a working icon, old or new.
			// The newest, not "this one": two uploads at once would each delete the other's row and leave the type on a key
			// that no longer exists, while each step here keeps the newest row, so whichever runs last leaves the type on it.
			// Only the type's own icon slot is touched; its status and priority icons stay.
			await _typeIconsRepository.InsertAsync(icon, cancellationToken);

			if (await _typeIconsRepository.SetTypeIconKeyToNewestAsync(departmentId, kind, typeId, cancellationToken) == 0)
			{
				await _typeIconsRepository.DeleteSlotAsync(departmentId, kind, typeId, null, null, cancellationToken);
				return null;
			}

			await _typeIconsRepository.DeleteAllButNewestInSlotAsync(departmentId, kind, typeId, null, cancellationToken);

			return icon.IconKey;
		}

		public async Task<bool> RemoveIconAsync(int departmentId, TypeIconKinds kind, int typeId, CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(TypeIconKinds), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));

			if (await _typeIconsRepository.SetTypeIconKeyAsync(departmentId, kind, typeId, null, cancellationToken) == 0)
				return false;

			await _typeIconsRepository.DeleteSlotAsync(departmentId, kind, typeId, null, null, cancellationToken);
			return true;
		}

		public async Task<string> UploadVariantIconAsync(int departmentId, TypeIconKinds kind, int typeId, string variant, string userId, byte[] data, CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(TypeIconKinds), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));

			if (!TypeIcons.IsValidVariant(kind, variant))
				return null;

			var rendition = TypeIconRenditions.Build(data);

			// Nothing on the type points at a status or priority icon, so ownership is proven up front.
			if (!await _typeIconsRepository.TypeBelongsToDepartmentAsync(departmentId, kind, typeId))
				return null;

			var icon = BuildIcon(departmentId, kind, typeId, variant, userId, rendition);

			// The slot keeps its newest upload rather than "this one": two uploads at once would each delete the other's row.
			await _typeIconsRepository.InsertAsync(icon, cancellationToken);
			await _typeIconsRepository.DeleteAllButNewestInSlotAsync(departmentId, kind, typeId, variant, cancellationToken);
			await InvalidateVariantsAsync(departmentId);

			return icon.IconKey;
		}

		public async Task<bool> RemoveVariantIconAsync(int departmentId, TypeIconKinds kind, int typeId, string variant, CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(TypeIconKinds), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));

			if (!TypeIcons.IsValidVariant(kind, variant) || !await _typeIconsRepository.TypeBelongsToDepartmentAsync(departmentId, kind, typeId))
				return false;

			await _typeIconsRepository.DeleteSlotAsync(departmentId, kind, typeId, variant, null, cancellationToken);
			await InvalidateVariantsAsync(departmentId);

			return true;
		}

		public async Task<List<TypeIconVariant>> GetVariantsForDepartmentAsync(int departmentId, bool bypassCache = false)
		{
			async Task<List<TypeIconVariant>> getVariants()
			{
				return await _typeIconsRepository.GetVariantsForDepartmentAsync(departmentId) ?? new List<TypeIconVariant>();
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync(string.Format(VariantsCacheKey, departmentId), getVariants, VariantsCacheLength) ?? new List<TypeIconVariant>();

			return await getVariants();
		}

		private async Task InvalidateVariantsAsync(int departmentId)
		{
			await _cacheProvider.RemoveAsync(string.Format(VariantsCacheKey, departmentId));
		}

		private static TypeIcon BuildIcon(int departmentId, TypeIconKinds kind, int typeId, string variant, string userId, TypeIconRendition rendition)
		{
			return new TypeIcon
			{
				TypeIconId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				TypeKind = (int)kind,
				TypeId = typeId,
				IconKey = Guid.NewGuid().ToString("N"),
				Variant = variant,
				ContentType = rendition.ContentType,
				Width = rendition.Width,
				Height = rendition.Height,
				ByteSize = rendition.Data.Length,
				Checksum = Checksum(rendition.Data),
				Data = rendition.Data,
				UploadedByUserId = userId,
				UploadedOn = DateTime.UtcNow
			};
		}

		public async Task<TypeIcon> GetIconByKeyAsync(string iconKey)
		{
			if (string.IsNullOrEmpty(iconKey) || !IconKeyFormat.IsMatch(iconKey))
				return null;

			return await _typeIconsRepository.GetByIconKeyAsync(iconKey);
		}

		private static string Checksum(byte[] data)
		{
			using var sha = SHA256.Create();
			return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
		}
	}
}
