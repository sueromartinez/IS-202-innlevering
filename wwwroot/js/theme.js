// theme.js
// Bytter mellom lyseblått (standard) og marineblått (mørk modus) tema,
// og slår fargeblind modus av og på. Husker begge valgene i nettleseren.

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

    // Fargeblind modus er av som standard. Fargene ligger i theme.css.
    var fargeblind = false;
    try {
        fargeblind = localStorage.getItem("colorblind") === "on";
    } catch (e) { }

    if (fargeblind) {
        document.documentElement.setAttribute("data-colorblind", "on");
    }


    // =====================================================================
    // FARGEBLIND MODUS: BYTT UT ALLE RØDE OG GRØNNE FARGER
    //
    // De viktigste fargene (akutt, planlagt, ressurs, status) har faste
    // fargeblindvennlige farger i theme.css. Men mange sider har egne
    // farger skrevet rett inn i stilen sin. For at ALT rødt og grønt skal
    // byttes, går vi gjennom alle CSS-reglene på siden og lager en kopi
    // av reglene som bruker rødt eller grønt:
    //   - rødt  -> oransje (samme lyshet, litt mørkere)
    //   - grønt -> blått   (samme lyshet)
    // Lyse bakgrunner forblir lyse og mørk tekst forblir mørk, så
    // kontrasten blir omtrent som før. Det samme gjøres med farger som er
    // skrevet direkte i HTML-en (style="..."), f.eks. kartmarkørene.
    // Når modusen slås av, fjernes kopien og alt blir som før.
    // =====================================================================

    var CB_PREFIX = '[data-colorblind="on"]';

    // Faste fargeblindvennlige farger for portalens viktigste farger.
    var FASTE = {
        "#d34b4b": "#c2410c",
        "#c63d3d": "#c2410c",
        "#23855b": "#0b4f8a"
    };

    var NAVNEFARGER = {
        red: "#ff0000", darkred: "#8b0000", crimson: "#dc143c", firebrick: "#b22222",
        indianred: "#cd5c5c", tomato: "#ff6347", green: "#008000", darkgreen: "#006400",
        forestgreen: "#228b22", seagreen: "#2e8b57", limegreen: "#32cd32", lime: "#00ff00",
        mediumseagreen: "#3cb371"
    };

    function hexTilRgb(hex) {
        hex = hex.replace("#", "");
        if (hex.length === 3 || hex.length === 4) {
            hex = hex.split("").map(function (c) { return c + c; }).join("");
        }
        return {
            r: parseInt(hex.substr(0, 2), 16),
            g: parseInt(hex.substr(2, 2), 16),
            b: parseInt(hex.substr(4, 2), 16),
            a: hex.length === 8 ? parseInt(hex.substr(6, 2), 16) / 255 : 1
        };
    }

    function rgbTilHsl(r, g, b) {
        r /= 255; g /= 255; b /= 255;
        var max = Math.max(r, g, b), min = Math.min(r, g, b);
        var h = 0, s = 0, l = (max + min) / 2;
        if (max !== min) {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max === r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max === g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return { h: h, s: s, l: l };
    }

    function hslTilRgb(h, s, l) {
        function f(n) {
            var k = (n + h / 30) % 12;
            var a = s * Math.min(l, 1 - l);
            return Math.round(255 * (l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1))));
        }
        return { r: f(0), g: f(8), b: f(4) };
    }

    function tilHex(c) {
        return "#" + [c.r, c.g, c.b].map(function (v) {
            return ("0" + v.toString(16)).slice(-2);
        }).join("");
    }

    // Gir ny farge for rødt og grønt, eller null hvis fargen kan beholdes.
    function byttFarge(c) {
        var hex = tilHex(c);
        if (FASTE[hex]) {
            var fast = hexTilRgb(FASTE[hex]);
            fast.a = c.a;
            return fast;
        }

        var hsl = rgbTilHsl(c.r, c.g, c.b);

        // Grå, nesten hvite og nesten svarte farger er trygge.
        if (hsl.s < 0.2 || hsl.l > 0.97 || hsl.l < 0.06) {
            return null;
        }

        var ny;

        if (hsl.h < 18 || hsl.h >= 330) {
            // Rødt og rosa -> oransje. Mørkere, så hvit tekst fortsatt syns.
            ny = hslTilRgb(22, Math.max(hsl.s, 0.6), hsl.l > 0.85 ? hsl.l : hsl.l * 0.9);
        } else if (hsl.h >= 75 && hsl.h < 175) {
            // Grønt og blågrønt -> blått.
            ny = hslTilRgb(212, hsl.s, hsl.l);
        } else {
            return null;
        }

        ny.a = c.a;
        return ny;
    }

    function skrivFarge(c) {
        return c.a >= 1
            ? tilHex(c)
            : "rgba(" + c.r + ", " + c.g + ", " + c.b + ", " + Math.round(c.a * 1000) / 1000 + ")";
    }

    // Heksfarger må ikke etterfølges av bokstav eller bindestrek, ellers ville
    // f.eks. «url(#face-bronse)» (en henvisning i SVG) blitt tolket som fargen #face.
    var FARGE_REGEX = new RegExp(
        "#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})(?![\\w-])" +
        "|rgba?\\(\\s*\\d+(?:\\.\\d+)?%?\\s*[, ]\\s*\\d+(?:\\.\\d+)?%?\\s*[, ]\\s*\\d+(?:\\.\\d+)?%?\\s*(?:[,/]\\s*[\\d.]+%?\\s*)?\\)" +
        // Fargenavn, men ikke inne i ord eller filnavn (f.eks. «green-bg.png»).
        "|(?<![\\w/.-])(?:" + Object.keys(NAVNEFARGER).join("|") + ")(?![\\w.-])",
        "gi"
    );

    function lesFarge(tekst) {
        var t = tekst.toLowerCase();
        if (NAVNEFARGER[t]) return hexTilRgb(NAVNEFARGER[t]);
        if (t.charAt(0) === "#") return hexTilRgb(t);

        var tall = t.replace(/rgba?\(|\)/g, "").split(/[\s,\/]+/).filter(Boolean);
        function kanal(v) {
            return v.indexOf("%") > -1 ? Math.round(parseFloat(v) * 2.55) : Math.round(parseFloat(v));
        }
        var alfa = tall.length > 3
            ? (tall[3].indexOf("%") > -1 ? parseFloat(tall[3]) / 100 : parseFloat(tall[3]))
            : 1;
        return { r: kanal(tall[0]), g: kanal(tall[1]), b: kanal(tall[2]), a: alfa };
    }

    // Bytter alle røde og grønne farger i en CSS-verdi. Gir null hvis ingenting endres.
    function byttVerdi(verdi) {
        if (!verdi) return null;
        var endret = false;

        function byttDel(del) {
            return del.replace(FARGE_REGEX, function (treff) {
                var farge = lesFarge(treff);
                if (!farge || isNaN(farge.r)) return treff;
                var byttet = byttFarge(farge);
                if (!byttet) return treff;
                endret = true;
                return skrivFarge(byttet);
            });
        }

        // Alt inne i url(...) er lenker og filnavn, ikke farger, og røres ikke.
        var ny = verdi.split(/(url\([^)]*\))/i).map(function (del) {
            return /^url\(/i.test(del) ? del : byttDel(del);
        }).join("");

        return endret ? ny : null;
    }

    function delSelektor(selektor) {
        // Deler på komma, men ikke komma inne i parenteser som :is(a, b).
        var deler = [], dybde = 0, start = 0;
        for (var i = 0; i < selektor.length; i++) {
            var c = selektor.charAt(i);
            if (c === "(") dybde++;
            else if (c === ")") dybde--;
            else if (c === "," && dybde === 0) {
                deler.push(selektor.slice(start, i).trim());
                start = i + 1;
            }
        }
        deler.push(selektor.slice(start).trim());
        return deler.filter(Boolean);
    }

    // Lager selektoren for kopien: gjelder bare når fargeblind modus er på.
    function cbSelektor(del) {
        if (/^html\b/i.test(del)) return del.replace(/^html/i, "html" + CB_PREFIX);
        if (/^:root\b/i.test(del)) return del.replace(/^:root/i, ":root" + CB_PREFIX);
        return CB_PREFIX + " " + del;
    }

    function lagRegler(regler, foreldre, ut) {
        for (var i = 0; i < regler.length; i++) {
            var regel = regler[i];

            if (regel.type === 4 && regel.cssRules) {
                // @media
                var indre = [];
                lagRegler(regel.cssRules, foreldre, indre);
                if (indre.length) ut.push("@media " + regel.media.mediaText + " {" + indre.join("\n") + "}");
                continue;
            }

            if (regel.type === 12 && regel.cssRules) {
                // @supports
                var stotte = [];
                lagRegler(regel.cssRules, foreldre, stotte);
                if (stotte.length) ut.push("@supports " + regel.conditionText + " {" + stotte.join("\n") + "}");
                continue;
            }

            if (!regel.selectorText || !regel.style) continue;

            // Reglene for fargeblind modus i theme.css er allerede riktige.
            if (regel.selectorText.indexOf("data-colorblind") > -1) continue;

            // CSS-nesting (brukes i theme.css): sett sammen med forelderen.
            var deler = delSelektor(regel.selectorText);
            if (foreldre) {
                var fulle = [];
                foreldre.forEach(function (forelder) {
                    deler.forEach(function (del) {
                        fulle.push(del.indexOf("&") > -1 ? del.replace(/&/g, forelder) : forelder + " " + del);
                    });
                });
                deler = fulle;
            }

            var erklaringer = [];
            for (var j = 0; j < regel.style.length; j++) {
                var navn = regel.style[j];
                var verdi = regel.style.getPropertyValue(navn);
                var ny = byttVerdi(verdi);
                if (ny) {
                    var viktig = regel.style.getPropertyPriority(navn) === "important" ? " !important" : "";
                    erklaringer.push(navn + ": " + ny + viktig + ";");
                }
            }

            if (erklaringer.length) {
                ut.push(deler.map(cbSelektor).join(",\n") + " {" + erklaringer.join(" ") + "}");
            }

            if (regel.cssRules && regel.cssRules.length) {
                lagRegler(regel.cssRules, deler, ut);
            }
        }
    }

    function byggFargeblindStil() {
        var ut = [];

        for (var i = 0; i < document.styleSheets.length; i++) {
            var ark = document.styleSheets[i];
            if (ark.ownerNode && ark.ownerNode.id === "fargeblind-stil") continue;

            var regler;
            try {
                regler = ark.cssRules;
            } catch (e) {
                // Stilark fra andre nettsteder (f.eks. Leaflet) kan ikke leses. De har ikke rødt/grønt.
                continue;
            }
            if (regler) lagRegler(regler, null, ut);
        }

        var stil = document.getElementById("fargeblind-stil");
        if (!stil) {
            stil = document.createElement("style");
            stil.id = "fargeblind-stil";
        }
        stil.textContent = ut.join("\n");
        document.head.appendChild(stil);
    }

    // Farger skrevet rett i HTML-en: style="...", fill="..." og stroke="..."
    function byttElement(el) {
        if (el.nodeType !== 1 || el.hasAttribute("data-cb-original")) return;

        var original = {};
        var endret = false;

        ["style", "fill", "stroke"].forEach(function (attr) {
            var verdi = el.getAttribute(attr);
            var ny = byttVerdi(verdi);
            if (ny) {
                original[attr] = verdi;
                el.setAttribute(attr, ny);
                endret = true;
            }
        });

        if (endret) {
            el.setAttribute("data-cb-original", JSON.stringify(original));
        }
    }

    function byttInnhold(rot) {
        if (rot.nodeType !== 1) return;
        byttElement(rot);
        var alle = rot.querySelectorAll("[style], [fill], [stroke]");
        for (var i = 0; i < alle.length; i++) byttElement(alle[i]);
    }

    function gjenopprettInnhold() {
        var alle = document.querySelectorAll("[data-cb-original]");
        for (var i = 0; i < alle.length; i++) {
            var el = alle[i];
            try {
                var original = JSON.parse(el.getAttribute("data-cb-original"));
                Object.keys(original).forEach(function (attr) { el.setAttribute(attr, original[attr]); });
            } catch (e) { }
            el.removeAttribute("data-cb-original");
        }
    }

    var observator = null;

    function startFargeblind() {
        byggFargeblindStil();
        byttInnhold(document.body);

        if (!observator && window.MutationObserver) {
            // Kart og lister som lages etterpå (f.eks. kartmarkører) får også nye farger.
            observator = new MutationObserver(function (endringer) {
                endringer.forEach(function (endring) {
                    for (var i = 0; i < endring.addedNodes.length; i++) byttInnhold(endring.addedNodes[i]);
                });
            });
            observator.observe(document.body, { childList: true, subtree: true });
        }
    }

    function stoppFargeblind() {
        if (observator) {
            observator.disconnect();
            observator = null;
        }
        gjenopprettInnhold();
        var stil = document.getElementById("fargeblind-stil");
        if (stil) stil.parentNode.removeChild(stil);
    }

    // Lar andre skript slå modusen av og på (brukes ikke ennå, men nyttig for tester).
    window.fargeblindModus = { start: startFargeblind, stopp: stoppFargeblind, byttVerdi: byttVerdi };


    // Ikoner: måne (vises i lyst tema, trykk for mørkt) og sol (vises i mørkt tema)
    var SOL =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="4"/>' +
        '<path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>';

    var MANE =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
        '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>';

    // Ikon for fargeblind modus: et øye der regnbuehinnen er halvt fylt,
    // som viser at det handler om syn og farger.
    var OYE =
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
        '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/>' +
        '<circle cx="12" cy="12" r="3.2"/>' +
        '<path d="M12 8.8a3.2 3.2 0 0 1 0 6.4z" fill="currentColor" stroke="none"/></svg>';

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

        // 6. Knapp for fargeblind modus, rett ved siden av sol/måne.
        var cbButton = document.createElement("button");
        cbButton.type = "button";
        cbButton.className = "theme-toggle colorblind-toggle";
        cbButton.innerHTML = OYE;

        function erFargeblind() {
            return document.documentElement.getAttribute("data-colorblind") === "on";
        }

        function oppdaterFargeblindKnapp() {
            var pa = erFargeblind();
            var tekst = pa ? "Slå av fargeblind modus" : "Slå på fargeblind modus";
            cbButton.setAttribute("aria-pressed", pa ? "true" : "false");
            cbButton.setAttribute("aria-label", tekst);
            cbButton.title = tekst;
        }

        cbButton.addEventListener("click", function () {
            var pa = !erFargeblind();

            if (pa) {
                document.documentElement.setAttribute("data-colorblind", "on");
                startFargeblind();
            } else {
                document.documentElement.removeAttribute("data-colorblind");
                stoppFargeblind();
            }

            try {
                localStorage.setItem("colorblind", pa ? "on" : "off");
            } catch (e) { }

            oppdaterFargeblindKnapp();
        });

        oppdaterFargeblindKnapp();

        if (erFargeblind()) {
            startFargeblind();

            // Stilark som lastes litt senere tas med når hele siden er ferdig.
            window.addEventListener("load", function () {
                if (erFargeblind()) byggFargeblindStil();
            });
        }

        if (anker) {
            anker.parentNode.insertBefore(button, anker);
            anker.parentNode.insertBefore(cbButton, anker);
        } else {
            button.classList.add("theme-toggle-fixed");
            cbButton.classList.add("theme-toggle-fixed");
            document.body.appendChild(button);
            document.body.appendChild(cbButton);
        }
    });

})();
