/**
 * Felles kartoppsett for alle kart i Beredskapsportalen.
 *
 * Legger til en liten nedtrekksmeny øverst til høyre i kartet der
 * brukeren kan bytte mellom tre kart:
 *   - Vanlig kart      (OpenStreetMap)
 *   - Topografisk kart (Kartverket "topo")
 *   - Sjøkart          (Kartverket "sjokartraster")
 *   - Flybilder        (Norge i bilder, Kartverket/Geovekst)
 *
 * Bruk: kall addKartverketLayers(map) rett etter L.map(...).
 */
function addKartverketLayers(map) {

    // Vanlig kart fra OpenStreetMap
    const normalMap = L.tileLayer(
        "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
        {
            maxZoom: 19,
            attribution: "&copy; OpenStreetMap"
        }
    );


    // Kart fra Kartverket. {layer} byttes ut med navnet på kartet.
    const kartverketUrl =
        "https://cache.kartverket.no/v1/wmts/1.0.0/{layer}/default/webmercator/{z}/{y}/{x}.png";

    const kartverketOptions = {
        maxZoom: 18, // Kartverket har kartbilder ned til zoomnivå 18
        attribution: '&copy; <a href="https://www.kartverket.no/">Kartverket</a>'
    };

    const topographicMap = L.tileLayer(kartverketUrl, { ...kartverketOptions, layer: "topo" });
    const seaMap = L.tileLayer(kartverketUrl, { ...kartverketOptions, layer: "sjokartraster" });


        // Flybilder (satellitt/ortofoto) fra Esri, med stedsnavn og grenser oppå.
    // Nyttig i en krise for å se terreng, bygninger, veier og vann slik de faktisk ser ut.
    const aerialImagery = L.tileLayer(
        "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
        {
            maxZoom: 19,
            maxNativeZoom: 18,
            attribution: "Flybilder &copy; Esri, Maxar, Earthstar Geographics"
        }
    );

    const aerialLabels = L.tileLayer(
        "https://server.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}",
        {
            maxZoom: 19,
            maxNativeZoom: 18
        }
    );

    const aerialMap = L.layerGroup([aerialImagery, aerialLabels]);


    // Kartene som kan velges i menyen
    const maps = {
        "Vanlig kart": normalMap,
        "Topografisk kart": topographicMap,
        "Sjøkart": seaMap,
        "Flybilder": aerialMap
    };


    // Vanlig kart vises når siden åpnes
    let activeMap = normalMap;
    activeMap.addTo(map);


    addMapMenuStyles();


    // Nedtrekksmeny øverst til høyre i kartet
    const MapMenu = L.Control.extend({
        options: { position: "topright" },

        onAdd: function () {
            const container = L.DomUtil.create("div", "leaflet-control kart-meny");

            const select = L.DomUtil.create("select", "kart-meny-select", container);
            select.setAttribute("aria-label", "Velg karttype");

            Object.keys(maps).forEach(function (name) {
                const option = document.createElement("option");
                option.value = name;
                option.textContent = name;
                select.appendChild(option);
            });

            // Liten melding hvis flybildene ikke lastes (f.eks. tjenesten er nede).
            const notice = L.DomUtil.create("div", "kart-meny-notice", container);
            notice.hidden = true;
            let failedTiles = 0;

            aerialMap.on("tileerror", function () {
                failedTiles++;

                if (failedTiles >= 6 && activeMap === aerialMap) {
                    notice.textContent = "Flybildene kan ikke vises akkurat nå. Velg et annet kart.";
                    notice.hidden = false;
                }
            });

            aerialMap.on("tileload", function () {
                failedTiles = 0;
                notice.hidden = true;
            });

            select.addEventListener("change", function () {
                const chosen = maps[select.value];

                if (chosen && chosen !== activeMap) {
                    map.removeLayer(activeMap);
                    chosen.addTo(map);
                    activeMap = chosen;
                }

                if (chosen !== aerialMap) {
                    notice.hidden = true;
                }
            });

            // Hindrer at klikk og scrolling i menyen flytter eller zoomer kartet.
            L.DomEvent.disableClickPropagation(container);
            L.DomEvent.disableScrollPropagation(container);

            return container;
        }
    });

    new MapMenu().addTo(map);
}


/**
 * Stilen til menyen legges inn én gang fra JavaScript,
 * slik at den ser lik ut på alle sider med kart.
 */
function addMapMenuStyles() {
    if (document.getElementById("kart-meny-styles")) {
        return;
    }

    const style = document.createElement("style");
    style.id = "kart-meny-styles";
    style.textContent = `
        .leaflet-control.kart-meny {
            border: 0;
            border-radius: 8px;
            box-shadow: 0 1px 5px rgba(0, 0, 0, 0.35);
            background: white;
        }

        .kart-meny-select,
        .kart-meny-select:focus {
            width: auto;
            min-width: 0;
            min-height: 0;
            height: 32px;
            margin: 0;
            padding: 0 28px 0 10px;
            border: 1px solid transparent;
            border-radius: 8px;
            box-shadow: none;
            color: #333;
            background-color: white;
            background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath d='M3 4.5l3 3 3-3' fill='none' stroke='%23333' stroke-width='1.6' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E");
            background-repeat: no-repeat;
            background-position: right 9px center;
            font: inherit;
            font-size: 0.8rem;
            font-weight: 600;
            cursor: pointer;
            appearance: none;
            -webkit-appearance: none;
        }

        .kart-meny-select:focus-visible {
            outline: 3px solid rgba(23, 107, 135, 0.45);
            outline-offset: 1px;
        }

        .kart-meny-notice {
            max-width: 190px;
            padding: 6px 10px 8px;
            color: #9d2f2f;
            font-size: 0.74rem;
            font-weight: 600;
            line-height: 1.35;
        }

        .kart-meny-notice[hidden] {
            display: none;
        }
    `;

    document.head.appendChild(style);
}