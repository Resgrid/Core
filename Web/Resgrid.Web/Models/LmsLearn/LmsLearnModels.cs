using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model.Lms;
using Resgrid.WebCore.Models;

namespace Resgrid.Web.Models.LmsLearn
{
	// The learner portal's page models (Training T5 item 2). Lms-prefixed: they are LMS layer types (LmsRulesTests separation guard).

	public class LmsLearnCatalogModel
	{
		public LmsLearnerCatalog Catalog { get; set; } = new LmsLearnerCatalog();
		public LmsExternalLearner Learner { get; set; }
	}

	public class LmsLearnSignInModel
	{
		[Required]
		[EmailAddress]
		[StringLength(LmsExternalLearner.EmailMaxLength)]
		public string Email { get; set; }

		[Required]
		[DataType(DataType.Password)]
		[StringLength(LmsExternalLearnerRules.PasswordMaxLength)]
		public string Password { get; set; }
	}

	/// <summary>
	/// Self-registration (and public class sign-up). Round 7B: the Account/Register reCAPTCHA standard (the same model base and validation
	/// attribute, the same widget and site key, the same behaviour when the keys are blank).
	/// </summary>
	public class LmsLearnRegisterModel : GoogleReCaptchaModelBase
	{
		public string SiteKey { get; set; }

		[Required]
		[EmailAddress]
		[StringLength(LmsExternalLearner.EmailMaxLength)]
		public string Email { get; set; }

		[Required]
		[StringLength(LmsExternalLearner.NameMaxLength)]
		public string FirstName { get; set; }

		[Required]
		[StringLength(LmsExternalLearner.NameMaxLength)]
		public string LastName { get; set; }

		[StringLength(LmsExternalLearner.OrganizationMaxLength)]
		public string Organization { get; set; }

		[Required]
		[DataType(DataType.Password)]
		[StringLength(LmsExternalLearnerRules.PasswordMaxLength, MinimumLength = LmsExternalLearnerRules.PasswordMinLength)]
		public string Password { get; set; }

		[Required]
		[DataType(DataType.Password)]
		public string ConfirmPassword { get; set; }

		public int? ClassId { get; set; }

		public LmsLearnerClassItem ClassItem { get; set; }
	}

	public class LmsLearnPasswordModel
	{
		[Required]
		public string Token { get; set; }

		public string Email { get; set; }

		public bool Invite { get; set; }

		[Required]
		[DataType(DataType.Password)]
		[StringLength(LmsExternalLearnerRules.PasswordMaxLength, MinimumLength = LmsExternalLearnerRules.PasswordMinLength)]
		public string Password { get; set; }

		[Required]
		[DataType(DataType.Password)]
		public string ConfirmPassword { get; set; }
	}

	/// <summary>The password-reset request (it e-mails a link): round 7B, behind the reCAPTCHA standard.</summary>
	public class LmsLearnForgotModel : GoogleReCaptchaModelBase
	{
		[Required]
		[EmailAddress]
		[StringLength(LmsExternalLearner.EmailMaxLength)]
		public string Email { get; set; }

		public string SiteKey { get; set; }
	}

	/// <summary>Round 7B: the verification or invitation e-mail sent again (it e-mails a link), behind the reCAPTCHA standard.</summary>
	public class LmsLearnResendModel : GoogleReCaptchaModelBase
	{
		[Required]
		[EmailAddress]
		[StringLength(LmsExternalLearner.EmailMaxLength)]
		public string Email { get; set; }

		public string SiteKey { get; set; }
	}

	/// <summary>Round 7B: the class-reminder unsubscribe page (the link's key is the only proof; the page posts it to confirm).</summary>
	public class LmsLearnUnsubscribeModel
	{
		public string Key { get; set; }
		public bool Done { get; set; }
		public string Error { get; set; }
	}

	/// <summary>
	/// Security review M2: the e-mail verification link's Confirm page (the GET shows it; the POST activates). Follow-up (M1, attacker-first
	/// order): the person who opened the link chooses the password here and confirms the name; the registration's password is not kept.
	/// </summary>
	public class LmsLearnVerifyModel
	{
		[Required]
		public string Token { get; set; }

		/// <summary>When the registration was made (shown so someone who did not register can tell).</summary>
		public System.DateTime RegisteredOn { get; set; }

		[Required]
		[StringLength(LmsExternalLearner.NameMaxLength)]
		public string FirstName { get; set; }

		[Required]
		[StringLength(LmsExternalLearner.NameMaxLength)]
		public string LastName { get; set; }

		[Required]
		[DataType(DataType.Password)]
		[StringLength(LmsExternalLearnerRules.PasswordMaxLength, MinimumLength = LmsExternalLearnerRules.PasswordMinLength)]
		public string Password { get; set; }

		[Required]
		[DataType(DataType.Password)]
		public string ConfirmPassword { get; set; }

		/// <summary>The registration's name as stored (the page shows it with the date); read again from the token, never posted.</summary>
		public string RegisteredName { get; set; }
	}

	public class LmsLearnMessageModel
	{
		public string Title { get; set; }
		public string Message { get; set; }
		public string Error { get; set; }
		public bool ShowSignIn { get; set; }
	}

	public class LmsLearnMyModel
	{
		public LmsExternalLearner Learner { get; set; }
		public LmsLearnerMyLearning Mine { get; set; } = new LmsLearnerMyLearning();
		/// <summary>Round 7B: the languages the learner can pick for e-mails (two-letter codes).</summary>
		public List<string> Languages { get; set; } = new List<string>();
	}

	public class LmsLearnPlayerModel
	{
		public LmsPlayerView Player { get; set; }
		public string Error { get; set; }
		public List<LmsSurveyQuestion> SurveyQuestions { get; set; } = new List<LmsSurveyQuestion>();
	}

	public class LmsLearnTestModel
	{
		public LmsAttemptView Attempt { get; set; }
	}

	public class LmsLearnTestResultModel
	{
		public LmsAttemptResultView Result { get; set; }
		public string Error { get; set; }
	}
}
