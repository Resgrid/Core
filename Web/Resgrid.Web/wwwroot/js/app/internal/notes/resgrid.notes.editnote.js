
var resgrid;
(function (resgrid) {
    var notes;
    (function (notes) {
        var editnote;
        (function (editnote) {
            $(document).ready(function () {
                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#editNoteForm', function () {
                    $('#Body').val(quill.root.innerHTML);

                    return true;
                });
            });
        })(editnote = notes.editnote || (notes.editnote = {}));
    })(notes = resgrid.notes || (resgrid.notes = {}));
})(resgrid || (resgrid = {}));
