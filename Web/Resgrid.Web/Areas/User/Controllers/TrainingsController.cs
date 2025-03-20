using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Training;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class TrainingsController : SecureBaseController
	{
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ITrainingService _trainingService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public TrainingsController(IDepartmentGroupsService departmentGroupsService, IDepartmentsService departmentService, ITrainingService trainingService, IPersonnelRolesService personnelRolesService)
		{
			_departmentGroupsService = departmentGroupsService;
			_departmentsService = departmentService;
			_trainingService = trainingService;
			_personnelRolesService = personnelRolesService;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			var model = new TrainingIndexModel();
			model.Trainings = await _trainingService.GetAllTrainingsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		
		public async Task<IActionResult> New()
		{
			var model = new NewTrainingModel();
			model.Training = new Training();
			model.Training.MinimumScore = 0;
			model.Training.CreatedByUserId = UserId;

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> New(NewTrainingModel model, IFormCollection form, ICollection<IFormFile> attachments)
		{
			model.Training.CreatedByUserId = UserId;

			if (attachments != null)
			{
				model.Training.Attachments = new Collection<TrainingAttachment>();
				foreach (var file in attachments)
				{
					if (file != null && file.Length > 0)
					{
						var extenion = file.FileName.Substring(file.FileName.IndexOf(char.Parse(".")) + 1,
							file.FileName.Length - file.FileName.IndexOf(char.Parse(".")) - 1);

						if (!String.IsNullOrWhiteSpace(extenion))
							extenion = extenion.ToLower();

						if (extenion != "jpg" && extenion != "jpeg" && extenion != "png" && extenion != "gif" && extenion != "gif" &&
						    extenion != "pdf" && extenion != "doc"
						    && extenion != "docx" && extenion != "ppt" && extenion != "pptx" && extenion != "pps" && extenion != "ppsx" &&
						    extenion != "odt"
						    && extenion != "xls" && extenion != "xlsx" && extenion != "txt" && extenion != "mpg" && extenion != "avi" &&
						    extenion != "mpeg")
							ModelState.AddModelError("fileToUpload", string.Format("File type ({0}) is not importable.", extenion));

						if (file.Length > 30000000)
							ModelState.AddModelError("fileToUpload", "Attachment is too large, must be smaller then 30MB.");

						var attachment = new TrainingAttachment();
						attachment.FileType = file.ContentType;
						attachment.FileName = file.FileName;

						var uploadedFile = new byte[file.OpenReadStream().Length];
						file.OpenReadStream().Read(uploadedFile, 0, uploadedFile.Length);

						attachment.Data = uploadedFile;
						model.Training.Attachments.Add(attachment);
					}
				}
			}

			var roles = new List<string>();
			var groups = new List<string>();
			var users = new List<string>();

			if (form.ContainsKey("rolesToAdd"))
				roles.AddRange(form["rolesToAdd"].ToString().Split(char.Parse(",")));

			if (form.ContainsKey("groupsToAdd"))
				groups.AddRange(form["groupsToAdd"].ToString().Split(char.Parse(",")));

			if (form.ContainsKey("usersToAdd"))
				users.AddRange(form["usersToAdd"].ToString().Split(char.Parse(",")));

			model.Training.Users = new List<TrainingUser>();

			if (model.SendToAll)
			{
				var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
				foreach (var user in allUsers)
				{
					var trainingUser = new TrainingUser();
					trainingUser.UserId = user.UserId;

					model.Training.Users.Add(trainingUser);
				}
			}
			else
			{
				foreach (var user in users)
				{
					var trainingUser = new TrainingUser();
					trainingUser.UserId = user;

					model.Training.Users.Add(trainingUser);
				}

				foreach (var group in groups)
				{
					var members = await _departmentGroupsService.GetAllMembersForGroupAsync(int.Parse(group));

					foreach (var member in members)
					{
						var trainingUser = new TrainingUser();
						trainingUser.UserId = member.UserId;

						if (model.Training.Users.All(x => x.UserId != member.UserId))
							model.Training.Users.Add(trainingUser);
					}
				}

				foreach (var role in roles)
				{
					var roleMembers = await _personnelRolesService.GetAllMembersOfRoleAsync(int.Parse(role));

					foreach (var member in roleMembers)
					{
						var trainingUser = new TrainingUser();
						trainingUser.UserId = member.UserId;

						if (model.Training.Users.All(x => x.UserId != member.UserId))
							model.Training.Users.Add(trainingUser);
					}
				}
			}

			if (!model.Training.Users.Any())
				ModelState.AddModelError("", "You have not selected any personnel, roles or groups to assign this training to.");

			if (ModelState.IsValid)
			{
				List<int> questions = (from object key in form.Keys where key.ToString().StartsWith("question_") select int.Parse(key.ToString().Replace("question_", ""))).ToList();

				if (questions.Count > 0)
					model.Training.Questions = new Collection<TrainingQuestion>();

				model.Training.DepartmentId = DepartmentId;
				model.Training.CreatedOn = DateTime.UtcNow;
				model.Training.CreatedByUserId = UserId;
				model.Training.GroupsToAdd = form["groupsToAdd"];
				model.Training.RolesToAdd = form["rolesToAdd"];
				model.Training.UsersToAdd = form["usersToAdd"];
				model.Training.Description = System.Net.WebUtility.HtmlDecode(model.Training.Description);
				model.Training.TrainingText = System.Net.WebUtility.HtmlDecode(model.Training.TrainingText);

				
				foreach (var i in questions)
				{
					if (form.ContainsKey("question_" + i))
					{
						var questionText = form["question_" + i];
						var question = new TrainingQuestion();
						question.Question = questionText;
						
						List<int> answers = (from object key in form.Keys where key.ToString().StartsWith("answerForQuestion_" + i + "_") select int.Parse(key.ToString().Replace("answerForQuestion_" + i + "_", ""))).ToList();

						if (answers.Count > 0)
							question.Answers = new Collection<TrainingQuestionAnswer>();

						foreach (var answer in answers)
						{
							var trainingQuestionAnswer = new TrainingQuestionAnswer();
							var answerForQuestion = form["answerForQuestion_" + i + "_" + answer];

							var possibleAnswer = form["answer_" + i];
							trainingQuestionAnswer.Answer = answerForQuestion;

							if (!string.IsNullOrWhiteSpace(possibleAnswer))
							{
								if ("answerForQuestion_" + i + "_" + answer == possibleAnswer)
									trainingQuestionAnswer.Correct = true;
							}

							question.Answers.Add(trainingQuestionAnswer);
						}

						model.Training.Questions.Add(question);
					}
				}

				await _trainingService.SaveAsync(model.Training);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> View(int trainingId)
		{
			var model = new ViewTrainingModel();
			model.Training = await _trainingService.GetTrainingByIdAsync(trainingId);
			model.CreatorUserName = await UserHelper.GetFullNameForUser(model.Training.CreatedByUserId);

			if (model.Training.DepartmentId != DepartmentId)
				Unauthorized();

			await _trainingService.SetTrainingAsViewedAsync(trainingId, UserId);

			return View(model);
		}

		[HttpGet]
		public async Task<FileResult> GetTrainingAttachment(int trainingAttachmentId)
		{
			var attachment = await _trainingService.GetTrainingAttachmentByIdAsync(trainingAttachmentId);

			return new FileContentResult(attachment.Data, attachment.FileType)
			{
				FileDownloadName = attachment.FileName
			};
		}

		[HttpGet]
		public async Task<IActionResult> Quiz(int trainingId, CancellationToken cancellationToken)
		{
			var model = new ViewTrainingModel();
			model.Training = await _trainingService.GetTrainingByIdAsync(trainingId);

			if (model.Training.DepartmentId != DepartmentId)
				Unauthorized();

			await _trainingService.SetTrainingAsViewedAsync(trainingId, UserId, cancellationToken);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Quiz(ViewTrainingModel model, IFormCollection form)
		{
			int correctAnswers = 0;
			var training = await _trainingService.GetTrainingByIdAsync(model.Training.TrainingId);
			
			List<int> questions = (from object key in form.Keys where key.ToString().StartsWith("question_") select int.Parse(key.ToString().Replace("question_", ""))).ToList();

			foreach (var questionId in questions)
			{
				var answerForQuestion = form["question_" + questionId].ToString();

				if (answerForQuestion != null)
				{
					var question = training.Questions.FirstOrDefault(x => x.TrainingQuestionId == questionId);
					if (question != null)
					{
						var answer = question.Answers.FirstOrDefault(x => x.TrainingQuestionAnswerId == int.Parse(answerForQuestion));
						if (answer != null && answer.Correct)
							correctAnswers++;
					}
				}
			}

			await _trainingService.RecordTrainingQuizResultAsync(training.TrainingId, UserId, correctAnswers);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> DeleteTraining(int trainingId)
		{
			var model = new ViewTrainingModel();
			model.Training = await _trainingService.GetTrainingByIdAsync(trainingId);

			if (model.Training.DepartmentId != DepartmentId)
				Unauthorized();

			await _trainingService.DeleteTrainingAsync(trainingId);

			return RedirectToAction("Index");
		}

		[HttpGet]
		public async Task<IActionResult> ResetUserTraining(int trainingId, string userId)
		{
			var model = new ViewTrainingModel();
			model.Training = await _trainingService.GetTrainingByIdAsync(trainingId);

			if (model.Training.DepartmentId != DepartmentId)
				Unauthorized();

			await _trainingService.ResetUserAsync(trainingId, userId);

			return RedirectToAction("Report", new { trainingId = model.Training.TrainingId});
		}

		[HttpGet]
		public async Task<IActionResult> Report(int trainingId)
		{
			var model = new TrainingReportView();
			model.Training = await _trainingService.GetTrainingByIdAsync(trainingId);
			model.UserGroups = new Dictionary<string, string>();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (model.Training.DepartmentId != DepartmentId)
				Unauthorized();

			foreach (var user in model.Training.Users)
			{
				var group = await _departmentGroupsService.GetGroupForUserAsync(user.UserId, DepartmentId);

				if (group != null)
					model.UserGroups.Add(user.UserId, group.Name);
			}

			return View(model);
		}
	}
}
