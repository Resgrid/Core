using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>Department-uploaded unit, call and POI type icons (M0285) and personnel role icons (M0286).</summary>
	public interface ITypeIconsService
	{
		/// <summary>Why <paramref name="data"/> cannot be a type icon; <see cref="TypeIconRejections.None"/> when it can.</summary>
		TypeIconRejections ValidateIcon(byte[] data);

		/// <summary>
		/// Re-encodes <paramref name="data"/> to the stored icon and makes it the type's icon under a new IconKey, replacing
		/// any earlier upload. Throws a user-facing rejection when the file is not a usable image. Returns the new IconKey,
		/// or null when the type is not this department's.
		/// </summary>
		Task<string> UploadIconAsync(int departmentId, TypeIconKinds kind, int typeId, string userId, byte[] data, CancellationToken cancellationToken = default);

		/// <summary>Removes the uploaded icon; the type falls back to its built-in icon. False when the type is not this department's.</summary>
		Task<bool> RemoveIconAsync(int departmentId, TypeIconKinds kind, int typeId, CancellationToken cancellationToken = default);

		/// <summary>The stored icon for an IconKey, or null for a malformed or unknown key.</summary>
		Task<TypeIcon> GetIconByKeyAsync(string iconKey);
	}
}
