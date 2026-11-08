using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department-uploaded unit, call and POI type icons (M0285), personnel role icons (M0286), the status and priority
	/// icons of unit types, call types and roles (M0289) and station icons (TypeIcons only, no migration).
	/// </summary>
	public interface ITypeIconsService
	{
		/// <summary>Why <paramref name="data"/> cannot be a type icon; <see cref="TypeIconRejections.None"/> when it can.</summary>
		TypeIconRejections ValidateIcon(byte[] data);

		/// <summary>
		/// Re-encodes <paramref name="data"/> to the stored icon and makes it the type's icon under a new IconKey, replacing
		/// any earlier upload (of uploads at once, the newest is kept). Throws a user-facing rejection when the file is not a
		/// usable image. Returns the new IconKey, or null when the type is not this department's. For
		/// <see cref="TypeIconKinds.Station"/>, <paramref name="typeId"/> is a station group of the department or
		/// <see cref="TypeIcons.DepartmentStationTypeId"/> for the department's icon for every station.
		/// </summary>
		Task<string> UploadIconAsync(int departmentId, TypeIconKinds kind, int typeId, string userId, byte[] data, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes the uploaded icon; the type falls back to its built-in icon. Its status and priority icons stay. False when
		/// the type is not this department's. A station icon is removed by department, kind and id alone (so a deleted
		/// group's icon can still go), and the result is always true.
		/// </summary>
		Task<bool> RemoveIconAsync(int departmentId, TypeIconKinds kind, int typeId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The department's station icons (no bytes): one per station group with an icon of its own, plus TypeId
		/// <see cref="TypeIcons.DepartmentStationTypeId"/> for every other station when the department uploaded one. Cached
		/// until one is uploaded or removed.
		/// </summary>
		Task<List<TypeIconVariant>> GetStationIconsAsync(int departmentId, bool bypassCache = false);

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
