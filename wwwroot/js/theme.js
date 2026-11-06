// theme.js
// Bytter mellom lyseblått (standard) og marineblått (mørk modus) tema
// og husker valget i nettleseren.

(function () {

    // 1. Hent lagret valg. Lyseblått er standard.
    var saved = null;
    try {
        saved = localStorage.getItem("theme");
    } catch (e) { }

    if (saved !== "light" && saved !== "navy") {
        saved = "light";
    }

    // 2. Sett temaet med en gang, før siden tegnes (så det ikke blinker).
    document.documentElement.setAttribute("data-theme", saved);

    // Ikoner: måne (vises i lyst tema, trykk for mørkt) og sol (vises i mørkt tema)
    var SOL =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/>' +
        '<path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>';

    var MANE =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
        '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>';

    // 3. Når siden er lastet: lag knappen og legg den i menyen.
    document.addEventListener("DOMContentLoaded", function () {

        var button = document.createElement("button");
        button.type = "button";
        button.className = "theme-toggle";

        function erLyst() {
            return document.documentElement.getAttribute("data-theme") === "light";
        }

        // Viser måne i lyst tema (trykk for mørkt) og sol i mørkt tema
        function oppdaterKnapp() {
            button.innerHTML = erLyst() ? MANE : SOL;
            var tekst = erLyst() ? "Bytt til mørkt tema" : "Bytt til lyst tema";
            button.setAttribute("aria-label", tekst);
            button.title = tekst;
        }

        // 4. Ved klikk: bytt tema og lagre valget.
        button.addEventListener("click", function () {
            var neste = erLyst() ? "navy" : "light";
            document.documentElement.setAttribute("data-theme", neste);
            try {
                localStorage.setItem("theme", neste);
            } catch (e) { }
            oppdaterKnapp();
        });

        oppdaterKnapp();

        // 5. Plassering: rett før "Logg ut"-knappen eller "Logg inn"-knappen i menyen.
        //    Finnes ingen av dem, ligger knappen fast øverst til høyre.
        var anker = document.querySelector(
            'header form[action*="Logout"], header .header-login, header .nav-login-button, header .header-actions'
        );

        if (anker) {
            anker.parentNode.insertBefore(button, anker);
        } else {
            button.classList.add("theme-toggle-fixed");
            document.body.appendChild(button);
        }
    });

})();
