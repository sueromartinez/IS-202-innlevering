/*
    AssignmentService.cs – reglene for forespørsler

    Hva filen gjør: Alt som skjer med en forespørsel etter at den er sendt:
    - RequestAsync: offentlig aktør spør om en ressurs. Behovet blir «Til vurdering», og eieren
      får varsel.
    - RespondAsync: den frivillige bekrefter eller avslår. Ved bekreftelse blir ressursen
      Opptatt.
    - CancelAsync: forespørselen avbrytes.
    - ResolveNeedAsync: behovet er løst. Ressursene blir ledige igjen, og eierne får takk.
    Reglene ligger her og ikke i controllerne, så nettsiden, Sami og testene bruker nøyaktig de
    samme reglene. Det vanskeligste er når to personer klikker samtidig.

    På nettsiden: Det som skjer når man trykker «Send forespørsel», «Bekreft», «Avslå», «Avbryt»
    og «Marker som løst».

    Oppgavekrav: 10
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FirstWebAppInDocker.Services.Matching;

/*
    Svaret fra tjenesten: gikk det bra, og en melding som kan vises rett til brukeren.

    Representerer resultatet fra en tjenesteoperasjon.

    Success:
    Angir om operasjonen ble gjennomført.

    Message:
    En melding som kan vises til brukeren.
*/
public record ServiceResult(bool Success, string Message);

/*
    Håndterer forespørsler og tildelinger mellom behov
    og ressurser i Beredskapsportalen.

    Tjenesten håndterer statusflyten:

    Ny → Vurderes → Tildelt → Løst.

    Ressurser blir opptatt ved bekreftet tildeling
    og frigjøres når tildelingen avsluttes.

    Ved bekreftelse brukes en atomisk databaseoppdatering
    og transaksjon for å forhindre dobbelbooking.

    Samtidighetskonflikter håndteres gjennom EF Core
    sin optimistiske samtidighetskontroll.

    Kontrollerne må fortsatt kontrollere at brukeren
    har tilgang til å utføre handlingene.
*/
public class AssignmentService
{
    #region Felt og konstruktør

    private readonly ApplicationDbContext _context;

    private readonly NotificationService _notifications;

    /*
        Oppretter tjenesten med database- og varslingstjenester.

        context:
        Applikasjonens databasekontekst.

        notifications:
        Tjenesten som sender varsler til brukerne.
    */
    public AssignmentService(ApplicationDbContext context, NotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    #endregion

    #region Send forespørsel

    /*
        Send forespørsel. Alt sjekkes på serveren: behovet finnes og er ikke løst, ressursen
        finnes, typen passer, den er ledig, den er ikke spurt fra før, og den er ikke booket et
        annet sted.

        Sender en forespørsel om en tilgjengelig ressurs
        til et registrert behov.

        needId: Behovets identifikator.
        resourceId: Ressursens identifikator.
        requestedByUserId:
        Identity-ID til brukeren som sender forespørselen.

        message:
        Valgfri melding til ressursleverandøren.

        suggestedByAssistant:
        Angir om ressursen ble foreslått av Sami.

        Returnerer:
        Resultat med informasjon om forespørselen ble opprettet.
    */
    public async Task<ServiceResult> RequestAsync(
        int needId,
        int resourceId,
        string requestedByUserId,
        string? message,
        bool suggestedByAssistant)
    {
        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            return new ServiceResult(false, "Du må være logget inn for å sende en forespørsel.");
        }

        Need? need = await _context.Needs
            .FirstOrDefaultAsync(n => n.Id == needId);

        if (need is null)
        {
            return new ServiceResult(false, "Fant ikke behovet.");
        }

        if (need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(false, "Behovet er allerede løst.");
        }

        Resource? resource = await _context.Resources
            .FirstOrDefaultAsync(r => r.Id == resourceId);

        if (resource is null)
        {
            return new ServiceResult(false, "Fant ikke ressursen.");
        }

        if (resource.ResourceType != need.ResourceType)
        {
            return new ServiceResult(false, "Ressurstypen passer ikke til behovet.");
        }

        if (resource.Status != ResourceStatus.Available)
        {
            return new ServiceResult(false, "Ressursen er ikke tilgjengelig akkurat nå.");
        }

        bool alreadyRequested =
            await _context.NeedAssignments.AnyAsync(assignment =>
                assignment.NeedId == needId
                && assignment.ResourceId == resourceId
                && (
                    assignment.Status == AssignmentStatus.Requested
                    || assignment.Status == AssignmentStatus.Confirmed));

        if (alreadyRequested)
        {
            return new ServiceResult(false, "Denne ressursen er allerede spurt om dette behovet.");
        }

        if (await HasOtherActiveConfirmationAsync(resourceId))
        {
            return new ServiceResult(false, "Ressursen er allerede tildelt et annet behov.");
        }

        _context.NeedAssignments.Add(new NeedAssignment
        {
            NeedId = needId,
            ResourceId = resourceId,
            RequestedByUserId = requestedByUserId,
            Message = string.IsNullOrWhiteSpace(message)
                ? null
                : message.Trim(),
            SuggestedByAssistant = suggestedByAssistant,
            Status = AssignmentStatus.Requested,
            CreatedAt = DateTime.UtcNow
        });

        /*
            Første forespørsel flytter behovet fra «Nytt» til «Til vurdering» på
            Koordinering-tavla.
        */
        if (need.Status == NeedStatus.New)
        {
            need.Status = NeedStatus.UnderReview;
        }

        await _context.SaveChangesAsync();

        if (string.IsNullOrWhiteSpace(resource.OwnerUserId))
        {
            return new ServiceResult(
                true,
                "Forespørselen er lagret, men ressursen er ikke " +
                "koblet til en konto. Kontakt eieren direkte " +
                "via kontaktinformasjonen.");
        }

        string? phone =
            string.IsNullOrWhiteSpace(resource.ContactPhoneNumber)
                ? null
                : $"{resource.ContactPhoneCountryCode}{resource.ContactPhoneNumber}";

        /*
            Eieren får varsel i portalen og på e-post (og simulert SMS). Varselet sendes etter
            at alt er lagret, så vi aldri varsler om noe som ikke ble lagret.
        */
        await _notifications.NotifyAsync(
            resource.OwnerUserId,
            $"Ressursen din er ønsket: {resource.ResourceName}",
            $"Et behov i nærheten trenger «{resource.ResourceName}». " +
            $"Behov: {need.Description}. Gå til Forespørsler " +
            "for å bekrefte om du kan stille.",
            "/Request",
            phone);

        return new ServiceResult(true, "Forespørsel sendt. Eieren har fått varsel og må bekrefte.");
    }

