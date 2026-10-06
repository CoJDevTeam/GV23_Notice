/* ═══════════════════════════════════════════════════════════════
   eNotice — shared modal + busy-button helpers
   Markup contract:
     <div class="en-modal" id="x" role="dialog" aria-modal="true">…</div>
     [data-en-open="x"]            opens modal #x
     [data-en-open-url="/path"]    loads a partial into #enModalHost, then opens it
     [data-en-close]               closes the modal it sits in
     form[data-en-busy]            disables its submit button on submit (stops double posts)
═══════════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    var lastFocus = null;

    function openModal(modal) {
        if (!modal) return;
        lastFocus = document.activeElement;
        modal.classList.add('open');
        document.body.classList.add('en-lock');
        var focusable = modal.querySelector('[data-en-autofocus], .en-modal-x, button, a[href]');
        if (focusable) setTimeout(function () { focusable.focus({ preventScroll: true }); }, 30);
    }

    function closeModal(modal) {
        if (!modal) return;
        modal.classList.remove('open');
        if (!document.querySelector('.en-modal.open')) document.body.classList.remove('en-lock');
        if (lastFocus && typeof lastFocus.focus === 'function') lastFocus.focus({ preventScroll: true });
    }

    function host() {
        var h = document.getElementById('enModalHost');
        if (!h) {
            h = document.createElement('div');
            h.id = 'enModalHost';
            document.body.appendChild(h);
        }
        return h;
    }

    function loadingModal() {
        return '<div class="en-modal open" role="dialog" aria-modal="true" aria-busy="true">' +
            '<div class="en-modal-dialog"><div class="en-modal-loading"><span class="en-spin"></span>' +
            '<div style="margin-top:10px">Loading…</div></div></div></div>';
    }

    function openUrl(url) {
        var h = host();
        h.innerHTML = loadingModal();
        document.body.classList.add('en-lock');

        return fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' })
            .then(function (r) {
                if (!r.ok) throw new Error('HTTP ' + r.status);
                return r.text();
            })
            .then(function (html) {
                h.innerHTML = html;
                var modal = h.querySelector('.en-modal');
                if (modal) {
                    modal.classList.remove('open');
                    // next frame so the open transition runs
                    requestAnimationFrame(function () { openModal(modal); });
                }
            })
            .catch(function () {
                h.innerHTML = '';
                document.body.classList.remove('en-lock');
                alertFallback('The summary could not be loaded. Please refresh the page and try again.');
            });
    }

    function alertFallback(msg) {
        var h = host();
        h.innerHTML =
            '<div class="en-modal" role="alertdialog" aria-modal="true"><div class="en-modal-dialog">' +
            '<div class="en-modal-head"><div><h2 class="en-modal-title">Something went wrong</h2></div>' +
            '<button type="button" class="en-modal-x" data-en-close aria-label="Close">&times;</button></div>' +
            '<div class="en-modal-body"></div>' +
            '<div class="en-modal-foot"><button type="button" class="en-btn en-btn-ghost" data-en-close>Close</button></div>' +
            '</div></div>';
        h.querySelector('.en-modal-body').textContent = msg;
        openModal(h.querySelector('.en-modal'));
    }

    document.addEventListener('click', function (e) {
        var opener = e.target.closest('[data-en-open]');
        if (opener) {
            e.preventDefault();
            openModal(document.getElementById(opener.getAttribute('data-en-open')));
            return;
        }

        var urlOpener = e.target.closest('[data-en-open-url]');
        if (urlOpener) {
            e.preventDefault();
            openUrl(urlOpener.getAttribute('data-en-open-url'));
            return;
        }

        var closer = e.target.closest('[data-en-close]');
        if (closer) {
            e.preventDefault();
            closeModal(closer.closest('.en-modal'));
            return;
        }

        // Click on the dark backdrop closes (not while a form is busy)
        if (e.target.classList && e.target.classList.contains('en-modal') && !e.target.hasAttribute('data-en-static')) {
            if (!e.target.querySelector('.is-busy')) closeModal(e.target);
        }
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        var open = document.querySelectorAll('.en-modal.open');
        if (open.length && !open[open.length - 1].querySelector('.is-busy'))
            closeModal(open[open.length - 1]);
    });

    // Prevent double submits (e.g. approving twice)
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form.matches('form[data-en-busy]')) return;
        if (form.dataset.submitted === '1') { e.preventDefault(); return; }
        form.dataset.submitted = '1';
        var btn = form.querySelector('[type="submit"]');
        if (btn) {
            btn.classList.add('is-busy');
            btn.setAttribute('disabled', 'disabled');
            var label = form.getAttribute('data-en-busy') || 'Please wait…';
            btn.innerHTML = '<span class="en-spin"></span> ' + label.replace(/</g, '&lt;');
        }
    }, true);

    /* ── Slide-out "Actions and Summary" panel (Print, Send Notices, Batch Summary)
          [data-en-panel="id"]   toggles panel #id
          [data-en-panel-close]  closes the panel it sits in
          panel[data-en-lift=".sel"][data-en-lift-to=".target"]
                                 moves matching messages out of the panel so they stay visible */
    function panelScrim(panel) {
        var scrim = panel.previousElementSibling;
        if (!scrim || !scrim.classList.contains('en-panel-scrim')) {
            scrim = document.createElement('div');
            scrim.className = 'en-panel-scrim';
            scrim.setAttribute('aria-hidden', 'true');
            // same stacking context as the panel, so it sits just under it
            panel.parentNode.insertBefore(scrim, panel);
            scrim.addEventListener('click', function () { closePanel(panel); });
        }
        return scrim;
    }

    function setPanelButtons(panel, open) {
        document.querySelectorAll('[data-en-panel="' + panel.id + '"]').forEach(function (b) {
            b.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    }

    function openPanel(panel) {
        if (!panel) return;
        panelScrim(panel).classList.add('open');
        panel.classList.add('open');
        setPanelButtons(panel, true);
        var x = panel.querySelector('[data-en-panel-close]');
        if (x) setTimeout(function () { x.focus({ preventScroll: true }); }, 30);
    }

    function closePanel(panel) {
        if (!panel) return;
        panel.classList.remove('open');
        panelScrim(panel).classList.remove('open');
        setPanelButtons(panel, false);
    }

    document.addEventListener('click', function (e) {
        var t = e.target.closest('[data-en-panel]');
        if (t) {
            var p = document.getElementById(t.getAttribute('data-en-panel'));
            if (p) (p.classList.contains('open') ? closePanel : openPanel)(p);
            return;
        }
        var c = e.target.closest('[data-en-panel-close]');
        if (c) closePanel(c.closest('.en-panel'));
    });

    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape' || document.querySelector('.en-modal.open')) return;
        document.querySelectorAll('.en-panel.open').forEach(closePanel);
    });

    // Close the panel when one of its buttons starts an action (print, send, navigate)
    document.addEventListener('submit', function (e) {
        var p = e.target.closest && e.target.closest('.en-panel');
        if (p) closePanel(p);
    });

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.en-panel[data-en-lift]').forEach(function (panel) {
            var items = panel.querySelectorAll(panel.getAttribute('data-en-lift'));
            var target = document.querySelector(panel.getAttribute('data-en-lift-to'));
            if (!items.length || !target) return;
            var box = document.createElement('div');
            box.className = 'en-lifted-alerts';
            items.forEach(function (el) { if (el.parentNode === panel) box.appendChild(el); });
            if (box.children.length) target.insertBefore(box, target.firstChild);
        });
    });

    window.eNotice = {
        openModal: openModal, closeModal: closeModal, openUrl: openUrl,
        openPanel: function (id) { openPanel(document.getElementById(id)); },
        closePanel: function (id) { closePanel(document.getElementById(id)); }
    };
})();
