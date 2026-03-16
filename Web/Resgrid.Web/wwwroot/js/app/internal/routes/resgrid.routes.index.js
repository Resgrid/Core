$(document).ready(function () {
    $('#routePlansTable').DataTable({
        order: [[4, 'desc']],
        pageLength: 25
    });
});
