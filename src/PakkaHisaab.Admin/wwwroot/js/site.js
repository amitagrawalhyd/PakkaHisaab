(function () {
    var sidebar = document.getElementById('phSidebar');
    var backdrop = document.getElementById('phSidebarBackdrop');
    var toggle = document.getElementById('phSidebarToggle');
    if (!sidebar || !toggle) return;

    function close() {
        sidebar.classList.remove('show');
        backdrop.classList.remove('show');
    }

    toggle.addEventListener('click', function () {
        sidebar.classList.toggle('show');
        backdrop.classList.toggle('show');
    });
    backdrop.addEventListener('click', close);
})();

// Shared confirmation modal (see Pages/Shared/_ConfirmModal.cshtml).
document.addEventListener('click', function (e) {
    var btn = e.target.closest('[data-confirm-action]');
    if (!btn) return;
    var modalEl = document.getElementById('phConfirmModal');
    if (!modalEl || typeof bootstrap === 'undefined') return;

    document.getElementById('phConfirmForm').action = btn.getAttribute('data-confirm-action');
    document.getElementById('phConfirmTitle').textContent = btn.getAttribute('data-confirm-title') || 'Confirm';
    document.getElementById('phConfirmBody').textContent = btn.getAttribute('data-confirm-body') || 'Are you sure?';
    var submitBtn = document.getElementById('phConfirmSubmit');
    submitBtn.textContent = btn.getAttribute('data-confirm-button') || 'Confirm';
    submitBtn.className = 'btn ' + (btn.getAttribute('data-confirm-class') || 'btn-danger');

    new bootstrap.Modal(modalEl).show();
});
