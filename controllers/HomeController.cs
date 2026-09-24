using System.Diagnostics; // Importere navnområdet som dermed gir tilgang til activity og verktøy for log in
using Microsoft.AspNetCore.Mvc; //Bruker MVC-rammeverket
using FirstWebAppInDocker.Models; // Importere modelklasser for prosjekt models-mappe

namespace FirstWebAppInDocker.Controllers; // definere namespace og sier at denne klasser ligger her

public class HomeController : Controller //lager en offentlig klasse HomeController som arver fra Controller, gjør at den kan håndtere HTTP forspørs og return views
{
    private static List<PositionModel> resources = new List<PositionModel>(); //lager en statiske liste resources som innnholder Postionmodel objekter
//lista deles av alle instanser av HomeController, så lenge appen kjører. "MINI databse for registrering av ressurs"
    // Viser skjema-siden for å registrere ressurs
    public IActionResult RegisterResource()
    {
        return View(); // retunere standard view med samme navm som metode
    }

    // Tar imot dataene fra skjemaet
    [HttpPost] // denne versjonen av registerresource håndtere Post-forsepørsel (skjema subit
    public IActionResult RegisterResource(PositionModel model) // objektet som fylles med data fra skjemaet 
    //sjekker om validering av data fra skjemaet mdoell binding (parameter)
    {
        if (ModelState.IsValid) // sjekker om validering av modelle er ok
        {
            resources.Add(model); // legger den nye ressursen inni i lista
            return RedirectToAction("ResourceOverview"); //sender brukeren videre til siden viser oversikt over alle ressurser
        }
        return View(model); //hvis modellen ikke er gyldig, vises samme skjema-view, med modellen
    }

    // Viser oversikts-siden med alle registrerte ressurser
    public IActionResult ResourceOverview() // håndtere get til oversiktisiden
    {
        return View(resources); // sender resource lista inn til viewet som modell, slik at alle registerer resssurser kan vises html
    }

    public IActionResult Index() //idex er typsik hovesiden
    {
        return View(); // retunere idex.ctml uten modell
    }

    public IActionResult Privacy() 
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}