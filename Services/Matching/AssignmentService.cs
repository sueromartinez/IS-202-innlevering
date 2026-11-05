using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Matching;

/// <summary>
/// Resultat fra en handling, med en melding som kan vises til brukeren.
/// </summary>
public record ServiceResult(bool Success, string Message);


/// <summary>
/// Statusflyten for forespørsler mellom behov og ressurser:
///
///   Offentlig aktør sender forespørsel  → behov "Vurderes"
///   Frivillig bekrefter                  → behov "Tildelt", ressurs "Opptatt"
///   Frivillig avslår                     → behov tilbake til "Ny" (hvis ingen andre svar venter)
///   Offentlig aktør markerer som løst    → behov "Løst", ressurs "Tilgjengelig" igjen
/// </summary>
public class AssignmentService
{
    private readonly ApplicationDbContext _context;

    private readonly NotificationService _notifications;


    public AssignmentService(
        ApplicationDbContext context,
        NotificationService notifications
    )
    {
        _context = context;
        _notifications = notifications;
    }


    // =========================================================
    // SEND FORESPØRSEL
    // =========================================================

    public async Task<ServiceResult> RequestAsync(
        int needId,
        int resourceId,
        string requestedByUserId,
        string? message,
        bool suggestedByAssistant
    )
    {
        Need? need = await _context.Needs.FirstOrDefaultAsync(n => n.Id == needId);

        if (need is null)
        {
            return new ServiceResult(false, "Fant ikke behovet.");
        }

        if (need.Status == NeedStatus.Resolved)
        {
            return new ServiceResult(false, "Behovet er allerede løst.");
        }

        Resource? resource = await _context.Resources.FirstOrDefaultAsync(r => r.Id == resourceId);

        if (resource is null)
        {
            return new ServiceResult(false, "Fant ikke ressursen.");
        }

        if (resource.Status != ResourceStatus.Available)
        {
            return new ServiceResult(false, "Ressursen er ikke tilgjengelig akkurat nå.");
        }

        bool alreadyRequested =
            await _context.NeedAssignments.AnyAsync(assignment =>
                assignment.NeedId == needId
                && assignment.ResourceId == resourceId
                && (assignment.Status == AssignmentStatus.Requested
                    || assignment.Status == AssignmentStatus.Confirmed));

        if (alreadyRequested)
        {
            return new ServiceResult(false, "Denne ressursen er allerede spurt om dette behovet.");
        }

        _context.NeedAssignments.Add(new NeedAssignment
        {
            NeedId = needId,
            ResourceId = resourceId,
            RequestedByUserId = requestedByUserId,
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            SuggestedByAssistant = suggestedByAssistant,
            Status = AssignmentStatus.Requested,
            CreatedAt = DateTime.UtcNow
        });

        if (need.Status == NeedStatus.New)
        {
            need.Status = NeedStatus.UnderReview;
        }

        await _context.SaveChangesAsync();


        if (string.IsNullOrEmpty(resource.OwnerUserId))
        {
            // Eldre ressurser kan mangle kobling til en konto.
            return new ServiceResult(
                true,
                "Forespørselen er lagret, men ressursen er ikke koblet til en konto. "
                + "Kontakt eieren direkte via kontaktinformasjonen.");
        }

        string? phone =
            string.IsNullOrWhiteSpace(resource.ContactPhoneNumber)
                ? null
                : $"{resource.ContactPhoneCountryCode}{resource.ContactPhoneNumber}";

        await _notifications.NotifyAsync(
            resource.OwnerUserId,
            $"Ressursen din er ønsket: {resource.ResourceName}",
            $"Et behov i nærheten trenger «{resource.ResourceName}». "
            + $"Behov: {need.Description}. Gå til Forespørsler for å bekrefte om du kan stille.",
            "/Request",
            phone);

        return new ServiceResult(true, "Forespørsel sendt. Eieren har fått varsel og må bekrefte.");
    }


    // =========================================================
    // FRIVILLIG SVARER
    // =========================================================

