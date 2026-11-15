// Utvider det eksisterende kartet uten å lage nye kart eller endre behovsmarkørene.
window.addNeedFullscreenControl = function (map) {
    const mapElement = map.getContainer();
    const dialog = document.createElement("dialog");
    dialog.className = "need-fullscreen-dialog";
    dialog.setAttribute("aria-label", "Behovskart i fullskjerm");
    document.body.appendChild(dialog);
    let placeholder;
    let button;
    const expandIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M9 3H3v6m12-6h6v6M3 15v6h6m12-6v6h-6"/></svg>';
    const collapseIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M3 9h6V3m6 0v6h6M9 21v-6H3m18 0h-6v6"/></svg>';

    function isFullscreen() {
        return document.fullscreenElement === mapElement || dialog.open;
    }

    // Leaflet får ny størrelse mens senter, zoom, kartlag og markører beholdes.
    function refreshMapSize() {
        const center = map.getCenter();
        const zoom = map.getZoom();
        map.invalidateSize({ pan: false });
        map.setView(center, zoom, { animate: false, reset: true });
    }

    function updateControl() {
        const expanded = isFullscreen();
        const label = expanded ? "Avslutt fullskjerm" : "Vis i fullskjerm";
        button.title = label;
        button.setAttribute("aria-label", label);
        button.setAttribute("aria-pressed", String(expanded));
        button.innerHTML = expanded ? collapseIcon : expandIcon;
        requestAnimationFrame(refreshMapSize);
    }

    // Dialog er reserve for mobilnettlesere uten støtte for vanlig fullskjerm.
    function openDialog() {
        placeholder = document.createComment("Kartets opprinnelige plassering");
        mapElement.before(placeholder);
        dialog.appendChild(mapElement);
        dialog.showModal();
        updateControl();
        button.focus();
    }

    dialog.addEventListener("close", function () {
        if (placeholder) {
            placeholder.replaceWith(mapElement);
            placeholder = null;
        }
        updateControl();
        button.focus({ preventScroll: true });
    });
    document.addEventListener("fullscreenchange", updateControl);

    // Både Escape og lukkeknappen fører samme kart tilbake til skjemaet.
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape" || !isFullscreen()) return;
        event.preventDefault();
        if (dialog.open) dialog.close();
        else document.exitFullscreen();
    });

    const FullscreenControl = L.Control.extend({
        options: { position: "topright" },
        onAdd: function () {
            const control = L.DomUtil.create("div", "leaflet-bar need-fullscreen-control");
            button = L.DomUtil.create("button", "", control);
            button.type = "button";
            L.DomEvent.disableClickPropagation(control);
            L.DomEvent.disableScrollPropagation(control);
            button.addEventListener("click", async function (event) {
                // Hindrer at åpning eller lukking også plasserer en behovsmarkør.
                event.preventDefault();
                event.stopPropagation();
                if (dialog.open) dialog.close();
                else if (document.fullscreenElement === mapElement) await document.exitFullscreen();
                else if (mapElement.requestFullscreen && document.fullscreenEnabled) {
                    try { await mapElement.requestFullscreen(); }
                    catch { openDialog(); }
                } else openDialog();
            });
            return control;
        }
    });
    new FullscreenControl().addTo(map);
    updateControl();

    // Tilpasser kartet når mobilbredden eller fullskjermstørrelsen endres.
    if (typeof ResizeObserver !== "undefined") {
        const sizeObserver = new ResizeObserver(refreshMapSize);
        sizeObserver.observe(mapElement);
    }
};
