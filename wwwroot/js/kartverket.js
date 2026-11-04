/**
 * Felles kartoppsett for alle kart i Beredskapsportalen.
Henter kart fra Kartverket (Vanlig og topografisk kart) og legger til en knapp for å bytte mellom de to kartene.
 */
function addKartverketLayers(map) {

    // {layer} byttes ut med navnet på kartet (topo / toporaster)
    const url =
        "https://cache.kartverket.no/v1/wmts/1.0.0/{layer}/default/webmercator/{z}/{y}/{x}.png";

    const options = {
        maxZoom: 18, // Kartverket har kartbilder ned til zoomnivå 18
        attribution: '&copy; <a href="https://www.kartverket.no/">Kartverket</a>'
    };

    const normalMap = L.tileLayer(url, { ...options, layer: "topo" });
    const topographicMap = L.tileLayer(url, { ...options, layer: "toporaster" });
    const seaMap = L.tileLayer(url, { ...options, layer: "sjokartraster" });

    // Vanlig kart vises når siden åpnes
    normalMap.addTo(map);

    // Valgknapp øverst til høyre i kartet
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