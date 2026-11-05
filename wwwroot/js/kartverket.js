/**
 * Felles kartoppsett for alle kart i Beredskapsportalen.
 *
 * Legger til en valgboks øverst til høyre i kartet der brukeren
 * kan bytte mellom tre kart:
 *   - Vanlig kart      (OpenStreetMap)
 *   - Topografisk kart (Kartverket "topo")
 *   - Sjøkart          (Kartverket "sjokartraster")
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


    // Vanlig kart vises når siden åpnes
    normalMap.addTo(map);


    // Valgboks øverst til høyre i kartet
    L.control.layers(
        {
            "Vanlig kart": normalMap,
            "Topografisk kart": topographicMap,
            "Sjøkart": seaMap
        },
        null,
        { collapsed: false }
    ).addTo(map);
}