/* Truvio Buying Assistant storefront widget.
   Talks to /truvio/buying-assistant/ask (server-sent events), renders the activity feed and the
   priced proposal, adds every line to the cart through the shop's cart service, and keeps the
   conversation id so follow-up questions refine the same proposal. No dependencies. */
(function () {
    if (window.__truvioBuyingAssistantInit) return;
    window.__truvioBuyingAssistantInit = true;

    function root(el) { return el.closest('[data-tba]'); }
    function labels(widget) {
        try { return JSON.parse(widget.getAttribute('data-tba-labels') || '{}'); } catch (e) { return {}; }
    }
    function fmt(template, a, b) { return String(template || '').replace('{0}', a).replace('{1}', b); }
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function el(tag, cls, html) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (html != null) e.innerHTML = html;
        return e;
    }

    function setBusy(widget, busy, text) {
        var btn = widget.querySelector('[data-tba-submit]');
        var status = widget.querySelector('[data-tba-status]');
        if (btn) btn.disabled = busy;
        widget.classList.toggle('is-busy', busy);
        if (status) status.textContent = busy ? (text || '') : '';
    }

    function activity(widget, type, text) {
        var box = widget.querySelector('[data-tba-activity]');
        if (!box) return;
        box.hidden = false;
        var line = el('div', 'tba__act tba__act--' + type);
        line.innerHTML = '<span class="tba__act-dot" aria-hidden="true"></span><span class="tba__act-text">' + esc(text) + '</span>';
        box.appendChild(line);
        var items = box.querySelectorAll('.tba__act');
        for (var i = 0; i < items.length - 1; i++) items[i].classList.add('is-past');
        box.scrollTop = box.scrollHeight;
    }

    function parseSse(chunk, onEvent) {
        var blocks = chunk.split('\n\n');
        for (var i = 0; i < blocks.length; i++) {
            var lines = blocks[i].split('\n');
            var data = '';
            for (var j = 0; j < lines.length; j++) {
                if (lines[j].indexOf('data:') === 0) data += lines[j].substring(5).trim();
            }
            if (!data) continue;
            try { onEvent(JSON.parse(data)); } catch (e) { /* ignore partial */ }
        }
    }

    function ask(widget, message) {
        var L = labels(widget);
        var form = widget.querySelector('[data-tba-form]');
        var textarea = form && form.querySelector('textarea');
        if (!message) message = textarea ? textarea.value.trim() : '';
        if (message.length < 3) { if (textarea) textarea.focus(); return; }

        setBusy(widget, true, L.working || 'Working on it');
        var act = widget.querySelector('[data-tba-activity]');
        if (act) { act.innerHTML = ''; act.hidden = false; }
        widget.classList.remove('has-result');

        var body = {
            conversationId: widget.getAttribute('data-tba-conversation') || '',
            message: message,
            pageId: parseInt(widget.getAttribute('data-tba-page') || '0', 10),
            paragraphId: parseInt(widget.getAttribute('data-tba-paragraph') || '0', 10),
            productId: widget.getAttribute('data-tba-product') || '',
            variantId: widget.getAttribute('data-tba-variant') || '',
            productName: widget.getAttribute('data-tba-product-name') || ''
        };

        fetch(widget.getAttribute('data-tba-endpoint') || '/truvio/buying-assistant/ask', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin',
            body: JSON.stringify(body)
        }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            var reader = r.body.getReader();
            var decoder = new TextDecoder();
            var buffer = '';
            var finished = false;
            function pump() {
                return reader.read().then(function (res) {
                    if (res.done) {
                        if (buffer.trim()) parseSse(buffer, handle);
                        if (!finished) setBusy(widget, false);
                        return;
                    }
                    buffer += decoder.decode(res.value, { stream: true });
                    var idx;
                    while ((idx = buffer.indexOf('\n\n')) >= 0) {
                        var block = buffer.substring(0, idx);
                        buffer = buffer.substring(idx + 2);
                        parseSse(block + '\n\n', handle);
                    }
                    return pump();
                });
            }
            function handle(evt) {
                if (evt.type === 'status') activity(widget, 'status', evt.text);
                else if (evt.type === 'tool_call') activity(widget, 'call', evt.text);
                else if (evt.type === 'tool_result') activity(widget, 'result', evt.text);
                else if (evt.type === 'text') activity(widget, 'text', evt.text);
                else if (evt.type === 'result') { finished = true; render(widget, evt.data); setBusy(widget, false); }
                else if (evt.type === 'error') { finished = true; renderError(widget, evt.text || (evt.data && evt.data.error)); setBusy(widget, false); }
                else if (evt.type === 'done') { if (!finished) setBusy(widget, false); }
            }
            return pump();
        }).catch(function () {
            setBusy(widget, false);
            renderError(widget, L.error || 'Something went wrong. Try again.');
        });
    }

    function renderError(widget, text) {
        var box = widget.querySelector('[data-tba-result]');
        if (!box) return;
        box.hidden = false;
        box.innerHTML = '<div class="tba__error">' + esc(text || 'Something went wrong.') + '</div>';
        widget.classList.add('has-result');
    }

    function render(widget, data) {
        var L = labels(widget);
        var box = widget.querySelector('[data-tba-result]');
        if (!box || !data) return;
        if (data.conversationId) widget.setAttribute('data-tba-conversation', data.conversationId);
        box.hidden = false;
        box.innerHTML = '';
        widget.classList.add('has-result');

        var act = widget.querySelector('[data-tba-activity]');
        if (act) act.classList.add('is-done');

        if (data.error && !(data.lines && data.lines.length)) {
            box.appendChild(el('div', 'tba__error', esc(data.error)));
        }

        if (data.followUpQuestion && !(data.lines && data.lines.length)) {
            var q = el('div', 'tba__question');
            q.innerHTML = '<p class="tba__question-text">' + esc(data.followUpQuestion) + '</p>';
            box.appendChild(q);
        }

        if (data.summary) {
            var sum = el('div', 'tba__summary');
            sum.innerHTML = '<p class="tba__summary-text">' + esc(data.summary) + '</p>';
            box.appendChild(sum);
        }

        if (data.assumptions && data.assumptions.length) {
            var det = el('details', 'tba__assumptions');
            det.innerHTML = '<summary>' + esc(L.assumptions || 'Assumptions') + ' (' + data.assumptions.length + ')</summary><ul>' +
                data.assumptions.map(function (a) { return '<li>' + esc(a) + '</li>'; }).join('') + '</ul>';
            box.appendChild(det);
        }

        if (data.lines && data.lines.length) {
            var wrap = el('div', 'tba__table-wrap');
            var t = el('table', 'table table-sm tba__table');
            t.innerHTML = '<thead><tr><th>' + esc(L.item || 'Item') + '</th><th class="text-end">' + esc(L.qty || 'Qty') + '</th><th class="text-end">' + esc(L.unitPrice || 'Unit price') + '</th><th class="text-end">' + esc(L.lineTotal || 'Line total') + '</th><th>' + esc(L.stock || 'Stock') + '</th></tr></thead>';
            var tb = el('tbody');
            data.lines.forEach(function (line) {
                var tr = el('tr', 'tba__line' + (line.isContextProduct ? ' is-context' : ''));
                tr.setAttribute('data-tba-line', '');
                tr.setAttribute('data-product-id', line.productId);
                tr.setAttribute('data-variant-id', line.variantId || '');
                tr.setAttribute('data-quantity', line.quantity);
                if (line.unitId) tr.setAttribute('data-unit-id', line.unitId);
                tr.innerHTML =
                    '<td><div class="tba__line-name">' + esc(line.name) + '</div><div class="tba__line-meta"><span class="tba__sku">' + esc(line.sku) + '</span>' + (line.reason ? '<span class="tba__reason">' + esc(line.reason) + '</span>' : '') + '</div></td>' +
                    '<td class="text-end tba__qty">' + esc(line.quantity) + (line.unit ? ' <span class="tba__unit">' + esc(line.unit) + '</span>' : '') + '</td>' +
                    '<td class="text-end">' + esc(line.unitPriceFormatted) + (line.tierLabel ? '<span class="tba__tier">' + esc(line.tierLabel) + '</span>' : '') + '</td>' +
                    '<td class="text-end tba__total">' + esc(line.lineTotalFormatted) + '</td>' +
                    '<td><span class="tba__stock ' + (line.inStock ? 'is-instock' : 'is-short') + '"><span class="tba__stock-dot" aria-hidden="true"></span>' + esc(line.stockLabel) + '</span></td>';
                tb.appendChild(tr);
            });
            t.appendChild(tb);
            var tf = el('tfoot');
            tf.innerHTML = '<tr><td colspan="3" class="text-end">' + esc(L.estimated || 'Estimated total') + '</td><td class="text-end tba__grand">' + esc(data.totalFormatted) + '</td><td></td></tr>';
            t.appendChild(tf);
            wrap.appendChild(t);
            box.appendChild(wrap);

            if (data.notes) box.appendChild(el('p', 'tba__notes', esc(data.notes)));

            var actions = el('div', 'tba__addall');
            var hideCart = widget.getAttribute('data-tba-hide-cart') === 'true';
            var cartService = widget.getAttribute('data-tba-cart-service');
            if (!hideCart && cartService) {
                var addBtn = el('button', 'btn btn-primary', esc(widget.getAttribute('data-tba-add-all-label') || 'Add all to cart') + ' (' + data.lines.length + ')');
                addBtn.type = 'button';
                addBtn.setAttribute('data-tba-addall', '');
                actions.appendChild(addBtn);
                var cartPage = widget.getAttribute('data-tba-cart-page');
                if (cartPage) {
                    var view = el('a', 'btn btn-outline-primary tba__viewcart', esc(L.viewCart || 'View cart'));
                    view.href = cartPage;
                    view.hidden = true;
                    view.setAttribute('data-tba-view-cart', '');
                    actions.appendChild(view);
                }
            }
            var st = el('span', 'tba__status');
            st.setAttribute('data-tba-addall-status', '');
            st.setAttribute('aria-live', 'polite');
            actions.appendChild(st);
            box.appendChild(actions);
            box.appendChild(el('p', 'tba__foot', esc(L.foot || '')));
        } else if (data.notes) {
            box.appendChild(el('p', 'tba__notes', esc(data.notes)));
        }

        // Follow-up box: refine the same conversation.
        var follow = el('form', 'tba__follow');
        follow.setAttribute('data-tba-follow', '');
        follow.innerHTML = '<label class="tba__label">' + esc(L.refine || 'Refine or ask a follow-up') + '</label>' +
            '<div class="tba__follow-row"><input type="text" class="form-control tba__follow-input" maxlength="4000" required>' +
            '<button type="submit" class="btn btn-secondary">' + esc(L.send || 'Send') + '</button>' +
            '<button type="button" class="btn btn-link tba__reset" data-tba-reset>' + esc(L.reset || 'Start over') + '</button></div>';
        box.appendChild(follow);
        box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function addAll(button) {
        var widget = root(button);
        if (!widget) return;
        var L = labels(widget);
        var cartUrl = widget.getAttribute('data-tba-cart-service');
        var lines = Array.prototype.slice.call(widget.querySelectorAll('[data-tba-line]'));
        if (!cartUrl || !lines.length) return;
        button.disabled = true;
        var status = widget.querySelector('[data-tba-addall-status]');
        var done = 0;
        function next(i) {
            if (i >= lines.length) {
                if (status) status.textContent = fmt(L.added || 'Added {0} lines to your cart', done);
                widget.classList.add('is-added');
                var go = widget.querySelector('[data-tba-view-cart]');
                if (go) go.hidden = false;
                // Swift's mini cart listens for this event (same one its own add-to-cart raises).
                try { document.dispatchEvent(new CustomEvent('updated.swift.cart', { cancelable: true, detail: { formData: new FormData(), parentEvent: null } })); } catch (e) { }
                document.dispatchEvent(new CustomEvent('truvio:assistant:added', { detail: { count: done } }));
                return;
            }
            var line = lines[i];
            var fd = new FormData();
            fd.append('cartcmd', 'add');
            fd.append('redirect', 'false');
            fd.append('ProductId', line.getAttribute('data-product-id'));
            fd.append('Quantity', line.getAttribute('data-quantity'));
            var variantId = line.getAttribute('data-variant-id');
            if (variantId) fd.append('VariantId', variantId);
            var unitId = line.getAttribute('data-unit-id');
            if (unitId) fd.append('UnitId', unitId);
            fd.append('ProductReferer', 'truvio_buying_assistant');
            if (status) status.textContent = fmt(L.adding || 'Adding {0} of {1}', i + 1, lines.length);
            fetch(cartUrl, { method: 'POST', body: fd, credentials: 'same-origin' })
                .then(function (r) { if (r.ok) { done++; line.classList.add('is-added'); } })
                .catch(function () { })
                .then(function () { next(i + 1); });
        }
        next(0);
    }

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || !form.matches) return;
        if (form.matches('[data-tba-form]')) {
            e.preventDefault();
            var widget = root(form);
            widget.removeAttribute('data-tba-conversation');
            ask(widget);
        } else if (form.matches('[data-tba-follow]')) {
            e.preventDefault();
            var w = root(form);
            var input = form.querySelector('input');
            var text = input ? input.value.trim() : '';
            if (text.length < 2) return;
            var ta = w.querySelector('[data-tba-form] textarea');
            if (ta) ta.value = text;
            ask(w, text);
        }
    });

    document.addEventListener('click', function (e) {
        var t = e.target.closest ? e.target.closest('[data-tba-example], [data-tba-addall], [data-tba-reset]') : null;
        if (!t) return;
        if (t.hasAttribute('data-tba-example')) {
            e.preventDefault();
            var widget = root(t);
            var textarea = widget && widget.querySelector('[data-tba-form] textarea');
            if (textarea) { textarea.value = t.getAttribute('data-tba-example'); textarea.focus(); }
            return;
        }
        if (t.hasAttribute('data-tba-addall')) { e.preventDefault(); addAll(t); return; }
        if (t.hasAttribute('data-tba-reset')) {
            e.preventDefault();
            var w = root(t);
            w.removeAttribute('data-tba-conversation');
            var res = w.querySelector('[data-tba-result]'); if (res) { res.hidden = true; res.innerHTML = ''; }
            var act = w.querySelector('[data-tba-activity]'); if (act) { act.hidden = true; act.innerHTML = ''; act.classList.remove('is-done'); }
            w.classList.remove('has-result', 'is-added');
            var ta = w.querySelector('[data-tba-form] textarea'); if (ta) { ta.value = ''; ta.focus(); }
        }
    });
})();