    public async Task<ServiceResult> RespondAsync(
        int assignmentId,
        string userId,
        bool accept
    )
    {
        NeedAssignment? assignment =
            await _context.NeedAssignments
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .FirstOrDefaultAsync(a => a.Id == assignmentId);

        // Bare eieren av ressursen kan svare.
        if (assignment?.Resource is null
            || assignment.Need is null
            || assignment.Resource.OwnerUserId != userId)
        {
            return new ServiceResult(false, "Fant ikke forespørselen.");
        }

        if (assignment.Status != AssignmentStatus.Requested)
        {
            return new ServiceResult(false, "Denne forespørselen er allerede besvart.");
        }

        assignment.RespondedAt = DateTime.UtcNow;

        if (accept)
        {
            assignment.Status = AssignmentStatus.Confirmed;
            assignment.Resource.Status = ResourceStatus.Busy;
        }
        else
        {
            assignment.Status = AssignmentStatus.Declined;
        }

        await UpdateNeedStatusAsync(assignment.Need);

        await _context.SaveChangesAsync();


        await _notifications.NotifyAsync(
            assignment.RequestedByUserId,
            accept
                ? $"Bekreftet: {assignment.Resource.ResourceName}"
                : $"Avslått: {assignment.Resource.ResourceName}",
            accept
                ? $"Eieren har bekreftet at «{assignment.Resource.ResourceName}» kan stille til behovet «{assignment.Need.Description}»."
                : $"Eieren kan ikke stille med «{assignment.Resource.ResourceName}». Finn gjerne en annen ressurs.",
            $"/Match/Need/{assignment.NeedId}");

        return new ServiceResult(
            true,
            accept
                ? "Takk! Du har bekreftet at ressursen stiller."
                : "Forespørselen er avslått.");
    }


    // =========================================================
    // AVBRYT FORESPØRSEL
    // =========================================================

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

        // En bekreftet ressurs blir ledig igjen.
        if (assignment.Status == AssignmentStatus.Confirmed)
        {
            assignment.Resource.Status = ResourceStatus.Available;
        }

        assignment.Status = AssignmentStatus.Cancelled;

        await UpdateNeedStatusAsync(assignment.Need);

        await _context.SaveChangesAsync();

        if (!string.IsNullOrEmpty(assignment.Resource.OwnerUserId))
        {
            await _notifications.NotifyAsync(
                assignment.Resource.OwnerUserId,
                $"Forespørsel avbrutt: {assignment.Resource.ResourceName}",
                "Den offentlige aktøren trenger ikke lenger denne ressursen. Takk for at du stilte deg tilgjengelig!",
                "/Request");
        }

        return new ServiceResult(true, "Forespørselen er avbrutt.");
    }


    // =========================================================
    // MARKER BEHOV SOM LØST
    // =========================================================

    public async Task<ServiceResult> ResolveNeedAsync(int needId)
    {
        Need? need = await _context.Needs.FirstOrDefaultAsync(n => n.Id == needId);

        if (need is null)
        {
            return new ServiceResult(false, "Fant ikke behovet.");
        }

        List<NeedAssignment> activeAssignments =
            await _context.NeedAssignments
                .Include(a => a.Resource)
                .Where(a =>
                    a.NeedId == needId
                    && (a.Status == AssignmentStatus.Requested
                        || a.Status == AssignmentStatus.Confirmed))
                .ToListAsync();

        var ownersToThank = new List<(string UserId, string ResourceName)>();

        foreach (NeedAssignment assignment in activeAssignments)
        {
            if (assignment.Status == AssignmentStatus.Confirmed && assignment.Resource is not null)
            {
                // Ressursen blir ledig til neste behov.
                assignment.Resource.Status = ResourceStatus.Available;

                if (!string.IsNullOrEmpty(assignment.Resource.OwnerUserId))
                {
                    ownersToThank.Add((assignment.Resource.OwnerUserId, assignment.Resource.ResourceName));
                }
            }
            else
            {
                // Ubesvarte forespørsler er ikke lenger aktuelle.
                assignment.Status = AssignmentStatus.Cancelled;
            }
        }

        need.Status = NeedStatus.Resolved;

        await _context.SaveChangesAsync();

        foreach (var (userId, resourceName) in ownersToThank)
        {
            await _notifications.NotifyAsync(
                userId,
                "Behovet er løst – takk for innsatsen!",
                $"Behovet du stilte «{resourceName}» til er markert som løst. Ressursen er tilgjengelig igjen.",
                "/Request");
        }

        return new ServiceResult(true, "Behovet er markert som løst.");
    }


    // =========================================================
    // HJELPEMETODER
    // =========================================================

    /// <summary>
    /// Setter behovets status ut fra forespørslene som gjelder det.
    /// Lagrer ikke; kalleren gjør SaveChangesAsync.
    /// </summary>
    private async Task UpdateNeedStatusAsync(Need need)
    {
        if (need.Status == NeedStatus.Resolved)
        {
            return;
        }

        // EF Core returnerer de samme objektene som allerede er lastet,
        // så endringer som ikke er lagret ennå blir tatt med.
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
}
