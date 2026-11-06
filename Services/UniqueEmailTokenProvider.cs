
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;

namespace FirstWebAppInDocker.Services;

/// <summary>
/// Genererer tilfeldige, sekssifrede
/// engangskoder for e-postbasert tofaktor.
///
/// I motsetning til Identity sin standard
/// EmailTokenProvider brukes ikke tidsbasert
/// kodegenerering.
///
/// Hver ny kode erstatter den gamle.
/// Koden utløper etter fem minutter.
/// Riktig kode fjernes etter verifisering.
/// </summary>
public class UniqueEmailTokenProvider
    : IUserTwoFactorTokenProvider<ApplicationUser>
{
    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;

    private static readonly TimeSpan CodeLifetime =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan PreviousCodeLifetime =
        TimeSpan.FromHours(24);

    public UniqueEmailTokenProvider(
        IDistributedCache cache,
        IDataProtectionProvider dataProtectionProvider)
    {
        _cache = cache;

        _protector = dataProtectionProvider.CreateProtector(
            "Beredskapsportalen.EmailTwoFactor.v1");
    }

    // ========================================
    // KONTROLLER OM E-POSTKODE KAN BRUKES
    // ========================================

    public async Task<bool> CanGenerateTwoFactorTokenAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user)
    {
        string? email =
            await manager.GetEmailAsync(user);

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return await manager.IsEmailConfirmedAsync(user);
    }

    // ========================================
    // GENERER NY SEKSSIFRET KODE
    // ========================================

    public async Task<string> GenerateAsync(
        string purpose,
        UserManager<ApplicationUser> manager,
        ApplicationUser user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        string userId =
            await manager.GetUserIdAsync(user);

        string keySuffix =
            CreateKeySuffix(userId, purpose);

        string activeKey =
            $"TwoFactor:Active:{keySuffix}";

        string previousKey =
            $"TwoFactor:Previous:{keySuffix}";

        // ========================================
        // FINN SISTE GENERERTE KODE
        // ========================================
        //
        // Sist brukte kode beholdes midlertidig
        // også etter at brukeren har logget ut.
        //
        // Dermed unngår vi å sende samme kode
        // ved to innlogginger rett etter hverandre.

        string? previousProtected =
            await _cache.GetStringAsync(previousKey);

        string? previousCode = null;

        if (!string.IsNullOrWhiteSpace(previousProtected))
        {
            try
            {
                previousCode =
                    _protector.Unprotect(previousProtected);
            }
            catch (CryptographicException)
            {
                // Tidligere kode kan ikke leses.
                // Vi genererer en ny tilfeldig kode.
            }
        }

        // ========================================
        // GENERER KRYPTOGRAFISK TILFELDIG KODE
        // ========================================
        //
        // RandomNumberGenerator bruker en
        // kryptografisk sikker tilfeldig generator.
        //
        // Koden kan være fra 000000 til 999999.
        //
        // Vi sammenligner med den forrige koden
        // og genererer på nytt ved likhet.

        string code;

        do
        {
            code = RandomNumberGenerator.GetInt32(
                    0,
                    1_000_000)
                .ToString(
                    "D6",
                    CultureInfo.InvariantCulture);
        }
        while (code == previousCode);

        // ========================================
        // KNYTT KODEN TIL BRUKERENS SECURITY STAMP
        // ========================================
        //
        // Dersom sikkerhetsstempelet endres,
        // eksempelvis ved tilbakestilling av
        // passord, skal koden ikke lenger
        // være gyldig.

        string securityStamp =
            manager.SupportsUserSecurityStamp
                ? await manager.GetSecurityStampAsync(user)
                : string.Empty;

        // ========================================
        // BESKYTT KODEN FØR LAGRING
        // ========================================
        //
        // Koden lagres kryptert i serverens
        // distribuerte cache.
        //
        // Den ligger ikke i brukerens
        // nettleser eller vanlig Session.

        string protectedCode =
            _protector.Protect(
                $"{securityStamp}\n{code}");

        // ========================================
        // LAGRE AKTIV KODE I FEM MINUTTER
        // ========================================
        //
        // En ny utsending erstatter den
        // tidligere aktive koden.

        await _cache.SetStringAsync(
            activeKey,
            protectedCode,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow =
                    CodeLifetime
            });

        // ========================================
        // HUSK SISTE KODE FOR Å UNNGÅ GJENTAKELSE
        // ========================================
        //
        // Denne verdien brukes bare til å sikre
        // at neste genererte kode blir forskjellig.
        //
        // Den gir IKKE tilgang til innlogging.

        await _cache.SetStringAsync(
            previousKey,
            _protector.Protect(code),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow =
                    PreviousCodeLifetime
            });

        return code;
    }

    // ========================================
    // KONTROLLER ENGANGSKODEN
    // ========================================

    public async Task<bool> ValidateAsync(
        string purpose,
        string token,
        UserManager<ApplicationUser> manager,
        ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(purpose) ||
            string.IsNullOrWhiteSpace(token) ||
            token.Length != 6)
        {
            return false;
        }

        // Godta bare seks vanlige siffer.

        foreach (char digit in token)
        {
            if (digit < '0' || digit > '9')
            {
                return false;
            }
        }

        string userId =
            await manager.GetUserIdAsync(user);

        string keySuffix =
            CreateKeySuffix(userId, purpose);

        string activeKey =
            $"TwoFactor:Active:{keySuffix}";

        // ========================================
        // HENT AKTIV KODE
        // ========================================

        string? protectedCode =
            await _cache.GetStringAsync(activeKey);

        if (string.IsNullOrWhiteSpace(protectedCode))
        {
            // Ingen aktiv kode, eller koden er utløpt.
            return false;
        }

        string storedValue;

        try
        {
            storedValue =
                _protector.Unprotect(protectedCode);
        }
        catch (CryptographicException)
        {
            return false;
        }

        // ========================================
        // LES SECURITY STAMP OG KODE
        // ========================================

        int separatorIndex =
            storedValue.LastIndexOf('\n');

        if (separatorIndex < 0)
        {
            return false;
        }

        string storedSecurityStamp =
            storedValue[..separatorIndex];

        string expectedCode =
            storedValue[(separatorIndex + 1)..];

        // ========================================
        // KONTROLLER SECURITY STAMP
        // ========================================

        string currentSecurityStamp =
            manager.SupportsUserSecurityStamp
                ? await manager.GetSecurityStampAsync(user)
                : string.Empty;

        if (!string.Equals(
                storedSecurityStamp,
                currentSecurityStamp,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (expectedCode.Length != 6)
        {
            return false;
        }

        // ========================================
        // SAMMENLIGN KODENE
        // ========================================
        //
        // FixedTimeEquals brukes for å unngå
        // at sammenligningstiden avslører
        // hvor mange tegn som er riktige.

        byte[] submittedBytes =
            Encoding.ASCII.GetBytes(token);

        byte[] expectedBytes =
            Encoding.ASCII.GetBytes(expectedCode);

        bool valid =
            CryptographicOperations.FixedTimeEquals(
                submittedBytes,
                expectedBytes);

        if (!valid)
        {
            return false;
        }

        // ========================================
        // KODEN ER BRUKT - FJERN DEN
        // ========================================
        //
        // Ved neste innlogging må Identity
        // generere en helt ny kode.
        //
        // Forrige kode beholdes bare i
        // PreviousKey for å hindre gjentakelse.

        await _cache.RemoveAsync(activeKey);

        return true;
    }

    // ========================================
    // HJELPEMETODE - CACHE-NØKKEL
    // ========================================
    //
    // Bruker-ID og formål hashes slik at
    // personidentifikatorer ikke inngår
    // direkte i cache-nøkkelen.

    private static string CreateKeySuffix(
        string userId,
        string purpose)
    {
        byte[] input =
            Encoding.UTF8.GetBytes(
                $"{userId}:{purpose}");

        byte[] hash =
            SHA256.HashData(input);

        return Convert.ToHexString(hash);
    }
}
