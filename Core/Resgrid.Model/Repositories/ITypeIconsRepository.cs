using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Uploaded unit, call and POI type icons (TypeIcons, M0285).</summary>
	public interface ITypeIconsRepository : IRepository<TypeIcon>
	{
		Task<TypeIcon> GetByIconKeyAsync(string iconKey);

		/// <summary>Deletes the type's icon rows, except the one under <paramref name="keepIconKey"/> when given.</summary>
		Task<int> DeleteForTypeAsync(int departmentId, TypeIconKinds kind, int typeId, string keepIconKey = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Points the department's unit, call or POI type at <paramref name="iconKey"/> (null clears it) with a targeted
		/// update, so no other column of the type is rewritten. Returns the rows changed: 0 when the type is not the department's.
		/// </summary>
		Task<int> SetTypeIconKeyAsync(int departmentId, TypeIconKinds kind, int typeId, string iconKey, CancellationToken cancellationToken = default);
	}
}
