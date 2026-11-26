Drift og konfigurering

Denne siden forklarer hvordan sensor (eller andre) får applikasjonen til å kjøre, hvilke brukere som finnes, og hva man gjør hvis noe går galt.

Forutsetninger
Programvare	Trengs til
Docker Desktop (startet)	Kjøre appen, databasen, e-post (Mailpit) og AI-modellen
Git	Hente koden
.NET SDK 10	Bare for å kjøre testene eller bygge utenfor Docker
1. Konfigurasjon (.env)

Passord og hemmeligheter ligger i en lokal .env-fil som ikke er i Git. Lag den fra malen i rotmappen (samme mappe som docker-compose.yml):

powershell
Copy-Item .env.example .env

Malen har ferdige testpassord for admin og offentlig aktør. Fyll bare inn de to databasepassordene:

Variabel	Påkrevd	Standard i malen	Forklaring
MARIADB_PASSWORD	Ja	–	Passord for databasebrukeren beredskap_user
MARIADB_ROOT_PASSWORD	Ja	–	Root-passord for MariaDB (bruk et annet enn over)
ADMIN_EMAIL	Ja	admin@beredskap.no	E-post til admin-kontoen
ADMIN_PASSWORD	Ja	Admin123!	Passord til admin-kontoen. Må ha minst 8 tegn med stor og liten bokstav, tall og spesialtegn.
PUBLIC_ACTOR_PASSWORD	Nei	Test123!	Passord til testbrukeren offentlig@beredskap.no. Står det tomt, får brukeren et tilfeldig passord.
SAMI_PROVIDER	Nei	Ollama	Ollama (lokal modell) eller Claude
SAMI_OLLAMA_MODEL	Nei	qwen2.5:7b	Modellen Ollama bruker
ANTHROPIC_API_KEY	Bare for Claude	–	API-nøkkel når SAMI_PROVIDER=Claude

Mangler en påkrevd variabel, stopper docker compose med en melding om hvilken.

Om testpassordene: Admin123! og Test123! er bare ment for lokal testing. Utenfor utviklingsmiljøet krever appen minst 16 tegn i admin-passordet, så Admin123! kan ikke brukes i drift.

Passordene brukes bare når brukerne lages første gang. Endrer man et passord i .env senere, beholder brukeren som allerede finnes det gamle. Nullstill da databasen med docker compose down -v (se steg 4).

2. Start
powershell
docker compose up --build

Docker Compose starter disse containerne:

Tjeneste	Adresse	Hva
app	http://localhost:8080	Nettsiden
mariadb	127.0.0.1:3307	Databasen beredskap_db (MariaDB 11.4)
mailpit	http://localhost:8025	Fanger all e-post (bekreftelseslenker og tofaktorkoder)
ollama	127.0.0.1:11434	Lokal AI-modell for Sami
ollama-pull	–	Laster ned AI-modellen første gang (ca. 4–5 GB), og avslutter så

Appen venter til databasen svarer. Deretter lager den tabellene selv (EF Core-migrasjoner) og legger inn roller, brukere og eksempeldata. Ingen manuelle databasesteg trengs.

Nettsiden er klar når loggen viser Now listening on: http://[::]:5216. Første oppstart kan ta et par minutter. Sami svarer først når modellen er lastet ned (docker compose logs -f ollama-pull). Resten av portalen virker i mellomtiden.

3. Brukere

Brukerne er både forhåndsdefinert og mulige å opprette.

Forhåndsdefinerte brukere (lages automatisk ved første oppstart):

Rolle	E-post	Passord	Merk
Admin	admin@beredskap.no	Admin123!	Har tofaktor: koden hentes i Mailpit (http://localhost:8025)
Offentlig aktør	offentlig@beredskap.no	Test123!	Fra PUBLIC_ACTOR_PASSWORD. Bare i utviklingsmiljøet.
Frivillig (demo)	frivillig@beredskap.no	Test123!	Bare i utviklingsmiljøet
Bedrift (demo)	entreprenor@beredskap.no	Test123!	Bare i utviklingsmiljøet

E-post og passord for admin og offentlig aktør kommer fra .env. Verdiene over er standardverdiene i .env.example.

Demobrukere, ressurser og behov lages bare når databasen er tom og miljøet er Development (som i docker-compose). De kan slås av med Seed__DemoData=false.

Nye brukere kan registrere seg selv på forsiden («Registrer»):

Fyll ut navn, e-post, telefon og passord.
Åpne Mailpit (http://localhost:8025) og klikk bekreftelseslenken.
Logg inn. Tofaktor slås på når e-posten bekreftes, så koden fra Mailpit trengs ved innlogging.

Nye brukere får alltid rollen frivillig. Offentlige aktører kan ikke registrere seg selv, men opprettes av admin under «Brukere». Admin kan også endre rolle og deaktivere brukere.

4. Stoppe og nullstille
Kommando	Effekt
docker compose down	Stopper alt. Dataene i databasen beholdes.
docker compose down -v	Stopper alt og sletter databasen, innloggingsnøklene og AI-modellen. Neste oppstart begynner helt på nytt, med passordene i .env.
docker compose logs -f app	Viser loggen til nettsiden (nyttig ved feil). Simulerte SMS-er vises også her.
5. Kjøre testene

Enhetstestene trenger ikke Docker:

powershell
dotnet test .\FirstWebAppInDocker.Tests\FirstWebAppInDocker.Tests.csproj

De to integrasjonstestene kjøres mot en egen MariaDB (port 3308) og hoppes over hvis de ikke er slått på:

powershell
docker compose -f docker-compose.integration.yml up -d
$env:BEREDSKAP_RUN_MARIADB_TESTS = "1"
$env:BEREDSKAP_TEST_DB_PASSWORD = "<samme som MARIADB_PASSWORD i .env>"
dotnet test .\FirstWebAppInDocker.Tests\FirstWebAppInDocker.Tests.csproj --filter Category=Integration

Se testing.md for hva testene dekker og resultatene.

6. Feilsøking
Problem	Løsning
Sett MARIADB_PASSWORD i .env	.env mangler, ligger i feil mappe eller er ikke fylt ut. Den må ligge ved siden av docker-compose.yml (se steg 1).
Appen starter på nytt hele tiden, og loggen viser Access denied	Databasen ble laget med andre passord tidligere. Bruk de gamle passordene, eller nullstill med docker compose down -v.
Port 8080, 3307 eller 8025 er opptatt	Stopp programmet som bruker porten, eller endre porten i docker-compose.yml.
Får ikke logget inn som admin eller offentlig aktør	Admin trenger koden fra Mailpit. Er passordet i .env endret etter første oppstart, gjelder fortsatt det gamle. Nullstill med docker compose down -v.
Sami svarer «ikke tilgjengelig»	AI-modellen lastes fortsatt ned, eller ANTHROPIC_API_KEY mangler når SAMI_PROVIDER=Claude.
Rare data etter mye testing	docker compose down -v og start på nytt.