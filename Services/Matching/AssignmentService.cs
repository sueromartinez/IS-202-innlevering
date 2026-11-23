
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Matching;

/// <summary>
/// Representerer resultatet fra en tjenesteoperasjon.
/// </summary>
/// <param name="Success">
/// Angir om operasjonen ble gjennomført.
/// </param>
/// <param name="Message">
/// En melding som kan vises til brukeren.
/// </param>
public record ServiceResult(bool Success, string Message);

/// <summary>
/// Håndterer forespørsler og tildelinger mellom behov
/// og ressurser i Beredskapsportalen.
/// </summary>
/// <remarks>
/// Tjenesten håndterer statusflyten:
///
/// Ny → Vurderes → Tildelt → Løst.
///
/// Ressurser blir opptatt ved bekreftet tildeling
/// og frigjøres når tildelingen avsluttes.
///
/// Ved bekreftelse brukes en atomisk databaseoppdatering
/// og transaksjon for å forhindre dobbelbooking.
///
/// Samtidighetskonflikter håndteres gjennom EF Core
/// sin optimistiske samtidighetskontroll.
///
/// Kontrollerne må fortsatt kontrollere at brukeren
/// har tilgang til å utføre handlingene.
/// </remarks>
public class AssignmentService
{
    #region Avhengigheter og konstruktør

    private readonly ApplicationDbContext _context;
    private readonly NotificationService _notifications;

    /// <summary>
    /// Oppretter tjenesten med database- og varslingstjenester.
    /// </summary>
    /// <param name="context">
    /// Applikasjonens databasekontekst.
    /// </param>
    /// <param name="notifications">
    /// Tjenesten som sender varsler til brukerne.
    /// </param>
    public AssignmentService(
        ApplicationDbContext context,
        NotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    #endregion

    #region Opprette forespørsler

    /// <summary>
    /// Sender en forespørsel om en tilgjengelig ressurs
    /// til et registrert behov.
    /// </summary>
    /// <param name="needId">Behovets identifikator.</param>
    /// <param name="resourceId">Ressursens identifikator.</param>
    /// <param name="requestedByUserId">
    /// Identity-ID til brukeren som sender forespørselen.
    /// </param>
    /// <param name="message">
    /// Valgfri melding til ressursleverandøren.
    /// </param>
    /// <param name="suggestedByAssistant">
    /// Angir om ressursen ble foreslått av Sami.
    /// </param>
    /// <returns>
    /// Resultat med informasjon om forespørselen ble opprettet.
    /// </returns>
    public async Task<ServiceResult> RequestAsync(
        int needId,
        int resourceId,
        string requestedByUserId,
        string? message,
        bool suggestedByAssistant)
    {
        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            return new ServiceResult(
                false,
                "Du må være logget inn for å sende en forespørsel.");
        }

        Need? need = await _context.Needs
            .FirstOrDefaultAsync(n => n.Id == needId);

        if (need is null)
        {
            return new ServiceResult(
                false,
                "Fant ikke behovet.");
        }

        if (need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(
                false,
                "Behovet er allerede løst.");
        }

        Resource? resource = await _context.Resources
            .FirstOrDefaultAsync(r => r.Id == resourceId);

        if (resource is null)
        {
            return new ServiceResult(
                false,
                "Fant ikke ressursen.");
        }

        if (resource.ResourceType != need.ResourceType)
        {
            return new ServiceResult(
                false,
                "Ressurstypen passer ikke til behovet.");
        }

        if (resource.Status != ResourceStatus.Available)
        {
            return new ServiceResult(
                false,
                "Ressursen er ikke tilgjengelig akkurat nå.");
        }

        bool alreadyRequested =
            await _context.NeedAssignments.AnyAsync(assignment =>
                assignment.NeedId == needId
                && assignment.ResourceId == resourceId
                && (
                    assignment.Status == AssignmentStatus.Requested
                    || assignment.Status == AssignmentStatus.Confirmed
                ));

        if (alreadyRequested)
        {
            return new ServiceResult(
                false,
                "Denne ressursen er allerede spurt om dette behovet.");
        }

        if (await HasOtherActiveConfirmationAsync(resourceId))
        {
            return new ServiceResult(
                false,
                "Ressursen er allerede tildelt et annet behov.");
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

        await _notifications.NotifyAsync(
            resource.OwnerUserId,
            $"Ressursen din er ønsket: {resource.ResourceName}",
            $"Et behov i nærheten trenger «{resource.ResourceName}». " +
            $"Behov: {need.Description}. Gå til Forespørsler " +
            "for å bekrefte om du kan stille.",
            "/Request",
            phone);

        return new ServiceResult(
            true,
            "Forespørsel sendt. Eieren har fått varsel og må bekrefte.");
    }

    #endregion

    #region Besvare forespørsler

    /// <summary>
    /// Lar ressursleverandøren bekrefte eller avslå
    /// en forespørsel om en ressurs.
    /// </summary>
    /// <remarks>
    /// Ved bekreftelse opprettes en databasetransaksjon
    /// dersom databasen er relasjonell.
    ///
    /// Ressursen reserveres atomisk ved å endre
    /// status fra Available til Busy.
    ///
    /// EF Core kontrollerer samtidig at tildelingsstatusen
    /// ikke er endret av en annen operasjon.
    ///
    /// Ved samtidighetskonflikt rulles transaksjonen
    /// tilbake, og brukeren får en forståelig melding.
    ///
    /// Varsler sendes først etter fullført lagring.
    /// </remarks>
    /// <param name="assignmentId">
    /// Identifikatoren til forespørselen.
    /// </param>
    /// <param name="userId">
    /// Identity-ID til ressursleverandøren.
    /// </param>
    /// <param name="accept">
    /// True for bekreftelse, false for avslag.
    /// </param>
    /// <returns>Resultatet av svarhandlingen.</returns>
    public async Task<ServiceResult> RespondAsync(
        int assignmentId,
        string userId,
        bool accept)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new ServiceResult(
                false,
                "Fant ikke forespørselen.");
        }

