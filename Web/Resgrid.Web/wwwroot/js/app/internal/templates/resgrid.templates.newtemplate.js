
var resgrid;
(function (resgrid) {
	var templates;
	(function (templates) {
		var newtemplate;
		(function (newtemplate) {
			$(document).ready(function () {
				resgrid.common.analytics.track('Templates - New');

				let editorContainer = document.querySelector('#editor-container2');
				if (!editorContainer)
					return;

				let quillDescription = new Quill(editorContainer, {
					placeholder: '',
					theme: 'snow'
				});

				$(document).on('submit', '#newTemplateForm', function () {
					$('#Template_CallNature').val(quillDescription.root.innerHTML);

					return true;
				});


			});
		})(newtemplate = templates.newtemplate || (templates.newtemplate = {}));
	})(templates = resgrid.templates || (resgrid.templates = {}));
})(resgrid || (resgrid = {}));
