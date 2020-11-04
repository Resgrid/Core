using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Resgrid.Framework
{
	public static class ReflectionHelpers
	{
		public static void SetProperty(string compoundProperty, object target, object value)
		{
			string[] bits = compoundProperty.Split('.');
			for (int i = 0; i < bits.Length - 1; i++)
			{
				PropertyInfo propertyToGet = target.GetType().GetProperty(bits[i]);
				target = propertyToGet.GetValue(target, null);
			}
			PropertyInfo propertyToSet = target.GetType().GetProperty(bits.Last());
			propertyToSet.SetValue(target, value, null);
		}

		public static async Task<object> InvokeAsync(this MethodInfo @this, object obj, params object[] parameters)
		{
			dynamic awaitable = @this.Invoke(obj, parameters);
			await awaitable;
			return awaitable.GetAwaiter().GetResult();
		}
	}
}
