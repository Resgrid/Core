using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class TypeIconsService : ITypeIconsService
	{
		// IconKeys are Guid "N" strings; anything else is refused before it reaches the database.
		private static readonly Regex IconKeyFormat = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private readonly ITypeIconsRepository _typeIconsRepository;

		public TypeIconsService(ITypeIconsRepository typeIconsRepository)
		{
			_typeIconsRepository = typeIconsRepository;
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

			var rendition = TypeIconRenditions.Build(data);

			var icon = new TypeIcon
			{
				TypeIconId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				TypeKind = (int)kind,
				TypeId = typeId,
				IconKey = Guid.NewGuid().ToString("N"),
				ContentType = rendition.ContentType,
				Width = rendition.Width,
				Height = rendition.Height,
				ByteSize = rendition.Data.Length,
				Checksum = Checksum(rendition.Data),
				Data = rendition.Data,
				UploadedByUserId = userId,
				UploadedOn = DateTime.UtcNow
			};

			// New row first, then point the type at it (the update is scoped to the department, so it also proves
			// ownership), then drop the replaced rows: a failure part way leaves the type on a working icon, old or new.
			await _typeIconsRepository.InsertAsync(icon, cancellationToken);

			if (await _typeIconsRepository.SetTypeIconKeyAsync(departmentId, kind, typeId, icon.IconKey, cancellationToken) == 0)
			{
				await _typeIconsRepository.DeleteForTypeAsync(departmentId, kind, typeId, null, cancellationToken);
				return null;
			}

			await _typeIconsRepository.DeleteForTypeAsync(departmentId, kind, typeId, icon.IconKey, cancellationToken);

			return icon.IconKey;
		}

		public async Task<bool> RemoveIconAsync(int departmentId, TypeIconKinds kind, int typeId, CancellationToken cancellationToken = default)
		{
			if (!Enum.IsDefined(typeof(TypeIconKinds), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));

			if (await _typeIconsRepository.SetTypeIconKeyAsync(departmentId, kind, typeId, null, cancellationToken) == 0)
				return false;

			await _typeIconsRepository.DeleteForTypeAsync(departmentId, kind, typeId, null, cancellationToken);
			return true;
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
