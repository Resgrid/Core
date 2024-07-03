
var resgrid;
(function (resgrid) {
    var documents;
    (function (documents) {
        var newdocument;
        (function (newdocument) {
            $(document).ready(function () {
                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newDocumentForm', function () {
                    $('#Document_Description').val(quill.root.innerHTML);

                    return true;
                });


                $("#fileToUpload").kendoUpload({
                    multiple: false,
                    localization: {
                        select: "Select File"
                    }
                });
            });
        })(newdocument = documents.newdocument || (documents.newdocument = {}));
    })(documents = resgrid.documents || (resgrid.documents = {}));
})(resgrid || (resgrid = {}));
