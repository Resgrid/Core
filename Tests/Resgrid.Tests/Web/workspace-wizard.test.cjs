// Shared workspace step wizard (js/app/common/workspace/resgrid.common.workspace.js).
//
// The wizard turns native validation off, because a browser refuses to report a required
// field that sits on a hidden step. These cases pin the behaviour that replaces it: a step
// cannot be left while it is incomplete, and a submit from the last step jumps back to the
// step that is actually missing an answer instead of posting a half-filled command.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('./browser-launch.cjs').playwright();

const root = path.resolve(__dirname, '../../..');
const script = fs.readFileSync(path.join(root, 'Web/Resgrid.Web/wwwroot/js/app/common/workspace/resgrid.common.workspace.js'), 'utf8');

const markup = `
<script id="workspace-settings" type="application/json">{"required":"Fill in the highlighted fields.","nothingEntered":"Nothing entered","yes":"Yes","no":"No"}</script>
<form id="wizard" class="rgw-wizard" method="post">
    <input type="hidden" name="RequestId" value="abc" />
    <ol class="rgw-steps"></ol>
    <div class="rgw-step" data-title="What">
        <h4>What</h4>
        <div class="rgw-grid">
            <div class="form-group">
                <label for="item">Item</label>
                <select class="form-control" id="item" name="ItemId" required>
                    <option value="">&mdash;</option>
                    <option value="item-a">Bandages</option>
                </select>
            </div>
        </div>
    </div>
    <div class="rgw-step" data-title="How much">
        <h4>How much</h4>
        <div class="rgw-grid">
            <div class="form-group">
                <label for="qty">Amount</label>
                <input class="form-control" id="qty" name="Quantity" type="number" required />
            </div>
            <div class="form-group">
                <label for="note">Note</label>
                <input class="form-control" id="note" name="Note" />
            </div>
            <div class="form-group">
                <label>Days</label>
                <div>
                    <label class="checkbox-inline"><input type="checkbox" name="days" value="1" data-review-skip="true" checked /> Monday</label>
                    <label class="checkbox-inline"><input type="checkbox" name="days" value="2" data-review-skip="true" /> Tuesday</label>
                </div>
            </div>
        </div>
    </div>
    <div class="rgw-step" data-title="Review">
        <h4>Review</h4>
        <dl class="rgw-review"></dl>
    </div>
    <div class="rgw-wizard-actions">
        <button type="button" class="btn rgw-step-prev">Back</button>
        <button type="button" class="btn rgw-step-next">Continue</button>
        <button type="submit" class="btn rgw-step-submit">Save</button>
    </div>
</form>`;

(async () => {
    const browser = await chromium.launch(require('./browser-launch.cjs').launchOptions());
    try {
        const page = await browser.newPage();
        await page.setContent(markup);
        await page.addScriptTag({ content: script });

        const result = await page.evaluate(() => {
            function check(condition, message) { if (!condition) throw new Error(message); }

            const form = document.getElementById('wizard');
            const steps = Array.from(form.querySelectorAll('.rgw-step'));
            const next = form.querySelector('.rgw-step-next');
            const previous = form.querySelector('.rgw-step-prev');
            const submit = form.querySelector('.rgw-step-submit');
            const indicator = form.querySelector('.rgw-steps');

            // The wizard, not the browser, decides when a step is complete.
            check(form.hasAttribute('novalidate'), 'The wizard left native validation on, which cannot report hidden steps.');

            check(!steps[0].hidden && steps[1].hidden && steps[2].hidden, 'The wizard did not open on its first step.');
            check(previous.hidden && !next.hidden && submit.hidden, 'The first step offered Back or Save.');
            check(indicator.children.length === 3, 'The step indicator did not list every step.');
            check(indicator.children[0].className === 'current' && Array.from(indicator.children).map(li => li.textContent).join('|') === '1What|2How much|3Review',
                'The step indicator did not label the steps in order.');

            // An incomplete step keeps the user where they are and says which field is missing.
            next.click();
            check(!steps[0].hidden, 'An incomplete step let the user continue.');
            check(form.querySelector('#item').closest('.form-group').classList.contains('rgw-invalid'), 'The missing field was not marked.');
            check((steps[0].querySelector('.rgw-step-error') || {}).hidden === false, 'No message explained why the step would not advance.');

            form.querySelector('#item').value = 'item-a';
            next.click();
            check(steps[0].hidden && !steps[1].hidden, 'A completed step did not advance.');
            check(!previous.hidden, 'Back was still hidden on the second step.');

            previous.click();
            check(!steps[0].hidden && !form.querySelector('#item').closest('.form-group').classList.contains('rgw-invalid'),
                'Going back did not restore the first step or left it marked invalid.');

            next.click();
            form.querySelector('#qty').value = '4';
            form.querySelector('#note').value = 'restock';
            next.click();
            check(!steps[2].hidden && next.hidden && !submit.hidden, 'The last step did not swap Continue for Save.');

            // The review step reads back what was answered, skipping empty and hidden fields.
            const review = Array.from(form.querySelectorAll('.rgw-review-row')).map(row => row.textContent);
            check(review.length === 3, 'The review step did not list every answered field: ' + review.join(' / '));
            check(review[0] === 'ItemBandages', 'The review step showed the option value instead of its label: ' + review[0]);
            check(review[1] === 'Amount4' && review[2] === 'Noterestock', 'The review step lost a later answer: ' + review.join(' / '));
            check(!review.some(row => row.includes('abc')), 'The review step exposed a hidden field.');
            // A repeated checkbox group would otherwise list one Yes/No row per box, all sharing
            // the group's single label, so those boxes opt out of the review.
            check(!review.some(row => row.startsWith('Days')), 'A checkbox marked data-review-skip reached the review step.');

            // A late edit that empties a required field must not slip through the review step.
            const posts = [];
            form.addEventListener('submit', event => { posts.push(event.defaultPrevented); event.preventDefault(); });

            form.querySelector('#qty').value = '';
            submit.click();
            check(posts.length === 1 && posts[0] === true, 'A submit with an incomplete step was not cancelled.');
            check(!steps[1].hidden, 'The cancelled submit did not return to the incomplete step.');
            check(form.querySelector('#qty').closest('.form-group').classList.contains('rgw-invalid'), 'The incomplete field was not marked after the submit.');

            const quantity = form.querySelector('#qty');
            quantity.value = '4';
            quantity.dispatchEvent(new Event('input', { bubbles: true }));
            check(!quantity.closest('.form-group').classList.contains('rgw-invalid'), 'Typing did not clear the invalid mark.');

            next.click();
            submit.click();
            check(posts.length === 2 && posts[1] === false, 'A complete wizard did not submit.');
            return 'ok';
        });

        assert.equal(result, 'ok');
        console.log('Workspace wizard step navigation, review and submit guard passed.');
    } finally {
        await browser.close();
    }
})().catch((error) => { console.error(error); process.exit(1); });
