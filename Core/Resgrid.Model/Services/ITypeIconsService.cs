using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department-uploaded unit, call and POI type icons (M0285), personnel role icons (M0286) and the status and priority
	/// icons of unit types, call types and roles (M0289).
	/// </summary>
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

		/// <summary>
		/// Removes the uploaded icon; the type falls back to its built-in icon. Its status and priority icons stay. False when
		/// the type is not this department's.
		/// </summary>
		Task<bool> RemoveIconAsync(int departmentId, TypeIconKinds kind, int typeId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Makes <paramref name="data"/> the icon the type shows on the map while its call has the priority, or its unit or
		/// member the status, named by <paramref name="variant"/> (<see cref="TypeIcons.IsValidVariant"/>), replacing any
		/// earlier upload for it. Throws a user-facing rejection when the file is not a usable image. Returns the new IconKey,
		/// or null when the type is not this department's or cannot have that slot.
		/// </summary>
		Task<string> UploadVariantIconAsync(int departmentId, TypeIconKinds kind, int typeId, string variant, string userId, byte[] data, CancellationToken cancellationToken = default);

		/// <summary>Removes the type's icon for one status or priority. False when the type is not this department's or cannot have that slot.</summary>
		Task<bool> RemoveVariantIconAsync(int departmentId, TypeIconKinds kind, int typeId, string variant, CancellationToken cancellationToken = default);

		/// <summary>Every status and priority icon of the department (no bytes), cached until one is uploaded or removed.</summary>
		Task<List<TypeIconVariant>> GetVariantsForDepartmentAsync(int departmentId, bool bypassCache = false);

		/// <summary>The stored icon for an IconKey, or null for a malformed or unknown key.</summary>
		Task<TypeIcon> GetIconByKeyAsync(string iconKey);
	}
}