    #endregion

    #region Svar på forespørsel

    /*
        Den frivillige svarer. Ved «ja» må to ting skje sammen: ressursen blir Opptatt og
        forespørselen blir Bekreftet. Derfor brukes en transaksjon: enten skjer begge, eller
        ingen.

        Lar ressursleverandøren bekrefte eller avslå
        en forespørsel om en ressurs.

        Ved bekreftelse opprettes en databasetransaksjon
        dersom databasen er relasjonell.

        Ressursen reserveres atomisk ved å endre
        status fra Available til Busy.

        EF Core kontrollerer samtidig at tildelingsstatusen
        ikke er endret av en annen operasjon.

        Ved samtidighetskonflikt rulles transaksjonen
        tilbake, og brukeren får en forståelig melding.

        Varsler sendes først etter fullført lagring.

        assignmentId:
        Identifikatoren til forespørselen.

        userId:
        Identity-ID til ressursleverandøren.

        accept:
        True for bekreftelse, false for avslag.

        Returnerer: Resultatet av svarhandlingen.
    */
    public async Task<ServiceResult> RespondAsync(int assignmentId, string userId, bool accept)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ServiceResult(false, "Fant ikke forespørselen.");
        }

        /*
            Bekreftelser bruker en eksplisitt transaksjon
            mot relasjonelle databaser.

            InMemory støtter ikke relasjonelle transaksjoner.
        */
        await using IDbContextTransaction? transaction =
            accept && _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync()
                : null;

        NeedAssignment? assignment =
            await _context.NeedAssignments
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

        /*
            Bare eieren av ressursen kan svare. Andre får samme melding som om forespørselen
            ikke fantes.
        */
        if (assignment?.Resource is null
            || assignment.Need is null
            || string.IsNullOrWhiteSpace(assignment.Resource.OwnerUserId)
            || !string.Equals(assignment.Resource.OwnerUserId, userId, StringComparison.Ordinal))
        {
            return new ServiceResult(false, "Fant ikke forespørselen.");
        }

        if (assignment.Status != AssignmentStatus.Requested)
        {
            return new ServiceResult(false, "Denne forespørselen er allerede besvart.");
        }

        if (assignment.Need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(
                false,
                "Behovet er allerede løst. " +
                "Forespørselen kan ikke lenger besvares.");
        }

        if (accept)
        {
            if (assignment.Resource.Status != ResourceStatus.Available)
            {
                return new ServiceResult(false, "Ressursen er ikke lenger tilgjengelig.");
            }

            bool alreadyAssigned =
                await HasOtherActiveConfirmationAsync(
                    assignment.ResourceId,
                    excludedAssignmentId: assignment.Id);

            if (alreadyAssigned)
            {
                return new ServiceResult(false, "Ressursen er allerede tildelt et annet behov.");
            }

            /*
                Reserverer ressursen i én enkelt databaseoperasjon: "sett til Opptatt, men bare
                hvis den fortsatt er Tilgjengelig". Bekrefter to personer samtidig, er det bare
                den første som lykkes. Den andre får en melding.
            */
            bool reserved = await TryMarkResourceBusyAsync(assignment.Resource);

            if (!reserved)
            {
                return new ServiceResult(
                    false,
                    "Ressursen ble nettopp reservert av " +
                    "en annen forespørsel. " +
                    "Oppdater siden og prøv en annen ressurs.");
            }

            assignment.Status = AssignmentStatus.Confirmed;
        }
        else
        {
            assignment.Status = AssignmentStatus.Declined;
        }

        assignment.RespondedAt = DateTime.UtcNow;

        // Behovets status regnes ut på nytt ut fra forespørslene.
        await UpdateNeedStatusAsync(assignment.Need);

        #region Lagre med samtidighetskontroll

        try
        {
            /*
                Status er konfigurert som concurrency token
                i ApplicationDbContext.

                EF Core kontrollerer dermed om forespørselen
                fortsatt har samme status som da vi leste den.
            */
            await _context.SaveChangesAsync();

            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }
        }
        /*
            Noen andre endret forespørselen etter at vi leste den. Vi angrer og ber brukeren
            laste siden på nytt, i stedet for å overskrive.
        */
        catch (DbUpdateConcurrencyException)
        {
            /*
                NYTT:
                En annen operasjon har endret forespørselen
                etter at vi leste den.

                Hvis ressursen allerede er satt til Busy
                gjennom ExecuteUpdateAsync, må dette
                rulles tilbake sammen med tildelingen.
            */
            if (transaction is not null)
            {
                await transaction.RollbackAsync();
            }

            /*
                Fjern utdaterte entiteter fra change tracker.
                Neste operasjon kan dermed laste ferske data.
            */
            _context.ChangeTracker.Clear();

            return new ServiceResult(
                false,
                "Forespørselen ble endret av en annen bruker " +
                "mens du behandlet den. " +
                "Oppdater siden for å se gjeldende status.");
        }

        #endregion

        // Varsler sendes først etter vellykket lagring.
        await _notifications.NotifyAsync(
            assignment.RequestedByUserId,
            accept
                ? $"Bekreftet: {assignment.Resource.ResourceName}"
                : $"Avslått: {assignment.Resource.ResourceName}",
            accept
                ? $"Eieren har bekreftet at «{assignment.Resource.ResourceName}» " +
                  $"kan stille til behovet «{assignment.Need.Description}»."
                : $"Eieren kan ikke stille med «{assignment.Resource.ResourceName}». " +
                  "Finn gjerne en annen ressurs.",
            $"/Match/Need/{assignment.NeedId}");

        return new ServiceResult(
            true,
            accept
                ? "Takk! Du har bekreftet at ressursen stiller."
                : "Forespørselen er avslått.");
    }

    #endregion

    #region Avbryt forespørsel

    /*
        Avbryt. Var forespørselen bekreftet, blir ressursen ledig igjen.

        Avbryter en aktiv forespørsel og frigjør ressursen
        dersom den ikke er bekreftet for et annet aktivt behov.

        Forespørselen markeres som Cancelled for å beholde
        historikken.

        En kansellert forespørsel regnes ikke som aktiv
        og skal ikke blokkere ny matching.

        EF Core kontrollerer at tildelingsstatusen
        ikke er endret siden forespørselen ble lest.

        Kallende controller må kontrollere at brukeren
        har rett til å avbryte forespørselen.

        assignmentId:
        Identifikatoren til forespørselen.

        Returnerer: Resultatet av kanselleringen.
    */
    public async Task<ServiceResult> CancelAsync(int assignmentId)
    {
        NeedAssignment? assignment =
            await _context.NeedAssignments
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment?.Need is null || assignment.Resource is null)
        {
            return new ServiceResult(false, "Fant ikke forespørselen.");
        }

        if (assignment.Status is AssignmentStatus.Declined or AssignmentStatus.Cancelled)
        {
            return new ServiceResult(false, "Forespørselen er allerede avsluttet.");
        }

        if (assignment.Need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(false, "Behovet er allerede løst.");
        }

        bool wasConfirmed = assignment.Status == AssignmentStatus.Confirmed;

        assignment.Status = AssignmentStatus.Cancelled;

        if (wasConfirmed && assignment.Resource.Status == ResourceStatus.Busy)
        {
            bool hasOtherActiveConfirmation =
                await HasOtherActiveConfirmationAsync(
                    assignment.ResourceId,
                    excludedAssignmentId: assignment.Id);

            if (!hasOtherActiveConfirmation)
            {
                assignment.Resource.Status = ResourceStatus.Available;
            }
        }

        await UpdateNeedStatusAsync(assignment.Need);

        #region Lagre med samtidighetskontroll

        try
        {
            /*
                NYTT:
                Dersom RespondAsync har endret statusen
                etter at CancelAsync leste den,
                vil concurrency token føre til
                DbUpdateConcurrencyException.

                EF Core ruller tilbake endringene
                i denne SaveChanges-operasjonen.
            */
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            /*
                Ikke overskriv en nyere bekreftelse
                eller annen statusendring.
            */
            _context.ChangeTracker.Clear();

            return new ServiceResult(
                false,
                "Forespørselen ble endret av en annen bruker " +
                "mens du forsøkte å avbryte den. " +
                "Oppdater siden for å se gjeldende status.");
        }

        #endregion

        if (!string.IsNullOrWhiteSpace(assignment.Resource.OwnerUserId))
        {
            await _notifications.NotifyAsync(
                assignment.Resource.OwnerUserId,
                $"Forespørsel avbrutt: {assignment.Resource.ResourceName}",
                "Den offentlige aktøren trenger ikke lenger " +
                "denne ressursen. Takk for at du stilte " +
                "deg tilgjengelig!",
                "/Request");
        }

        return new ServiceResult(true, "Forespørselen er avbrutt.");
    }

    #endregion

    #region Løs behov

    /*
        Behovet er løst: ubesvarte forespørsler avbrytes, bekreftede ressurser blir ledige (hvis
        de ikke er booket et annet sted), og eierne får et takkevarsel.

        Markerer et behov som løst, kansellerer ubesvarte
        forespørsler og frigjør bekreftede ressurser.

        needId:
        Identifikatoren til behovet.

        Returnerer: Resultatet av handlingen.
    */
    public async Task<ServiceResult> ResolveNeedAsync(int needId)
    {
        Need? need = await _context.Needs
            .FirstOrDefaultAsync(n => n.Id == needId);

        if (need is null)
        {
            return new ServiceResult(false, "Fant ikke behovet.");
        }

        if (need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(false, "Behovet er allerede løst.");
        }

        List<NeedAssignment> activeAssignments =
            await _context.NeedAssignments
                .Include(a => a.Resource)
                .Where(a =>
                    a.NeedId == needId
                    && (
                        a.Status == AssignmentStatus.Requested
                        || a.Status == AssignmentStatus.Confirmed))
                .ToListAsync();

        int[] confirmedResourceIds = activeAssignments
            .Where(a => a.Status == AssignmentStatus.Confirmed)
            .Select(a => a.ResourceId)
            .Distinct()
            .ToArray();

        HashSet<int> bookedElsewhere = new();

        if (confirmedResourceIds.Length > 0)
        {
            List<int> otherBookedResourceIds =
                await _context.NeedAssignments
                    .AsNoTracking()
                    .Where(a =>
                        confirmedResourceIds.Contains(a.ResourceId)
                        && a.NeedId != needId
                        && a.Status == AssignmentStatus.Confirmed
                        && a.Need!.Status != NeedStatus.Resolved)
                    .Select(a => a.ResourceId)
                    .Distinct()
                    .ToListAsync();

            bookedElsewhere = otherBookedResourceIds.ToHashSet();
        }

        List<(string UserId, string ResourceName)> ownersToThank =
            new List<(string UserId, string ResourceName)>();

        foreach (NeedAssignment assignment in activeAssignments)
        {
            if (assignment.Status == AssignmentStatus.Confirmed)
            {
                if (assignment.Resource is null)
                {
                    continue;
                }

                if (assignment.Resource.Status == ResourceStatus.Busy
                    && !bookedElsewhere.Contains(assignment.ResourceId))
                {
                    assignment.Resource.Status = ResourceStatus.Available;
                }

                if (!string.IsNullOrWhiteSpace(assignment.Resource.OwnerUserId))
                {
                    ownersToThank.Add((
                        assignment.Resource.OwnerUserId,
                        assignment.Resource.ResourceName));
                }
            }
            else
            {
                assignment.Status = AssignmentStatus.Cancelled;
            }
        }

        need.Status = NeedStatus.Resolved;

        #region Lagre med samtidighetskontroll

        try
        {
            /*
                En samtidig endring av en ubesvart
                forespørsel skal ikke overskrives.
            */
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();

            return new ServiceResult(
                false,
                "En eller flere forespørsler ble endret " +
                "mens behovet ble avsluttet. " +
                "Oppdater siden og prøv igjen.");
        }

        #endregion

        foreach ((string userId, string resourceName) in ownersToThank)
        {
            await _notifications.NotifyAsync(
                userId,
                "Behovet er løst – takk for innsatsen!",
                $"Behovet du stilte «{resourceName}» til " +
                "er markert som løst. Takk for innsatsen!",
                "/Request");
        }

        return new ServiceResult(true, "Behovet er markert som løst.");
    }

    #endregion

    #region Hjelpemetoder

    /*
        Kjører oppdateringen rett i databasen. Svaret forteller om vi kom først (1 rad endret)
        eller om noen andre kom før oss (0 rader).

        Forsøker å reservere en ressurs ved atomisk
        å endre status fra Available til Busy.

        For relasjonelle databaser brukes ExecuteUpdateAsync.

        SQL-operasjonen krever at ressursen fortsatt
        har status Available.

        Dersom en annen operasjon har reservert ressursen,
        blir ingen rader oppdatert, og metoden returnerer false.

        For EF Core InMemory brukes en vanlig statusendring.

        Kalleren er ansvarlig for databasetransaksjonen.

        resource:
        Ressursen som skal reserveres.

        Returnerer:
        True dersom ressursen ble reservert.
        False dersom den ikke lenger var tilgjengelig.
    */
    private async Task<bool> TryMarkResourceBusyAsync(Resource resource)
    {
        if (resource.Status != ResourceStatus.Available)
        {
            return false;
        }

        /*
            I testene brukes en database i minnet som ikke støtter denne operasjonen, derfor en
            enklere vei der.
        */
        if (!_context.Database.IsRelational())
        {
            resource.Status = ResourceStatus.Busy;
            return true;
        }

        /*
            Atomisk UPDATE:
            Bare ressurser som fortsatt er Available
            kan oppdateres til Busy.
        */
        int affectedRows =
            await _context.Resources
                .Where(r => r.Id == resource.Id && r.Status == ResourceStatus.Available)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(r => r.Status, ResourceStatus.Busy));

        if (affectedRows != 1)
        {
            return false;
        }

        /*
            ExecuteUpdateAsync oppdaterer databasen direkte,
            men ikke EF Core sin change tracker.

            Synkroniser derfor også den innlastede entiteten.
        */
        resource.Status = ResourceStatus.Busy;

        return true;
    }

    /*
        Kontrollerer om en ressurs allerede har en bekreftet
        tildeling knyttet til et uløst behov.

        resourceId:
        Ressursens identifikator.

        excludedAssignmentId:
        En eventuell forespørsel som ikke skal telles med.

        Returnerer:
        True dersom ressursen har en annen aktiv bekreftelse.
    */
    private async Task<bool> HasOtherActiveConfirmationAsync(
        int resourceId,
        int? excludedAssignmentId = null)
    {
        IQueryable<NeedAssignment> query =
            _context.NeedAssignments
                .AsNoTracking()
                .Where(a =>
                    a.ResourceId == resourceId
                    && a.Status == AssignmentStatus.Confirmed
                    && a.Need!.Status != NeedStatus.Resolved);

        if (excludedAssignmentId.HasValue)
        {
            int excludedId = excludedAssignmentId.Value;

            query = query.Where(a => a.Id != excludedId);
        }

        return await query.AnyAsync();
    }

    /*
        Oppdaterer behovets status ut fra aktive forespørsler.
        Metoden lagrer ikke endringene.

        need:
        Behovet som skal få oppdatert status.

        Returnerer:
        En oppgave som fullføres når statusen er beregnet.
    */
    private async Task UpdateNeedStatusAsync(Need need)
    {
        if (need.Status == NeedStatus.Resolved)
        {
            return;
        }

        /*
            Bruk tracking slik at endringer i forespørsler
            som ennå ikke er lagret, tas med i beregningen.
        */
        List<NeedAssignment> assignments =
            await _context.NeedAssignments
                .Where(a => a.NeedId == need.Id)
                .ToListAsync();

        if (assignments.Any(a => a.Status == AssignmentStatus.Confirmed))
        {
            need.Status = NeedStatus.Assigned;
        }
        else if (assignments.Any(a => a.Status == AssignmentStatus.Requested))
        {
            need.Status = NeedStatus.UnderReview;
        }
        else
        {
            need.Status = NeedStatus.New;
        }
    }

    #endregion
}
