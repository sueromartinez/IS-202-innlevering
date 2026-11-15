/* =========================================================
   SAMI – hjelpeassistenten (chat-widget)

   - Sender samtalen til /Sami/Chat (serveren snakker med modellen).
   - Samtalen huskes i sessionStorage, så den overlever sidebytte
     men forsvinner når nettleserfanen lukkes.
   - All tekst fra modellen escapes før den vises. Bare enkle
     formateringer (fet skrift, lister og interne lenker) tillates.
   ========================================================= */

(function () {
    "use strict";

    const root = document.getElementById("sami");

    if (!root) {
        return;
    }

    const role = root.dataset.role || "volunteer";
    const chatUrl = root.dataset.chatUrl;
    const requestUrl = root.dataset.requestUrl;
    const registerResourceUrl = root.dataset.registerResourceUrl;
    const tokenInput = root.querySelector('input[name="__RequestVerificationToken"]');
    const token = tokenInput ? tokenInput.value : "";

    const launcher = document.getElementById("samiLauncher");
    const panel = document.getElementById("samiPanel");
    const closeButton = document.getElementById("samiClose");
    const resetButton = document.getElementById("samiReset");
    const messagesEl = document.getElementById("samiMessages");
    const startersEl = document.getElementById("samiStarters");
    const form = document.getElementById("samiForm");
    const input = document.getElementById("samiInput");
    const sendButton = document.getElementById("samiSend");
    const hint = document.getElementById("samiHint");

    const historyKey = "sami:" + root.dataset.user + ":history";
    const openKey = "sami:" + root.dataset.user + ":open";
    const hintKey = "sami:hint-dismissed";


    // ---------------------------------------------------------
    // TEKST PER ROLLE
    // ---------------------------------------------------------

    const welcome = {
        volunteer:
            "Hei! Jeg er Sami, hjelpeassistenten i portalen. Fortell meg hva du vil tilby og hvor det står, " +
            "så **registrerer jeg ressursen** for deg. Du kan også spørre meg om **alt i portalen**, " +
            "for eksempel nivå, mørk modus eller innlogging.",
        public:
            "Hei! Jeg er Sami. Beskriv en hendelse, f.eks. «flom i Grimstad sentrum», så finner jeg " +
            "**ressurser i nærheten** som kan passe. Du kan også spørre meg om **alt i portalen**.",
        admin:
            "Hei! Jeg er Sami. Jeg kan finne **ressurser til behov og hendelser**, gi **tall for portalen** " +
            "og svare på spørsmål om **alt i portalen**, også brukeradministrasjon."
    };

    const starters = {
        volunteer: [
            "Hjelp meg å registrere en ressurs",
            "Hvordan går jeg opp i nivå?",
            "Hvilke behov kan jeg hjelpe med?",
            "Hvordan skrur jeg på mørk modus?"
        ],
        public: [
            "Det er flom i Grimstad sentrum. Hva har vi i nærheten?",
            "Hvilke akutte behov mangler ressurser?",
            "Hvordan skrur jeg på mørk modus?"
        ],
        admin: [
            "Gi meg en oversikt over ressurser og behov",
            "Hvilke akutte behov mangler ressurser?",
            "Hvordan oppretter jeg en offentlig aktør?"
        ]
    };


    // ---------------------------------------------------------
    // LAGRING (kan feile i privat modus, derfor try/catch)
    // ---------------------------------------------------------

    function load(storage, key, fallback) {
        try {
            const value = storage.getItem(key);
            return value === null ? fallback : JSON.parse(value);
        } catch (error) {
            return fallback;
        }
    }

    function save(storage, key, value) {
        try {
            storage.setItem(key, JSON.stringify(value));
        } catch (error) {
            /* Ignoreres: samtalen fungerer fortsatt, men huskes ikke. */
        }
    }

    let history = load(sessionStorage, historyKey, []);

    if (!Array.isArray(history)) {
        history = [];
    }

    let busy = false;


    // ---------------------------------------------------------
    // TRYGG FORMATERING AV TEKST FRA MODELLEN
    // ---------------------------------------------------------

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function formatInline(escaped) {
        return escaped
            // **fet**
            .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
            // [tekst](/intern/sti) – bare relative lenker i portalen
            .replace(/\[([^\]]+)\]\((\/(?!\/)[^)\s]*)\)/g, '<a href="$2">$1</a>')
            // Andre lenker vises bare som tekst
            .replace(/\[([^\]]+)\]\(([^)]*)\)/g, "$1");
    }

    function formatMessage(text) {
        const lines = escapeHtml(text).split(/\r?\n/);
        let html = "";
        let listType = null;
        let paragraph = [];

        function flushParagraph() {
            if (paragraph.length > 0) {
                html += "<p>" + formatInline(paragraph.join("<br>")) + "</p>";
                paragraph = [];
            }
        }

        function closeList() {
            if (listType) {
                html += "</" + listType + ">";
                listType = null;
            }
        }

        lines.forEach(function (rawLine) {
            const line = rawLine.trim();
            const bullet = line.match(/^[-*•]\s+(.*)$/);
            const numbered = line.match(/^\d+[.)]\s+(.*)$/);

            if (bullet || numbered) {
                flushParagraph();
                const wanted = bullet ? "ul" : "ol";

                if (listType !== wanted) {
                    closeList();
                    html += "<" + wanted + ">";
                    listType = wanted;
                }

                html += "<li>" + formatInline((bullet || numbered)[1]) + "</li>";
                return;
            }

            closeList();

            if (line === "") {
                flushParagraph();
            } else {
                paragraph.push(line);
            }
        });

        flushParagraph();
        closeList();

        return html;
    }


    // ---------------------------------------------------------
    // VISNING
    // ---------------------------------------------------------

    function addMessageElement(from, text, isError) {
        const element = document.createElement("div");
        element.className = "sami-message " + (from === "user" ? "from-user" : "from-sami");

        if (isError) {
            element.classList.add("is-error");
        }

        if (from === "user") {
            element.textContent = text;
        } else {
            element.innerHTML = formatMessage(text);
        }

        messagesEl.appendChild(element);
        return element;
    }

    function addSuggestionCard(suggestion, entry) {
        const card = document.createElement("article");
        card.className = "sami-card";

        const title = document.createElement("h3");
        title.textContent = suggestion.resourceName;

        const meta = document.createElement("p");
        meta.className = "sami-card-meta";
        meta.textContent = suggestion.resourceType + ", " +
            Number(suggestion.distanceKm).toLocaleString("nb-NO") + " km unna";

        const reason = document.createElement("p");
        reason.className = "sami-card-reason";
        reason.textContent = suggestion.reason;

        const need = document.createElement("p");
        need.className = "sami-card-need";
        need.textContent = "Til behovet: " + suggestion.needDescription;

        const button = document.createElement("button");
        button.type = "button";
        button.className = "sami-card-button";
        button.textContent = "Send forespørsel";

        const status = document.createElement("p");
        status.className = "sami-card-status";
        status.setAttribute("role", "status");

        if (suggestion.sent) {
            button.disabled = true;
            button.textContent = "Forespørsel sendt";
            status.classList.add("is-success");
            status.textContent = suggestion.sentMessage || "Eieren har fått varsel.";
        }

        button.addEventListener("click", function () {
            sendResourceRequest(suggestion, button, status);
        });

        card.append(title, meta, reason, need, button, status);
        messagesEl.appendChild(card);
    }

    // Utkast til en ny ressurs. Knappen sender det vanlige registreringsskjemaet,
    // så serveren validerer og lagrer akkurat som når brukeren fyller det ut selv.
    function addResourceDraftCard(draft) {
        const summary = draft.summary || {};

        const card = document.createElement("article");
        card.className = "sami-card sami-draft-card";

        const title = document.createElement("h3");
        title.textContent = summary.name || "Ny ressurs";

        const meta = document.createElement("p");
        meta.className = "sami-card-meta";
        meta.textContent = "Utkast til ny ressurs, ikke lagret ennå";

        const list = document.createElement("dl");
        list.className = "sami-draft-fields";

        [
            ["Type", summary.typeName],
            ["Beskrivelse", summary.description],
            ["Antall", summary.quantity],
            ["Kapasitet", summary.capacity],
            ["Tilgjengelig i", summary.availability],
            ["Adresse", summary.address],
            ["Telefon", summary.phone]
        ].forEach(function (row) {
            if (row[1] === null || row[1] === undefined || row[1] === "") {
                return;
            }

            const term = document.createElement("dt");
            term.textContent = row[0];

            const value = document.createElement("dd");
            value.textContent = String(row[1]);

            list.append(term, value);
        });

        const hint = document.createElement("p");
        hint.className = "sami-card-need";
        hint.textContent = "Sjekk at adressen stemmer. Vil du endre noe, si det til meg.";

        const button = document.createElement("button");
        button.type = "button";
        button.className = "sami-card-button";
        button.textContent = draft.sent ? "Sendt til registrering" : "Registrer ressursen";
        button.disabled = !!draft.sent || !registerResourceUrl;

        button.addEventListener("click", function () {
            submitResourceDraft(draft, button);
        });

        card.append(title, meta, list, hint, button);
        messagesEl.appendChild(card);
    }

    function submitResourceDraft(draft, button) {
        if (!registerResourceUrl || !draft.fields) {
            return;
        }

        button.disabled = true;
        button.textContent = "Registrerer …";

        // Husk at utkastet er sendt, så knappen ikke kan brukes to ganger etter sidebytte.
        draft.sent = true;
        save(sessionStorage, historyKey, history);

        // Et vanlig skjema som sendes med POST, akkurat som "Registrer ressurs"-siden.
        const form = document.createElement("form");
        form.method = "post";
        form.action = registerResourceUrl;
        form.hidden = true;

        Object.keys(draft.fields).forEach(function (name) {
            const value = draft.fields[name];

            if (value === null || value === undefined) {
                return;
            }

            const input = document.createElement("input");
            input.type = "hidden";
            input.name = name;
            input.value = String(value);
            form.appendChild(input);
        });

        const tokenField = document.createElement("input");
        tokenField.type = "hidden";
        tokenField.name = "__RequestVerificationToken";
        tokenField.value = token;
        form.appendChild(tokenField);

        document.body.appendChild(form);
        form.submit();
    }

    function renderStarters() {
        startersEl.innerHTML = "";

        if (history.length > 0) {
            return;
        }

        (starters[role] || starters.volunteer).forEach(function (text) {
            const chip = document.createElement("button");
            chip.type = "button";
            chip.className = "sami-starter";
            chip.textContent = text;
            chip.addEventListener("click", function () {
                sendMessage(text);
            });
            startersEl.appendChild(chip);
        });
    }

    function renderAll() {
        messagesEl.innerHTML = "";
        addMessageElement("sami", welcome[role] || welcome.volunteer);

        history.forEach(function (entry) {
            addMessageElement(entry.role === "user" ? "user" : "sami", entry.text, entry.isError);

            (entry.suggestions || []).forEach(function (suggestion) {
                addSuggestionCard(suggestion, entry);
            });

            (entry.resourceDrafts || []).forEach(function (draft) {
                addResourceDraftCard(draft);
            });
        });

        renderStarters();
        scrollToBottom();
    }

    function scrollToBottom() {
        messagesEl.scrollTop = messagesEl.scrollHeight;
    }

    function showTyping() {
        const element = document.createElement("div");
        element.className = "sami-message from-sami";
        element.id = "samiTyping";
        element.innerHTML =
            '<span class="sami-typing">' +
            '<span class="sami-typing-dots" aria-hidden="true"><span></span><span></span><span></span></span>' +
            "Sami tenker …</span>";
        messagesEl.appendChild(element);
        scrollToBottom();
    }

    function hideTyping() {
        const element = document.getElementById("samiTyping");

        if (element) {
            element.remove();
        }
    }

    function setBusy(value) {
        busy = value;
        sendButton.disabled = value;
        input.setAttribute("aria-busy", value ? "true" : "false");
    }


    // ---------------------------------------------------------
    // SNAKK MED SERVEREN
    // ---------------------------------------------------------

    async function readJson(response) {
        const type = response.headers.get("content-type") || "";

        if (!type.includes("application/json")) {
            // Typisk en omdirigering til innlogging.
            throw new Error("Du ser ut til å være logget ut. Last inn siden på nytt og logg inn.");
        }

        return response.json();
    }

    async function sendMessage(text) {
        const message = (text || "").trim();

        if (!message || busy) {
            return;
        }

        history.push({ role: "user", text: message });
        save(sessionStorage, historyKey, history);

        startersEl.innerHTML = "";
        addMessageElement("user", message);
        input.value = "";
        resizeInput();
        setBusy(true);
        showTyping();

        try {
            const response = await fetch(chatUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    "RequestVerificationToken": token
                },
                body: JSON.stringify({
                    // Bare tekst sendes; feilmeldinger og forslagskort holdes utenfor.
                    messages: history
                        .filter(function (entry) { return !entry.isError; })
                        .map(function (entry) { return { role: entry.role, text: entry.text }; })
                })
            });

            const data = await readJson(response);

            if (!response.ok) {
                throw new Error(data.error || "Sami fikk ikke svart. Prøv igjen om litt.");
            }

            const entry = {
                role: "assistant",
                text: data.reply,
                suggestions: data.suggestions || [],
                resourceDrafts: data.resourceDrafts || []
            };

            history.push(entry);
            save(sessionStorage, historyKey, history);

            hideTyping();
            addMessageElement("sami", entry.text);
            entry.suggestions.forEach(function (suggestion) {
                addSuggestionCard(suggestion, entry);
            });
            entry.resourceDrafts.forEach(function (draft) {
                addResourceDraftCard(draft);
            });
        } catch (error) {
            hideTyping();

            const text = error instanceof TypeError
                ? "Fikk ikke kontakt med serveren. Sjekk nettforbindelsen og prøv igjen."
                : error.message;

            history.push({ role: "assistant", text: text, isError: true });
            save(sessionStorage, historyKey, history);
            addMessageElement("sami", text, true);
        } finally {
            setBusy(false);
            scrollToBottom();
            input.focus();
        }
    }

    async function sendResourceRequest(suggestion, button, status) {
        button.disabled = true;
        button.textContent = "Sender …";
        status.className = "sami-card-status";
        status.textContent = "";

        const body = new URLSearchParams();
        body.append("needId", suggestion.needId);
        body.append("resourceId", suggestion.resourceId);
        body.append("message", suggestion.reason || "");
        body.append("suggestedByAssistant", "true");
        body.append("__RequestVerificationToken", token);

        try {
            const response = await fetch(requestUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: { "Accept": "application/json" },
                body: body
            });

            const data = await readJson(response);

            if (!response.ok || !data.success) {
                throw new Error(data.message || "Kunne ikke sende forespørselen.");
            }

            button.textContent = "Forespørsel sendt";
            status.classList.add("is-success");
            status.textContent = data.message;

            // Husk at forslaget er sendt, også etter sidebytte.
            suggestion.sent = true;
            suggestion.sentMessage = data.message;
            save(sessionStorage, historyKey, history);
        } catch (error) {
            button.disabled = false;
            button.textContent = "Send forespørsel";
            status.classList.add("is-error");
            status.textContent = error instanceof TypeError
                ? "Fikk ikke kontakt med serveren."
                : error.message;
        }
    }


    // ---------------------------------------------------------
    // ÅPNE OG LUKKE
    // ---------------------------------------------------------

    function open(moveFocus) {
        root.classList.add("is-open");
        panel.hidden = false;
        launcher.setAttribute("aria-expanded", "true");
        dismissHint();
        save(sessionStorage, openKey, true);
        scrollToBottom();

        if (moveFocus !== false) {
            input.focus();
        }
    }

    function close() {
        root.classList.remove("is-open");
        panel.hidden = true;
        launcher.setAttribute("aria-expanded", "false");
        save(sessionStorage, openKey, false);
        launcher.focus();
    }

    function dismissHint() {
        hint.hidden = true;
        save(localStorage, hintKey, true);
    }

    function resizeInput() {
        input.style.height = "auto";
        input.style.height = Math.min(input.scrollHeight, 130) + "px";
    }


    // ---------------------------------------------------------
    // HENDELSER
    // ---------------------------------------------------------

    launcher.addEventListener("click", function () { open(true); });
    closeButton.addEventListener("click", close);

    resetButton.addEventListener("click", function () {
        if (busy) {
            return;
        }

        history = [];
        save(sessionStorage, historyKey, history);
        renderAll();
        input.focus();
    });

    hint.querySelector(".sami-hint-close").addEventListener("click", dismissHint);

    form.addEventListener("submit", function (event) {
        event.preventDefault();
        sendMessage(input.value);
    });

    input.addEventListener("keydown", function (event) {
        // Enter sender, Shift+Enter gir ny linje.
        if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
            event.preventDefault();
            sendMessage(input.value);
        }
    });

    input.addEventListener("input", resizeInput);

    panel.addEventListener("keydown", function (event) {
        if (event.key === "Escape") {
            close();
        }
    });


    // ---------------------------------------------------------
    // START
    // ---------------------------------------------------------

    renderAll();

    if (load(sessionStorage, openKey, false)) {
        // Holder panelet åpent etter sidebytte, uten å flytte fokus.
        open(false);
    } else if (!load(localStorage, hintKey, false)) {
        // Vis hilsenen litt etter at siden har lastet.
        window.setTimeout(function () {
            if (!root.classList.contains("is-open")) {
                hint.hidden = false;
            }
        }, 1200);
    }
})();
