using System;
using System.Collections.Generic;
using System.Linq;

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

		public static T Random<T>(this IEnumerable<T> input)
		{
			return EnumerableHelper<T>.Random(input);
		}
	}

	public static class EnumerableHelper<E>
	{
		private static Random r;

		static EnumerableHelper()
		{
			r = new Random(DateTime.Now.Millisecond);
		}

		public static T Random<T>(IEnumerable<T> input)
		{
			return input.ElementAt(r.Next(input.Count()));
		}

	}
}
