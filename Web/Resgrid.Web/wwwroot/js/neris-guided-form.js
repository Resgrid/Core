/* Guided authoring from the embedded, pinned NERIS contract. No report content is inserted as HTML. */
(function (root) {
    "use strict";
    let sequence = 0;
    const own = (value, key) => Object.prototype.hasOwnProperty.call(value || {}, key);
    const copy = value => value === undefined ? undefined : JSON.parse(JSON.stringify(value));
    const label = value => ({ FF: "Firefighter", NONFF: "Nonfirefighter" }[value]) || String(value || "Entry").replace(/Payload$|Value$/g, "").replace(/([a-z])([A-Z])/g, "$1 $2")
        .replace(/\|\|/g, " / ").replace(/_/g, " ").toLowerCase().replace(/(^|\s)[a-z]/g, c => c.toUpperCase());
    function resolve(schema, schemas, depth = 0) {
        if (depth > 24 || !schema || typeof schema !== "object") throw new Error("Unsupported reporting field.");
        if (!schema.$ref) return schema;
        const name = schema.$ref.replace("#/components/schemas/", "");
        if (name === schema.$ref || !own(schemas, name)) throw new Error("Missing reporting field definition.");
        const result = Object.assign({}, resolve(schemas[name], schemas, depth + 1), schema);
        delete result.$ref;
        return result;
    }
    function createEditor(host, schema, initial, schemas, options = {}) {
        const doc = host.ownerDocument;
		const moveField = new WeakMap();
		function structureChanged() {
			host.dispatchEvent(new doc.defaultView.CustomEvent("neris-structure-change", { bubbles: true }));
		}
        function el(tag, text, parent, cls) {
            const node = doc.createElement(tag);
            if (text !== undefined) node.textContent = text;
            if (cls) node.className = cls;
            if (parent) parent.appendChild(node);
            return node;
        }
        function field(parent, spec, value, title, path, depth, optional) {
            if (depth > 24) throw new Error("This section exceeds the supported nesting depth.");
            spec = resolve(spec, schemas);
            let variants = spec.oneOf || spec.anyOf;
            if (variants) {
                variants = variants.map(v => resolve(v, schemas)).filter(v => v.type !== "null");
                if (variants.length === 1) {
                    const merged = Object.assign({}, spec, variants[0]);
                    delete merged.anyOf; delete merged.oneOf;
                    return field(parent, merged, value, title, path, depth + 1, optional);
                }
            }
            const name = spec["x-ui-label"] || spec.title || title;
            const wrap = el("div", undefined, parent, "neris-field");
            wrap.dataset.nerisPath = path;
			wrap.dataset.nerisFullPath = (options.root || "") + path;
			moveField.set(wrap, nextPath => {
				path = nextPath;
				wrap.dataset.nerisPath = path;
				wrap.dataset.nerisFullPath = (options.root || "") + path;
			});
            const id = "neris-field-" + (++sequence);
            let head = el("label", name + (optional ? "" : " *"), wrap);
            head.htmlFor = id;
            const hint = spec["x-ui-hint"];
            if (hint) { const help = el("p", hint, wrap, "help-block"); help.id = id + "-hint"; }
            if (variants && variants.length > 1) {
                const select = el("select", undefined, wrap, "form-control"); select.id = id;
                el("option", "Choose a type", select).value = "";
                const discriminator = spec.discriminator && spec.discriminator.propertyName || "type";
                variants.forEach((v, i) => {
                    const t = resolve((v.properties || {})[discriminator] || {}, schemas);
                    const text = own(t, "const") ? label(t.const) : t.enum ? t.enum.map(label).join(" / ") : label(v.title || "Type " + (i + 1));
                    el("option", text, select).value = String(i);
                });
                let chosen = variants.findIndex(v => {
                    const t = resolve((v.properties || {})[discriminator] || {}, schemas);
                    return value && (own(t, "const") ? value[discriminator] === t.const : t.enum && t.enum.includes(value[discriminator]));
                });
                const area = el("div", undefined, wrap, "neris-nested");
                let current = null;
                const drafts = new Map();
                if (chosen >= 0) drafts.set(chosen, copy(value));
                function choose() {
                    if (current && chosen >= 0) drafts.set(chosen, current.get());
                    chosen = select.value === "" ? -1 : Number(select.value);
                    area.replaceChildren();
                    current = chosen < 0 ? null : field(area, variants[chosen], drafts.get(chosen), title, path, depth + 1, false);
                }
                select.value = chosen < 0 ? "" : String(chosen);
				choose(); select.addEventListener("change", () => { choose(); structureChanged(); });
                return { get: () => current ? current.get() : undefined };
            }
            if (own(spec, "const")) {
                const control = el("input", undefined, wrap, "form-control"); control.id = id; control.value = label(spec.const); control.readOnly = true;
                return { get: () => spec.const };
            }
            const complex = spec.type === "object" || spec.properties || spec.type === "array";
            if (complex && optional) {
                head.remove();
                const toggleLabel = el("label", undefined, wrap, "checkbox-inline");
                const toggle = el("input", undefined, toggleLabel); toggle.type = "checkbox"; toggle.id = id;
                toggleLabel.appendChild(doc.createTextNode(" Include " + name));
                toggle.checked = value !== undefined && value !== null;
                const area = el("div", undefined, wrap, "neris-nested");
                let child = null;
                function activate() {
                    area.hidden = !toggle.checked;
                    if (toggle.checked && !child) child = field(area, spec, value, title, path, depth + 1, false);
                }
				toggle.addEventListener("change", () => { activate(); structureChanged(); }); activate();
                return { get: () => toggle.checked && child ? child.get() : undefined };
            }
            if (spec.type === "object" || spec.properties) {
                head.removeAttribute("for");
                const original = value && typeof value === "object" && !Array.isArray(value) ? copy(value) : {};
                const fields = new Map();
                const required = spec.required || [];
                Object.entries(spec.properties || {}).forEach(([key, child]) => {
                    if (["__proto__", "constructor", "prototype"].includes(key)) throw new Error("Invalid reporting field.");
                    const childPath = path + "/" + key;
                    if ((options.exclude || []).includes(childPath)) return;
                    fields.set(key, field(wrap, child, original[key], label(key), childPath, depth + 1, !required.includes(key)));
                });
                const unknown = Object.keys(original).filter(k => !own(spec.properties, k));
                if (unknown.length) el("p", "Previously saved fields outside this form are preserved. Run validation before finalizing.", wrap, "text-warning");
                return { get: () => {
                    const result = copy(original);
                    fields.forEach((child, key) => { const v = child.get(); if (v === undefined) delete result[key]; else result[key] = v; });
                    return result;
                }};
            }
            if (spec.type === "array") {
                head.removeAttribute("for");
                const itemSpec = resolve(spec.items || {}, schemas);
                if (itemSpec.enum) {
                    const select = el("select", undefined, wrap, "form-control"); select.multiple = true; select.id = id;
                    select.setAttribute("aria-label", name); select.size = Math.min(8, itemSpec.enum.length);
                    const known = new Set(itemSpec.enum);
                    const values = Array.isArray(value) ? value : [];
                    [...itemSpec.enum, ...values.filter(v => !known.has(v))].forEach(v => {
                        const option = el("option", label(v), select); option.value = JSON.stringify(v); option.selected = values.includes(v);
                    });
                    el("small", "Select all that apply. Use Ctrl or Command to select multiple options.", wrap, "help-block");
                    return { get: () => Array.from(select.selectedOptions).map(o => JSON.parse(o.value)) };
                }
                const list = el("div", undefined, wrap, "neris-nested");
                const rows = [];
                const add = el("button", "Add " + label(title), wrap, "btn btn-default btn-sm"); add.type = "button";
                function addRow(v) {
                    if (rows.length >= (spec.maxItems || 1000)) return;
                    const area = el("div", undefined, list, "neris-array-entry");
                    const child = field(area, spec.items || {}, v, /coordinates$/.test(path) ? (rows.length === 0 ? "Longitude" : "Latitude") : "Entry", path + "/" + rows.length, depth + 1, false);
                    const remove = el("button", "Remove entry", area, "btn btn-default btn-sm"); remove.type = "button";
                    const row = { area, child }; rows.push(row);
                    remove.addEventListener("click", () => {
                        rows.splice(rows.indexOf(row), 1); area.remove(); add.disabled = false;
                        rows.forEach((remaining, index) => {
                            const previous = remaining.area.firstElementChild.dataset.nerisPath;
                            const next = path + "/" + index;
                            remaining.area.querySelectorAll("[data-neris-path]").forEach(node => {
                                const currentPath = node.dataset.nerisPath;
                                if (currentPath === previous || currentPath.startsWith(previous + "/"))
                                    moveField.get(node)(next + currentPath.slice(previous.length));
                            });
                        });
                        structureChanged();
                    });
                    add.disabled = rows.length >= (spec.maxItems || 1000);
                }
                (Array.isArray(value) ? value : []).forEach(addRow);
                add.addEventListener("click", () => { addRow(undefined); structureChanged(); });
                return { get: () => rows.map(r => r.child.get()).filter(v => v !== undefined) };
            }
            let control;
            if (spec.enum || spec.type === "boolean") {
                control = el("select", undefined, wrap, "form-control");
                el("option", "Choose a value", control).value = "";
                const choices = spec.enum || [true, false];
                if (value !== undefined && value !== null && !choices.includes(value)) el("option", "Previously recorded: " + label(value), control).value = JSON.stringify(value);
                choices.forEach(v => { el("option", typeof v === "boolean" ? (v ? "Yes" : "No") : label(v), control).value = JSON.stringify(v); });
                control.value = value === undefined || value === null ? "" : JSON.stringify(value);
            } else {
                const multiline = spec.type === "string" && ((spec.maxLength || 0) > 512 || /narrative|description|comment/.test(path));
                control = el(multiline ? "textarea" : "input", undefined, wrap, "form-control");
                if (multiline) control.rows = 3;
                else control.type = spec.type === "integer" || spec.type === "number" ? "number" : spec.format === "date" ? "date" : "text";
                if (spec.type === "integer" || spec.type === "number") {
                    control.step = spec.type === "integer" ? "1" : "any";
                    if (spec.minimum !== undefined) control.min = spec.minimum;
                    if (spec.maximum !== undefined) control.max = spec.maximum;
                }
                if (spec.maxLength) control.maxLength = spec.maxLength;
                if (spec.format === "date-time") { control.placeholder = "YYYY-MM-DDTHH:MM:SSZ"; el("small", "Enter an ISO timestamp including its time zone (Z for UTC).", wrap, "help-block"); }
                control.value = value === undefined || value === null ? "" : String(value);
            }
            control.id = id;
            if (hint) control.setAttribute("aria-describedby", id + "-hint");
            return { get: () => {
                if (control.value === "") return undefined;
                if (spec.enum || spec.type === "boolean") return JSON.parse(control.value);
                if (spec.type === "integer" || spec.type === "number") {
                    const number = Number(control.value);
                    if (!Number.isFinite(number) || (spec.type === "integer" && !Number.isInteger(number))) throw new Error("Enter a valid number for " + name + ".");
                    return number;
                }
                return control.value;
            }};
        }
        const editor = field(host, schema, initial, options.title || "Section details", "", 0, false);
        // These conditions are prose rules in the pinned contract, outside its JSON Schema keywords.
        if (resolve(schema, schemas).title === "CasualtyRescuePayload") {
            const conditions = [
                { path: "/rank", personType: "FF" },
                { path: "/years_of_service", personType: "FF" },
                { path: "/rescue/mayday", personType: "FF" },
                { path: "/casualty/injury_or_noninjury/ff_injury_details", personType: "FF" },
                { path: "/rescue/presence_known", personType: "NONFF" }
            ];
            const help = el("p", "Choose whether the person is a firefighter. Fields that do not apply are excluded when saving; switching back during this edit restores their draft values.", host, "help-block");
            help.setAttribute("role", "note");
            const typeField = Array.from(host.querySelectorAll("[data-neris-path]")).find(node => node.dataset.nerisPath === "/type");
            const typeControl = typeField && typeField.querySelector("select");
            function personType() { return typeControl && typeControl.value ? JSON.parse(typeControl.value) : null; }
            function showApplicable() {
                const type = personType();
                for (const rule of conditions) for (const node of host.querySelectorAll("[data-neris-path]"))
                    if (node.dataset.nerisPath === rule.path) node.hidden = type !== rule.personType;
            }
            host.addEventListener("change", showApplicable);
			host.addEventListener("neris-structure-change", showApplicable);
            showApplicable();
            return { get: () => {
                const result = editor.get();
                for (const rule of conditions) if (result.type !== rule.personType) {
                    const keys = rule.path.slice(1).split("/");
                    const key = keys.pop();
                    const parent = keys.reduce((value, part) => value && value[part], result);
                    if (parent) delete parent[key];
                }
                return result;
            }};
        }
        return editor;
    }
    async function initialize(form) {
        const inputs = Array.from(form.querySelectorAll("input[data-neris-schema]"));
        if (!inputs.length) return;
        let ready = false;
        const editors = [];
        const status = form.ownerDocument.createElement("p"); status.setAttribute("role", "alert"); status.className = "alert alert-info";
        status.textContent = "Loading report fields…"; form.prepend(status);
        form.addEventListener("submit", event => {
            try {
                if (!ready) throw new Error("Report fields could not be loaded. Reload this page before saving.");
                // Read every field first. A failure must never partially replace hidden stored values.
                const values = editors.map(e => JSON.stringify(e.editor.get() || {}));
                editors.forEach((e, i) => { e.input.value = values[i]; });
            } catch (error) { event.preventDefault(); status.hidden = false; status.textContent = error.message; status.className = "alert alert-danger"; status.focus(); }
        });
        try {
            const response = await fetch(form.dataset.nerisSchemaUrl, { credentials: "same-origin", headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error("Reporting fields are unavailable. Reload the page before saving.");
            const schemas = await response.json();
            for (const input of inputs) {
                if (!own(schemas, input.dataset.nerisSchema)) throw new Error("A reporting section could not be loaded.");
                const host = form.ownerDocument.createElement("div"); host.className = "neris-guided-section"; input.after(host);
                const initial = JSON.parse(input.value || "{}");
                const editor = createEditor(host, schemas[input.dataset.nerisSchema], initial, schemas, { exclude: (input.dataset.nerisExclude || "").split(","), title: "Section details", root: input.dataset.nerisRoot || "" });
                editors.push({ input, editor });
                const includeName = input.name.replace(/DetailJson$/, "Included");
                const include = form.elements.namedItem(includeName);
                if (include && include.type === "checkbox") { const show = () => { host.hidden = !include.checked; }; include.addEventListener("change", show); show(); }
            }
            form.querySelectorAll("[data-neris-issue]").forEach(issue => {
                let path = issue.dataset.nerisIssue;
                if (!path.startsWith("/")) path = "/" + path.replace(/\[(\d+)\]/g, "/$1").replace(/\./g, "/");
                const fields = Array.from(form.querySelectorAll("[data-neris-full-path]"));
                const field = fields.find(f => f.dataset.nerisFullPath === path);
                if (!field) return;
                field.classList.add("has-error");
                const control = field.querySelector("input,select,textarea");
                if (!control) return;
                control.setAttribute("aria-invalid", "true");
                const jump = form.ownerDocument.createElement("button"); jump.type = "button"; jump.className = "btn btn-link btn-xs";
                jump.textContent = "Go to field";
                jump.addEventListener("click", () => { field.scrollIntoView({ block: "center" }); control.focus(); });
				field.closest(".neris-guided-section").addEventListener("neris-structure-change", () => {
					field.classList.remove("has-error"); control.removeAttribute("aria-invalid");
					jump.disabled = true; jump.textContent = "Section changed — save and validate again";
				}, { once: true });
                issue.appendChild(jump);
            });
            ready = true; status.hidden = true; form.querySelectorAll("[data-neris-save]").forEach(button => { button.disabled = false; });
        } catch (error) { status.textContent = error.message; status.className = "alert alert-danger"; }
    }
    const api = { createEditor, resolve, initialize };
    if (typeof module !== "undefined" && module.exports) module.exports = api;
    else { root.NerisGuidedForm = api; document.querySelectorAll("form[data-neris-schema-url]").forEach(initialize); }
})(typeof window !== "undefined" ? window : globalThis);
