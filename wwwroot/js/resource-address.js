// Kobler adressefeltene til samme kart og lagrer samlet adresse i det eksisterende feltet.
function setupResourceAddress(map, onAddressSelected) {
    const street = document.getElementById('StreetAddress');
    const postal = document.getElementById('PostalLocation');
    const municipality = document.getElementById('Municipality');
    const address = document.getElementById('Address');
    const status = document.getElementById('addressLookupStatus');
    const suggestions = document.getElementById('addressSuggestions');
    let timer, controller, version = 0, lastAutomaticAddress = null;

    function closeSuggestions() {
        suggestions.replaceChildren();
        suggestions.hidden = true;
        street.setAttribute('aria-expanded', 'false');
    }

    function cancelRequest() {
        clearTimeout(timer);
        version++;
        if (controller) controller.abort();
    }

    function syncAddress() {
        const first = [street.value.trim(), postal.value.trim()].filter(Boolean).join(', ');
        address.value = [first, municipality.value.trim() ? municipality.value.trim() + ' kommune' : ''].filter(Boolean).join(' – ');
        street.setCustomValidity(address.value.length > 200 ? 'Den samlede adressen kan ha maksimalt 200 tegn.' : '');
    }

    function fillAddress(item) {
        street.value = item.adressetekst || '';
        postal.value = [item.postnummer, item.poststed].filter(Boolean).join(', ');
        municipality.value = item.kommunenavn || '';
        syncAddress();
        lastAutomaticAddress = address.value;
    }

    function validAddress(item) {
        return item.adressetekst && /^\d{4}$/.test(item.postnummer) && item.poststed && item.kommunenavn;
    }

    // Avbrudd og løpenummer hindrer at et gammelt svar overskriver et nyere valg eller manuell retting.
    async function request(url, currentVersion) {
        controller = new AbortController();
        const currentController = controller;
        const timeout = setTimeout(() => currentController.abort(), 10000);
        try {
            const response = await fetch(url, { signal: currentController.signal });
            if (!response.ok) throw new Error('Adresseoppslag feilet');
            const data = await response.json();
            return currentVersion === version ? data.adresser || [] : null;
        } finally {
            clearTimeout(timeout);
        }
    }

    // Forslag viser også poststed og kommune, slik at like gatenavn kan skilles fra hverandre.
    async function search(currentVersion) {
        status.textContent = 'Søker etter adresser ...';
        const query = new URLSearchParams({ sok: street.value.trim(), treffPerSide: '8', utkoordsys: '4326' });
        try {
            const items = await request('https://api.kartverket.no/adresser/v1/sok?' + query, currentVersion);
            if (!items) return;
            closeSuggestions();
            for (const item of items) {
                const point = item.representasjonspunkt;
                if (!validAddress(item) || !point || !Number.isFinite(point.lat) || !Number.isFinite(point.lon)) continue;
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'resource-address-option';
                button.setAttribute('role', 'option');
                button.textContent = `${item.adressetekst}, ${item.postnummer} ${item.poststed} – ${item.kommunenavn} kommune`;
                button.addEventListener('click', () => {
                    cancelRequest();
                    fillAddress(item);
                    closeSuggestions();
                    status.textContent = 'Adresse valgt. Du kan korrigere feltene manuelt.';
                    onAddressSelected(point.lat, point.lon);
                });
                suggestions.append(button);
            }
            suggestions.hidden = !suggestions.childElementCount;
            street.setAttribute('aria-expanded', String(!suggestions.hidden));
            status.textContent = suggestions.hidden ? 'Ingen registrert adresse funnet. Du kan skrive inn adresse eller område manuelt.' : 'Velg riktig adresse fra forslagene for å plassere markøren.';
        } catch {
            if (currentVersion === version) status.textContent = 'Kunne ikke hente adresser. Du kan fylle ut feltene manuelt.';
        }
    }

    for (const field of [street, postal, municipality]) {
        field.addEventListener('input', () => {
            cancelRequest();
            closeSuggestions();
            syncAddress();
            lastAutomaticAddress = null;
            status.textContent = 'Manuell adresse. Koordinatene beholdes til du velger en adresse eller klikker i kartet.';
            if (field === street && street.value.trim().length >= 3) {
                const currentVersion = version;
                timer = setTimeout(() => search(currentVersion), 350);
            }
        });
    }
    street.addEventListener('keydown', event => {
        if (event.key === 'ArrowDown' && !suggestions.hidden) {
            event.preventDefault();
            suggestions.firstElementChild?.focus();
        }
        if (event.key === 'Escape') closeSuggestions();
    });
    suggestions.addEventListener('keydown', event => {
        if (event.key === 'Escape') { closeSuggestions(); street.focus(); }
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();
            (event.key === 'ArrowDown' ? document.activeElement.nextElementSibling : document.activeElement.previousElementSibling)?.focus();
        }
    });

    // Kartklikk beholder de nøyaktige koordinatene og viser nærmeste adresse som et forslag innen 100 meter.
    async function lookup(latitude, longitude) {
        cancelRequest();
        closeSuggestions();
        const currentVersion = version;
        // Fjerner et gammelt automatisk forslag ved nytt kartpunkt, men beholder manuell tekst.
        if (lastAutomaticAddress !== null && address.value === lastAutomaticAddress) {
            street.value = ''; postal.value = ''; municipality.value = '';
            syncAddress();
        }
        lastAutomaticAddress = null;
        status.textContent = 'Henter adresse ...';
        const query = new URLSearchParams({ lat: latitude, lon: longitude, radius: '100', koordsys: '4326', treffPerSide: '10' });
        try {
            const items = await request('https://api.kartverket.no/adresser/v1/punktsok?' + query, currentVersion);
            if (!items) return;
            const nearest = items.filter(item => validAddress(item) && Number.isFinite(item.meterDistanseTilPunkt)
                && item.meterDistanseTilPunkt >= 0 && item.meterDistanseTilPunkt <= 100)
                .sort((a, b) => a.meterDistanseTilPunkt - b.meterDistanseTilPunkt)[0];
            if (nearest) {
                fillAddress(nearest);
                status.textContent = `Adresseforslag, omtrent ${Math.round(nearest.meterDistanseTilPunkt)} meter fra markøren. Kontroller og korriger ved behov.`;
            } else {
                status.textContent = 'Ingen registrert adresse funnet. Koordinatene beholdes. Du kan skrive inn adresse eller område manuelt.';
            }
        } catch {
            if (currentVersion === version) status.textContent = 'Kunne ikke hente adresse. Kontroller feltene manuelt. Koordinatene er beholdt.';
        }
    }

    // Beholder en eldre samlet adresse dersom skjemaet kommer tilbake med valideringsfeil.
    if (!street.value && !postal.value && !municipality.value && address.value) street.value = address.value;
    syncAddress();
    return { lookup };
}
