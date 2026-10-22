using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Binds the <see cref="CancellationToken"/> an action takes. A read gets the request-aborted token, as MVC's own binder
	/// gives it, so a closed page stops work nobody will see. A mutating request (POST/PUT/PATCH/DELETE) gets a token that
	/// never fires: its writes go out in several steps with no transaction around them (a call's row, then its dispatches,
	/// contacts and unit statuses), and a browser that leaves partway (a double-clicked button, a closed tab, a dropped
	/// connection) would otherwise leave the record half saved -- a call closed with its units never released, which no
	/// retry can release because the call is no longer active. Opt back in with <see cref="CancelOnClientDisconnectAttribute"/>.
	/// </summary>
	public sealed class MutationCancellationTokenBinder : IModelBinder
	{
		public Task BindModelAsync(ModelBindingContext bindingContext)
		{
			var token = RunsToCompletion(bindingContext.ActionContext)
				? CancellationToken.None
				: bindingContext.HttpContext.RequestAborted;

			// Boxed once so the validation state and the result hold the same reference, as MVC's own binder does.
			var model = (object)token;
			bindingContext.ValidationState.Add(model, new ValidationStateEntry { SuppressValidation = true });
			bindingContext.Result = ModelBindingResult.Success(model);

			return Task.CompletedTask;
		}

		private static bool RunsToCompletion(ActionContext context)
		{
			var method = context.HttpContext.Request.Method;
			var isMutation = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
							 HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

			return isMutation && !HasCancelAttribute(context);
		}

		private static bool HasCancelAttribute(ActionContext context)
		{
			// This app runs legacy MVC routing (EnableEndpointRouting = false), where EndpointMetadata is not reliably
			// populated -- read the attributes off the action and controller directly.
			if (context.ActionDescriptor is ControllerActionDescriptor descriptor)
			{
				return descriptor.MethodInfo.GetCustomAttributes(typeof(CancelOnClientDisconnectAttribute), inherit: true).Any() ||
					   descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(CancelOnClientDisconnectAttribute), inherit: true).Any();
			}

			return false;
		}
	}

	/// <summary>
	/// Puts <see cref="MutationCancellationTokenBinder"/> ahead of MVC's own CancellationToken binder.
	/// </summary>
	public sealed class MutationCancellationTokenBinderProvider : IModelBinderProvider
	{
		private static readonly IModelBinder Binder = new MutationCancellationTokenBinder();

		public static void Register(MvcOptions options)
		{
			// First: the binder providers are asked in order, and MVC's CancellationTokenModelBinderProvider answers for
			// every CancellationToken parameter it is asked about.
			options.ModelBinderProviders.Insert(0, new MutationCancellationTokenBinderProvider());
		}

		public IModelBinder GetBinder(ModelBinderProviderContext context)
		{
			return context.Metadata.ModelType == typeof(CancellationToken) ? Binder : null;
		}
	}
}
