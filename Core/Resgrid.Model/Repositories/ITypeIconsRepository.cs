using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Uploaded unit, call and POI type icons (TypeIcons, M0285).</summary>
	public interface ITypeIconsRepository : IRepository<TypeIcon>
	{
		Task<TypeIcon> GetByIconKeyAsync(string iconKey);

		/// <summary>
		/// Deletes the type's icon rows, its status and priority icons included, except the one under
		/// <paramref name="keepIconKey"/> when given. The type deletes use it.
		/// </summary>
		Task<int> DeleteForTypeAsync(int departmentId, TypeIconKinds kind, int typeId, string keepIconKey = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes the rows of one icon slot of the type: its own icon when <paramref name="variant"/> is null, else the icon
		/// of that status or priority; except the one under <paramref name="keepIconKey"/> when given.
		/// </summary>
		Task<int> DeleteSlotAsync(int departmentId, TypeIconKinds kind, int typeId, string variant, string keepIconKey = null, CancellationToken cancellationToken = default);

		/// <summary>Every status and priority icon of the department, without bytes.</summary>
		Task<List<TypeIconVariant>> GetVariantsForDepartmentAsync(int departmentId);

		/// <summary>True when the unit type, call type, POI type or role is the department's.</summary>
		Task<bool> TypeBelongsToDepartmentAsync(int departmentId, TypeIconKinds kind, int typeId);

		/// <summary>
		/// Points the department's unit, call or POI type at <paramref name="iconKey"/> (null clears it) with a targeted
		/// update, so no other column of the type is rewritten. Returns the rows changed: 0 when the type is not the department's.
		/// </summary>
		Task<int> SetTypeIconKeyAsync(int departmentId, TypeIconKinds kind, int typeId, string iconKey, CancellationToken cancellationToken = default);
	}
}
