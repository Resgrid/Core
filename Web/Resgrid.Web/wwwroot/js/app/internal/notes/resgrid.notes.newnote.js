
var resgrid;
(function (resgrid) {
    var notes;
    (function (notes) {
        var newnote;
        (function (newnote) {
            $(document).ready(function () {
                let quill = new Quill('#editor-container', {
                    placeholder: '',
                    theme: 'snow'
                });

                $(document).on('submit', '#newNoteForm', function () {
                    $('#Body').val(quill.root.innerHTML);

                    return true;
                });
            });
        })(newnote = notes.newnote || (notes.newnote = {}));
    })(notes = resgrid.notes || (resgrid.notes = {}));
})(resgrid || (resgrid = {}));