        // Bekreftelser bruker en eksplisitt transaksjon
        // mot relasjonelle databaser.
        //
        // InMemory støtter ikke relasjonelle transaksjoner.
        await using var transaction =
            accept && _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync()
                : null;

        NeedAssignment? assignment =
            await _context.NeedAssignments
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment?.Resource is null
            || assignment.Need is null
            || string.IsNullOrWhiteSpace(
                assignment.Resource.OwnerUserId)
            || !string.Equals(
                assignment.Resource.OwnerUserId,
                userId,
                StringComparison.Ordinal))
        {
            return new ServiceResult(
                false,
                "Fant ikke forespørselen.");
        }

        if (assignment.Status != AssignmentStatus.Requested)
        {
            return new ServiceResult(
                false,
                "Denne forespørselen er allerede besvart.");
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
                return new ServiceResult(
                    false,
                    "Ressursen er ikke lenger tilgjengelig.");
            }

            bool alreadyAssigned =
                await HasOtherActiveConfirmationAsync(
                    assignment.ResourceId,
                    excludedAssignmentId: assignment.Id);

            if (alreadyAssigned)
            {
                return new ServiceResult(
                    false,
                    "Ressursen er allerede tildelt et annet behov.");
            }

            // Atomisk ressursreservasjon.
            // Bare én samtidig bekreftelse kan endre
            // ressursen fra Available til Busy.
            bool reserved =
                await TryMarkResourceBusyAsync(
                    assignment.Resource);

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

        await UpdateNeedStatusAsync(assignment.Need);

        #region Lagre med samtidighetskontroll

        try
        {
            // Status er konfigurert som concurrency token
            // i ApplicationDbContext.
            //
            // EF Core kontrollerer dermed om forespørselen
            // fortsatt har samme status som da vi leste den.
            await _context.SaveChangesAsync();

            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // NYTT:
            // En annen operasjon har endret forespørselen
            // etter at vi leste den.
            //
            // Hvis ressursen allerede er satt til Busy
            // gjennom ExecuteUpdateAsync, må dette
            // rulles tilbake sammen med tildelingen.
            if (transaction is not null)
            {
                await transaction.RollbackAsync();
            }

            // Fjern utdaterte entiteter fra change tracker.
            // Neste operasjon kan dermed laste ferske data.
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

    #region Kansellere forespørsler

    /// <summary>
    /// Avbryter en aktiv forespørsel og frigjør ressursen
    /// dersom den ikke er bekreftet for et annet aktivt behov.
    /// </summary>
    /// <remarks>
    /// Forespørselen markeres som Cancelled for å beholde
    /// historikken.
    ///
    /// En kansellert forespørsel regnes ikke som aktiv
    /// og skal ikke blokkere ny matching.
    ///
    /// EF Core kontrollerer at tildelingsstatusen
    /// ikke er endret siden forespørselen ble lest.
    ///
    /// Kallende controller må kontrollere at brukeren
    /// har rett til å avbryte forespørselen.
    /// </remarks>
    /// <param name="assignmentId">
    /// Identifikatoren til forespørselen.
    /// </param>
    /// <returns>Resultatet av kanselleringen.</returns>
    public async Task<ServiceResult> CancelAsync(int assignmentId)
    {
        NeedAssignment? assignment =
            await _context.NeedAssignments
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

        if (assignment?.Need is null || assignment.Resource is null)
        {
            return new ServiceResult(
                false,
                "Fant ikke forespørselen.");
        }

        if (assignment.Status is
            AssignmentStatus.Declined or AssignmentStatus.Cancelled)
        {
            return new ServiceResult(
                false,
                "Forespørselen er allerede avsluttet.");
        }

        if (assignment.Need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(
                false,
                "Behovet er allerede løst.");
        }

        bool wasConfirmed =
            assignment.Status == AssignmentStatus.Confirmed;

        assignment.Status = AssignmentStatus.Cancelled;

        if (wasConfirmed
            && assignment.Resource.Status == ResourceStatus.Busy)
        {
            bool hasOtherActiveConfirmation =
                await HasOtherActiveConfirmationAsync(
                    assignment.ResourceId,
                    excludedAssignmentId: assignment.Id);

            if (!hasOtherActiveConfirmation)
            {
                assignment.Resource.Status =
                    ResourceStatus.Available;
            }
        }

        await UpdateNeedStatusAsync(assignment.Need);

        #region Lagre med samtidighetskontroll

        try
        {
            // NYTT:
            // Dersom RespondAsync har endret statusen
            // etter at CancelAsync leste den,
            // vil concurrency token føre til
            // DbUpdateConcurrencyException.
            //
            // EF Core ruller tilbake endringene
            // i denne SaveChanges-operasjonen.
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ikke overskriv en nyere bekreftelse
            // eller annen statusendring.
            _context.ChangeTracker.Clear();

            return new ServiceResult(
                false,
                "Forespørselen ble endret av en annen bruker " +
                "mens du forsøkte å avbryte den. " +
                "Oppdater siden for å se gjeldende status.");
        }

        #endregion

        if (!string.IsNullOrWhiteSpace(
            assignment.Resource.OwnerUserId))
        {
            await _notifications.NotifyAsync(
                assignment.Resource.OwnerUserId,
                $"Forespørsel avbrutt: {assignment.Resource.ResourceName}",
                "Den offentlige aktøren trenger ikke lenger " +
                "denne ressursen. Takk for at du stilte " +
                "deg tilgjengelig!",
                "/Request");
        }

        return new ServiceResult(
            true,
            "Forespørselen er avbrutt.");
    }

    #endregion

    #region Løse behov

    /// <summary>
    /// Markerer et behov som løst, kansellerer ubesvarte
    /// forespørsler og frigjør bekreftede ressurser.
    /// </summary>
    /// <param name="needId">
    /// Identifikatoren til behovet.
    /// </param>
    /// <returns>Resultatet av handlingen.</returns>
    public async Task<ServiceResult> ResolveNeedAsync(int needId)
    {
        Need? need = await _context.Needs
            .FirstOrDefaultAsync(n => n.Id == needId);

        if (need is null)
        {
            return new ServiceResult(
                false,
                "Fant ikke behovet.");
        }

        if (need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(
                false,
                "Behovet er allerede løst.");
        }

        List<NeedAssignment> activeAssignments =
            await _context.NeedAssignments
                .Include(a => a.Resource)
                .Where(a =>
                    a.NeedId == needId
                    && (
                        a.Status == AssignmentStatus.Requested
                        || a.Status == AssignmentStatus.Confirmed
                    ))
                .ToListAsync();

        int[] confirmedResourceIds = activeAssignments
            .Where(a =>
                a.Status == AssignmentStatus.Confirmed)
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

            bookedElsewhere =
                otherBookedResourceIds.ToHashSet();
        }

        var ownersToThank =
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
                    && !bookedElsewhere.Contains(
                        assignment.ResourceId))
                {
                    assignment.Resource.Status =
                        ResourceStatus.Available;
                }

                if (!string.IsNullOrWhiteSpace(
                    assignment.Resource.OwnerUserId))
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
            // En samtidig endring av en ubesvart
            // forespørsel skal ikke overskrives.
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

        foreach (var (userId, resourceName) in ownersToThank)
        {
            await _notifications.NotifyAsync(
                userId,
                "Behovet er løst – takk for innsatsen!",
                $"Behovet du stilte «{resourceName}» til " +
                "er markert som løst. Takk for innsatsen!",
                "/Request");
        }

        return new ServiceResult(
            true,
            "Behovet er markert som løst.");
    }

    #endregion

    #region Atomisk ressursreservasjon

    /// <summary>
    /// Forsøker å reservere en ressurs ved atomisk
    /// å endre status fra Available til Busy.
    /// </summary>
    /// <remarks>
    /// For relasjonelle databaser brukes ExecuteUpdateAsync.
    ///
    /// SQL-operasjonen krever at ressursen fortsatt
    /// har status Available.
    ///
    /// Dersom en annen operasjon har reservert ressursen,
    /// blir ingen rader oppdatert, og metoden returnerer false.
    ///
    /// For EF Core InMemory brukes en vanlig statusendring.
    ///
    /// Kalleren er ansvarlig for databasetransaksjonen.
    /// </remarks>
    /// <param name="resource">
    /// Ressursen som skal reserveres.
    /// </param>
    /// <returns>
    /// True dersom ressursen ble reservert.
    /// False dersom den ikke lenger var tilgjengelig.
    /// </returns>
    private async Task<bool> TryMarkResourceBusyAsync(
        Resource resource)
    {
        if (resource.Status != ResourceStatus.Available)
        {
            return false;
        }

        // EF Core InMemory støtter ikke ExecuteUpdateAsync.
        if (!_context.Database.IsRelational())
        {
            resource.Status = ResourceStatus.Busy;
            return true;
        }

        // Atomisk UPDATE:
        // Bare ressurser som fortsatt er Available
        // kan oppdateres til Busy.
        int affectedRows =
            await _context.Resources
                .Where(r =>
                    r.Id == resource.Id
                    && r.Status == ResourceStatus.Available)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(
                        r => r.Status,
                        ResourceStatus.Busy));

        if (affectedRows != 1)
        {
            return false;
        }

        // ExecuteUpdateAsync oppdaterer databasen direkte,
        // men ikke EF Core sin change tracker.
        //
        // Synkroniser derfor også den innlastede entiteten.
        resource.Status = ResourceStatus.Busy;

        return true;
    }

    #endregion

    #region Hjelpemetoder

    /// <summary>
    /// Kontrollerer om en ressurs allerede har en bekreftet
    /// tildeling knyttet til et uløst behov.
    /// </summary>
    /// <param name="resourceId">
    /// Ressursens identifikator.
    /// </param>
    /// <param name="excludedAssignmentId">
    /// En eventuell forespørsel som ikke skal telles med.
    /// </param>
    /// <returns>
    /// True dersom ressursen har en annen aktiv bekreftelse.
    /// </returns>
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

            query = query.Where(a =>
                a.Id != excludedId);
        }

        return await query.AnyAsync();
    }

    /// <summary>
    /// Oppdaterer behovets status ut fra aktive forespørsler.
    /// Metoden lagrer ikke endringene.
    /// </summary>
    /// <param name="need">
    /// Behovet som skal få oppdatert status.
    /// </param>
    /// <returns>
    /// En oppgave som fullføres når statusen er beregnet.
    /// </returns>
    private async Task UpdateNeedStatusAsync(Need need)
    {
        if (need.Status == NeedStatus.Resolved)
        {
            return;
        }

        // Bruk tracking slik at endringer i forespørsler
        // som ennå ikke er lagret, tas med i beregningen.
        List<NeedAssignment> assignments =
            await _context.NeedAssignments
                .Where(a => a.NeedId == need.Id)
                .ToListAsync();

        if (assignments.Any(a =>
            a.Status == AssignmentStatus.Confirmed))
        {
            need.Status = NeedStatus.Assigned;
        }
        else if (assignments.Any(a =>
            a.Status == AssignmentStatus.Requested))
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
