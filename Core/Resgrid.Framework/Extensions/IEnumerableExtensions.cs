using System.Collections.Generic;

namespace Resgrid
{
	public static class IEnumerableExtensions
	{
		public static ICollection<T> ToCollection<T>(this IEnumerable<T> items)
		{
			var col = new System.Collections.ObjectModel.Collection<T>();
			if (items == null)
			{
				return col;
			}

			foreach (var item in items)
			{
				col.Add(item);
			}
			return col;
		}
	}
}
