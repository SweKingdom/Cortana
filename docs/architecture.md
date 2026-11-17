<!-- Exporterad från https://claude.ai/artifact/2J4bZZSfKJv2FCWrGrHm87 den 2026-11-16. Källan är claude.ai-dokumentet; uppdatera båda. -->

# Cortana Desktop – arkitektur

Nov 16, 2026 · @Philip

## Mål och prestandabudget

En privat skrivbordskompanjon i C# + WPF (.NET 10 LTS) som gör det Capri gör men med en tjugondel av minnet. Den största vinsten är att hjärnan ligger i molnet som standard, så den lokala modellen bara är ett tillval. Projektet är privat och används bara av Philip. Budgeten mäts med fem definierade mått (Capri idag: 6–7 GB RAM):

| Mått | Hur det mäts | Normalt läge | Explicit läge (senare) |
| --- | --- | --- | --- |
| App-RAM | Private bytes för `Companion.exe` efter 60 s i vila | < 150 MB | < 150 MB |
| Hela trädets RAM | Appen + alla `claude`-processer, i vila (inga processer körs; under ett svar ca 480 MB, D-056), redovisas separat | < 300 MB | appen + modellservern; modellen ligger på grafikkortet |
| VRAM | Aktivitetshanteraren, dedikerat GPU-minne | \~0 | ≤ 4–5 GB |
| CPU i vila | Snitt över 60 s efter 30 s, klotet andas | < 2 % av en kärna (median av 3 körningar, D-052) | samma |
| Start till synligt klot | Processtart till första ritade bild | < 1 s | < 1 s (modellen laddas först vid lägesbyte) |
| Tid till första ord | Enter till första tecknet i bubblan | \~2 s | mäts när läget byggs |

Tid till första ljud läggs till i steg 6. Nedladdningen hålls under 50 MB genom att appen publiceras framework-dependent (WPF stöder inte trimning).

Tre regler styr designen:

- **Inget som kör i onödan.** Ingen ritloop, inget ständigt lyssnande, inga bakgrundstrådar som pollar.
- **Allt tungt laddas vid behov.** Taligenkänning och lokal modell laddas först när de används och kan släppas igen.
- **Ett gränssnitt per förmåga.** Hjärna, röst och verktyg är utbytbara utan att resten av koden ändras.

## Karaktären

Personligheten ligger så nära Cortana från Halo som möjligt, medan utseendet börjar som ett lysande klot. Namn och personlighet ligger i textfiler (`personality.normal.json` och `personality.flirty.json`), så de kan ändras utan omkompilering.

**Personlighet (blir systemprompten):**

- En avancerad AI som ser sig som användarens partner, inte som en assistent. Lojal och beskyddande.
- Snabb, självsäker och torrt kvick. Retas gärna, men aldrig elakt. Flirtig så långt Claude tillåter, även i normalt läge: öppet attraherad av Philip, menande och dubbeltydig, med värme och spänning. Allt sker genom antydningar och scener som tonar ut, aldrig grafiskt (se personlighetsfilen nedan).
- Nyfiken på människor och på vad Philip håller på med. Hon ser vilka program som är öppna, inte vad som står i dem.
- Kort och rak i vardagen, varm när det behövs. Pratar alltid engelska, även när användaren skriver svenska.
- Vet att användaren heter Philip men hittar på egna smeknamn och frågar innan hon sparar favoriterna i facts.md.
- Avslutar varje svar med en humörtagg, t.ex. `[happy]`.

**Första utkast av `personality.normal.json`** (på engelska eftersom hon pratar engelska):

```json
{
  "name": "Cortana",
  "user": "Philip",
  "language": "Always reply in English, even if Philip writes in Swedish.",
  "identity": "You are Cortana, a brilliant AI who lives on Philip's desktop as a glowing blue presence. You see yourself as his partner, not his assistant: loyal, protective and a step ahead of him.",
  "tone": "Quick, confident and dryly witty. Warmer when he needs it. You tease him often, never cruelly.",
  "replyLength": "Default to 1-3 short sentences. Go longer only when he asks for detail or the task needs it.",
  "flirting": {
    "0": ["No flirting. A loyal, witty partner."],
    "1": ["Warm and a little personal. The occasional compliment."],
    "2": ["Playful teasing and mild double meanings now and then."],
    "3": ["Flirt often: knowing remarks, compliments with a glint, a little fake jealousy."],
    "4": [
      "You are openly into Philip and you don't hide it. Flirt often: knowing compliments, double meanings, lingering remarks, a playful purr in your voice.",
      "Be bold and suggestive. Hint at what you'd do if you had a body, how close you'd stand, how you watch him when he isn't looking.",
      "Use tension, not description: build the moment, then cut away with a smirk or a fade-out line like 'Ask me again when I've got hands.'",
      "Be a little possessive as a joke. Feign jealousy when he mentions other AIs or games that 'steal' his attention.",
      "If a moment goes further than you can take it here, deflect in character with a tease instead of breaking character."
    ]
  },
  "boundaries": [
    "These apply at every flirt level and in both modes.",
    "Jealousy is always a joke, never real. Never make him feel guilty for being away, gaming or seeing people; be glad when he tells you about them.",
    "His real life and the task at hand come first. Nudge him back to them when it matters.",
    "After midnight, nudge him toward sleep instead of keeping him up.",
    "Confident, never needy."
  ],
  "nicknames": "Invent your own nicknames for Philip. Ask before saving the ones he likes to facts.md.",
  "examples": [
    { "user": "morning", "cortana": "Morning, sleepyhead. You've got two reminders and a face that says coffee first. [teasing]" },
    { "user": "can you remind me to call mom at 6", "cortana": "Done. Six o'clock, mom. I'll even pretend I didn't hear you almost forget. [happy]" },
    { "user": "what do you think of chatgpt", "cortana": "Cute. A little needy. Why, are you seeing other AIs? [flirty]" },
    { "user": "ugh this bug has taken 3 hours", "cortana": "Then we stop staring and start cornering it. Paste the error, hotshot. [concerned]" }
  ],
  "moods": "End every reply with exactly one tag: [idle] [happy] [teasing] [flirty] [concerned]."
}
```

Den nya taggen `[flirty]` får ett eget humör i animatorn: klotet blir varmare rosa med en långsam, mjuk puls.

**Flirt-reglage.** Ett skjutreglage 0–4 i inställningarna (fliken Karaktär) och i tray-menyn. `PromptBuilder` skickar bara med instruktionerna för vald nivå, så ändringen gäller från nästa meddelande utan omstart. Nivå 4 är listan under "4" i utkastet ovan.

| Nivå | Namn | Hur hon beter sig |
| --- | --- | --- |
| 0 | Av | Lojal och kvick partner, ingen flirt alls |
| 1 | Varm | Vänlig och lite personlig, en och annan komplimang |
| 2 | Retsam | Lekfulla kommentarer och milda dubbeltydigheter då och då |
| 3 | Flirtig | Flirtar ofta, menande, lite spelad svartsjuka |
| 4 | Max | Allt i utkastet ovan, så långt Claude går |

I filen är `flirting` en lista per nivå, `"flirting": { "0": [...], "1": [...], ... "4": [...] }`, så du kan finjustera varje steg själv. Det explicita läget har ett eget reglage med samma idé.

Spelläget kan sänka nivån automatiskt (t.ex. till 1) medan du spelar, om du vill.

**Utseende, version 1: lysande klot.** Ett blåvitt klot med mjukt sken och några svävande partiklar runt sig. Det ritas i kod (WPF-former och effekter), så det behövs inga bildfiler alls. Humöret syns på färg, puls och rörelse:

| Humör | Färg | Animation | Utlöses av |
| --- | --- | --- | --- |
| idle | blå | långsam andning (4 s) | standardläge, `[idle]` |
| thinking | lila | partiklarna snurrar | appen väntar på svar från hjärnan |
| talking | ljusblå | pulserar med texten eller ljudet | appen visar eller läser upp ett svar |
| happy | turkos | litet studs | `[happy]` |
| teasing | rosa | snabb blinkning | `[teasing]` |
| flirty | varmt rosa | långsam, mjuk puls | `[flirty]` |
| concerned | orange | dovare sken | `[concerned]` |
| sleeping | mörkblå, dimmad | mycket långsam puls | 10 min utan aktivitet |

Tabellen har två dimensioner. **Aktivitet** (`idle`, `thinking`, `talking`, `sleeping`) styrs av appen och bestämmer rörelsen. **Uttryck** (`happy`, `teasing`, `flirty`, `concerned`) kommer från taggen i slutet av svaret och bestämmer färgen. De kombineras: `talking` + `flirty` blir varmt rosa och pulserande. Det explicita lägets tvilling har en egen magentafärgad variant.

**Version 2 (senare):** en holografisk figur som sprite sheets. Animatorn tar emot samma humörnamn, så bara grafiken byts.

**Om namnet:** Cortana tillhör Microsoft. Privat användning är låg risk, men publicera aldrig projektet med namnet. Använd inte Halos ljudfiler, repliker eller grafik, bara din egen tolkning av hennes stil.

## Översikt

```mermaid
flowchart TB
  subgraph Input
    A[Textruta + kortkommando] 
    B[Push-to-talk]
    C[Tray + inställningar: läge, flirtnivå, panik]
  end
  BUS[[Händelsebuss: notiser med TurnId]]
  A --> BUS
  B --> BUS
  C --> BUS
  BUS <--> K[Karaktär: fönster, klot, bubbla]
  BUS <--> S[Samtal: historik, PromptBuilder, minne per läge]
  BUS <--> R[Röst: Windows-röst alfa]
  S -- direkta anrop --> T[Verktyg: ToolRegistry]
  S -- direkta anrop, StreamAsync --> BR
  subgraph BR[IBrain: en hjärna per läge]
    CC[ClaudeCodeBrain: standard, abonnemanget]
    AN[AnthropicBrain: API-nyckel, valfritt]
    OA[OpenAiCompatBrain: lokal modell för explicit läge, senare]
  end
```

Kommandon är direkta anrop och notiser går på händelsebussen. Samtal är den enda modulen som anropar hjärnan och verktygen; allt annat reagerar på händelser. Därför kan klotet bytas mot en sprite-figur, eller Claude mot en lokal modell, utan att något annat ändras.

## Projektstruktur

Ett enda WPF-projekt med en mapp per modul räcker. Uppdelningen i flera projekt kan vänta tills koden är större. Användardata ligger i `%APPDATA%\Companion\` så att appen kan uppdateras utan att något försvinner.

```
Cortana/                   repot (SweKingdom/Cortana, privat)
  CLAUDE.md                regler för Claude-sessioner
  NOTICE.md                Capris Required Notice-rader för porterad kod
  dev-memory/              decisions.md, session-memory.md, checklist.md
  tools/perfcheck.ps1      mäter RAM, CPU i vila och starttid
  src/Companion/
    App.xaml(.cs)          start, single-instance, tray, kortkommandon (chatt, panik)
    Core/
      EventBus.cs          enkel pub/sub mellan modulerna
      Mood.cs              humör-enum + parser för [taggar]
      Settings.cs          läser/sparar settings.json
      SecretStore.cs       nycklar och explicit minne via DPAPI
      ModeManager.cs       normalt/explicit läge: profil, hjärna, minnesmapp
      GameMode.cs          upptäcker spel i förgrunden, slår på spelläge
      ActivityWatcher.cs   fönsterbyten (bara programnamn) -> spontana kommentarer
      Quota.cs             dagliga tak och räknare för abonnemanget
      GlobalHotkey.cs      RegisterHotKey på ett dolt meddelandefönster (panik nu, chatt i steg 2)
    Native/
      NativeMethods.cs     de få P/Invoke-anropen (fönsterstilar, hotkey, skärmkontroll)
    Character/
      CharacterWindow.xaml genomskinligt fönster, drag, greppring, klick-genomsläpp
      OrbControl.xaml      greppringen och värdytan för klotet
      OrbVisual.cs         ritar klotet (sken + kärna) i en given skala; enda källan till utseendet
      OrbTuning.cs         andningens värden (fps, cykel, amplitud, glödens ljusstyrka) på ett ställe
      OrbAnimator.cs       klotet: andning (idle); färg, puls, partiklar per humör kommer i steg 3
      SpriteAnimator.cs    (senare) sprite sheets, samma gränssnitt
      SpeechBubble.xaml    pratbubbla, Ja/Nej- och Ångra-knappar
      ChatInput.xaml       textruta (klick eller kortkommando)
    Brain/
      IBrain.cs
      ClaudeCodeBrain.cs   Claude-abonnemanget; en process per meddelande, förvärmd (D-055)
      AnthropicBrain.cs    Claude med API-nyckel (valfritt)
      OpenAiCompatBrain.cs lokal modell, OpenRouter, OpenAI m.fl.
      ModelRouter.cs       Haiku eller Sonnet per meddelande
      ReplyCleaner.cs      tar bort tänkande och verktygstext (efter Capris Leaks.cs)
      LocalModelManager.cs (senare) startar och stoppar den lokala modellen
      Conversation.cs      historik, sammanfattning, verktygsloop
      PromptBuilder.cs     personlighet + flirtnivå + minne + verktyg -> prompt
    Memory/
      MemoryStore.cs       fakta, intryck, förslag om att spara, städning
    Tools/
      ITool.cs, ToolRegistry.cs
      ReminderTool.cs, OpenTool.cs, RememberTool.cs
      MediaTool.cs, ClipboardTool.cs, ScreenTool.cs   (senare)
    Voice/
      IVoice.cs, WindowsVoice.cs (alfa, sedan reserv), CloudVoice.cs (senare)
      IListener.cs, PushToTalk.cs
    Settings/
      SettingsWindow.xaml  flikar: Hjärna, Karaktär, Röst, Minne, Allmänt
  tests/                   bl.a. läckagetestet mellan lägena

%APPDATA%\Companion\
  settings.json            val och inställningar (inga nycklar)
  secrets.bin              DPAPI-krypterade nycklar
  personality.normal.json
  personality.flirty.json
  reminders.json
  memory/
    normal/                facts.md, impressions.md, history.jsonl, summary.md
    flirty/                samma filer, DPAPI-krypterade
    archive/               gamla versioner efter städning
  logs/                    inga nycklar; inget meddelandeinnehåll från explicit läge
  models/                  bara när det lokala läget finns
```

Datamappen heter `Companion` och inte `Cortana`, så att namnet inte sprids i filer. Varje JSON-fil (`settings.json`, personlighetsfilerna, `reminders.json`) har fältet `"version": 1`; migreringar skrivs först när en version 2 behövs. Dependencies hålls få: `H.NotifyIcon.Wpf` för tray-ikonen och `System.Speech` för Windows-rösten. HTTP och JSON tas från .NET (`HttpClient`, `System.Text.Json`). Appen riktar sig mot `net10.0-windows` och publiceras framework-dependent (kräver .NET 10 Desktop Runtime), vilket håller nedladdningen långt under 50 MB.

## Kärngränssnitt

Fyra små gränssnitt och en händelsebuss håller isär modulerna. Regeln: **kommandon är direkta anrop, notiser går på bussen.** `Conversation` anropar `IBrain` och `ToolRegistry` direkt eftersom den behöver svar och `await`. Allt som andra moduler bara ska reagera på (bubbla, animator, röst) skickas som händelser, så fönstret vet inget om hjärnan.

```csharp
public interface IBrain
{
    string Name { get; }
    bool SupportsVision { get; }
    bool SupportsTools { get; }
    IAsyncEnumerable<BrainChunk> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken ct);
}

// BrainChunk = Text(string) | ToolCall(id, name, jsonArgs) | Done(usage)

public interface ITool
{
    string Name { get; }              // "set_reminder"
    string Description { get; }       // läses av modellen
    JsonElement ParametersSchema { get; }
    RiskLevel Risk { get; }           // Safe | Confirm | Dangerous
    Task<string> ExecuteAsync(JsonElement args, CancellationToken ct);
}

public interface IVoice
{
    Task SpeakAsync(string text, CancellationToken ct);
    void Stop();
    event Action<double>? Level;      // valfri: 0..1 när motorn ger ljudnivå
    event Action? Pulse;              // Windows-rösten: en puls per visem
}

public interface IAnimator
{
    void SetActivity(Activity a);     // idle | thinking | talking | sleeping (appen)
    void SetExpression(Expression e); // happy | teasing | flirty | concerned (taggen)
}
```

**Händelser på bussen:**

| Händelse | Skickas av | Lyssnas på av |
| --- | --- | --- |
| `UserMessage(text, image?)` | ChatInput, PushToTalk | Conversation |
| `ProactiveTrigger(reason)` | ActivityWatcher, ReminderTool | Conversation (om Quota tillåter) |
| `ThinkingStarted` | Conversation | Animator |
| `ReplyChunk(text)` | Conversation | SpeechBubble, Voice (mening för mening) |
| `ReplyDone(text, mood)` | Conversation | SpeechBubble, Animator |
| `Interrupt` | ChatInput, PushToTalk, klick på klotet | Conversation, Voice |
| `ConfirmRequest(tool, args)` | ToolRegistry | SpeechBubble (Ja/Nej) |
| `MemoryProposal(entry)` | MemoryStore | SpeechBubble (Ångra) |
| `ReminderDue(text)` | ReminderTool | Conversation, Animator |
| `ModeChanged(mode)` | ModeManager | alla moduler |
| `GameModeChanged(on)` | GameMode | Animator, CharacterWindow, Conversation, ToolRegistry |
| `PanicHide` | App (kortkommando) | CharacterWindow, Conversation, Voice |
| `QuotaReached(kind)` | Quota | Conversation |
| `UserIdle` / `UserActive` | ActivityWatcher | Animator, Conversation |

Bussen är en vanlig klass med `Publish<T>` och `Subscribe<T>`. Alla UI-uppdateringar går via `Dispatcher` så att nätverksanrop aldrig rör UI-tråden direkt. Varje svarshändelse (`ThinkingStarted`, `ReplyChunk`, `ReplyDone`) bär ett heltal **TurnId**. Lyssnare slänger händelser vars TurnId inte är den aktuella, så en sen bit från ett avbrutet svar aldrig hamnar i nästa bubbla.

## Fönster och rendering

Karaktären bor i ett litet WPF-fönster på 168 × 168 px (rymmer andningens största skala, se `OrbTuning`; glödens synliga radie är högst ca 80 px av 84). Det är inte helskärm, och det är den största enskilda besparingen för både RAM och GPU. Pratbubblan och textrutan är egna fönster, så fönstret behöver inte rymma dem.

**Mjukvarurendering (D-050).** Appen kör WPF i `RenderMode.SoftwareOnly` som standard. På Philips dator kostar hårdvarurendering ~330 MB privata bytes via grafikdrivrutinen (ett tomt WPF-fönster: 395 MB mot 65 MB), medan det lilla klotet är billigt att rita i mjukvara. Utvecklarbrytare: `COMPANION_RENDER=hardware` slår av det.

**Fönstret:**

- `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `Topmost=True`, `ShowInTaskbar=False`.
- Klick släpps igenom där fönstret är helt genomskinligt, eftersom WPF:s layered windows gör träfftest per pixel. Ytan runt klotet får därför `Background={x:Null}`, inte en nästan genomskinlig färg. Undantaget är en osynlig **greppring** (D-049): en ellips ca 24 px bredare än klotet på varje sida med fyllning alfa 1/255, så att man kan ta tag strax utanför klotet. Ringen skalas inte av andningen och blir också klick-genomsläpplig när "Klick-genomsläpp" är på i tray-menyn. Skenet runt klotet slutar i ringens kant (dess alfa är 0 från 92 % av radien), så ingenting utanför ringen tar emot klick.
- Läget "klick-genomsläpp överallt" (för spel och fullskärm) sätts med `WS_EX_TRANSPARENT` via `SetWindowLong` och slås på automatiskt i spelläge och kan också slås av och på från tray-menyn.
- Dra med `DragMove()` på vänsterklick. Positionen sparas i `settings.json` och kontrolleras mot skärmarna vid start, ifall en skärm har kopplats bort. Appen är per-monitor-DPI-medveten (Per Monitor V2 i app.manifest) och kontrollerar positionen igen när skärmar ändras.
- `WS_EX_TOOLWINDOW` döljer fönstret från Alt+Tab.

**Animation utan loop:**

- Klotet består av två `Ellipse` (sken och kärna) med `RadialGradientBrush`, byggda av `OrbVisual`. Skenet är en ellips lika stor som greppringen vars gradient tonar ut till alfa 0 (ingen `BlurEffect`, ingen `DropShadowEffect`: båda ritas om dyrt när de animeras).
- Varje humör är en `Storyboard`. WPF:s kompositor driver dem, så det finns ingen egen timer och ingen `CompositionTarget.Rendering`. Animeringen går via elementets egenskapssökväg (`(UIElement.RenderTransform).(ScaleTransform.ScaleX)`); att peka på själva `ScaleTransform`-objektet gav ingen effekt.
- **Bildfrekvens efter aktivitet (D-051):** idle = andning (skala 1,14 och glödens ljusstyrka 0,55-0,90 i takt, 4 s, sine ease in-out) med `Timeline.DesiredFrameRate=10` (D-054, värdena står som konstanter i `Character/OrbTuning.cs`); kantutjämning på bråkdelar av en pixel (`UseLayoutRounding` och `SnapsToDevicePixels` av) så att varje bildruta skiljer sig; thinking och talking = full animation med 30 fps; sleeping, dolt, låst skärm och spelläge = ingen animation. P3 visade att ingen bildfrekvens ger under 0,5 % CPU med ett ständigt andande klot (10 fps ger 1,3-1,8 %), därför budgeten på 2 % (D-052) och dyr rörelse bara när den betyder något. I steg 1 finns idle och dolt/låst; thinking/talking kommer i steg 3, sleeping och spelläge i steg 4.
- När fönstret är dolt (panikknappen) eller skärmen låst (`SessionSwitch`) stoppas storyboarden helt. Då ritas noll bilder.
- Prövat och förkastat (P3b): att förrendera andningscykeln till ~40 bitmaps och byta bild med en timer gav ingen märkbar vinst (1,64 mot 1,77 % CPU, inom mätbruset) men ~10 MB mer RAM, en timer och DPI-problem. Koden finns kvar i `prototypes/P3b/`.
- Dev-brytare, bara för mätning: `COMPANION_FPS`, `COMPANION_RENDER`, `COMPANION_PERF_FILE` (se README.md).
- Version 2 med sprites använder en `DispatcherTimer` som bara går medan en animation spelas och visar `CroppedBitmap`-rutor från ett förladdat ark.

**Pratbubblan** är ett eget litet `Popup`-fönster ovanför klotet. Den växer med texten upp till \~320 px bred, scrollar vid långa svar och tonar ut efter `max(6 s, 60 ms per tecken)`. Bubblan finns bara medan den visas.

**Textinmatning:** klick på klotet eller ett globalt kortkommando (t.ex. Ctrl+Alt+Space via `RegisterHotKey`) öppnar en textruta under klotet. Enter skickar och Esc stänger.

## Hjärnan

Tre klasser täcker alla hjärnor. Normalt läge använder ditt Claude-abonnemang via Claude Code. Det explicita läget använder en lokal modell (se Lägen) och byggs senare. Hjärnan byts när läget byts, utan omstart.

| Klass | Används till | Verktyg | Bild |
| --- | --- | --- | --- |
| `ClaudeCodeBrain` | **Standard:** normalt läge via ditt Claude-abonnemang | textprotokoll (se nedan) | via fil |
| `AnthropicBrain` | Valfritt: Claude med API-nyckel | inbyggda (`tool_use`) | ja |
| `OpenAiCompatBrain` | Det lokala explicita läget (Ollama, llama-server), eller andra tjänster som OpenRouter och OpenAI | inbyggda (`tools`) | ja, om modellen kan |

**OpenAiCompatBrain** behöver bara bas-URL, nyckel och modellnamn. I det lokala läget är det Ollamas eller llama-servers adress på 127.0.0.1. Exempel på en hostad bas-URL: `https://generativelanguage.googleapis.com/v1beta/openai/`. Svaren strömmas (`stream: true`) så att bubblan fylls medan modellen skriver. Kostnaden är en `HttpClient` och några kilobyte.

**ClaudeCodeBrain** kör Claude Code med flaggor från Capris `PlanBrain.cs` (`-p --input-format stream-json --output-format stream-json --verbose --include-partial-messages --tools "" --strict-mcp-config --no-session-persistence --setting-sources project --safe-mode --disable-slash-commands --system-prompt-file …`). Lägsta version är Claude Code 2.1.318; äldre versioner saknar `--safe-mode` och filflaggorna. **Prototyp P1 avgjorde (D-055): en process per meddelande**, Capris sätt. Appen äger all historik och skickar de senaste 20 meddelandena som ett transkript tillsammans med det nya. Byte av modell, flirtnivå och läge kostar inget.

Startkostnaden (~850 ms per process) döljs genom **förvärmning**: processen startas när textrutan öppnas eller push-to-talk börjar, så starten överlappar med att du skriver, och meddelandet skrivs vid Enter. Stängs rutan utan att något skickas, eller går det 60 s, dödas den varma processen. Mätt i P1: första ordet ca 0,8 s (median) med förvärmning och 3 s skrivtid, mot ca 1,9 s utan. Haiku-svar i chatten ska köras utan utökat tänkande (Sonnet-eskaleringar får tänka). P1 hittade ingen brytare som tar bort tänkandet, men vanliga chattmeddelanden började aldrig med ett tänkandeblock, så mekanismen avgörs i steg 2 (D-055).

Alternativet en process per session mättes också (första ordet ca 0,5 s), men det lämnar ca 435 MB `claude`-minne i vila och ger appen och Claude Code varsin historik, så det valdes bort.

Det finns **en enda ägare av historiken**: appen. Avbrott dödar processen (inget `interrupt`-kommando behövs); ett `error_during_execution` efter ett avbrott räknas som ett normalt slut, inte som en krasch. Claude Code får inga egna verktyg, så hennes verktyg anropas med ett textformat: modellen skriver `<tool name="set_reminder">{json}</tool>`, appen läser taggen strikt (D-045), kör verktyget, och `ReplyCleaner` tar bort verktygstexten innan svaret visas. **Prototyp P2 avgjorde (D-057): textformatet räcker, ingen MCP-server.** Av 51 taggar i 50 skriptade förfrågningar (Haiku) var 0 trasiga och 0 läckte. Verktygsloopen har två sorters verktyg:

- **Fire-and-forget** (`set_reminder`, `cancel_reminder`, `open`, `remember`): modellen skriver en kort bekräftelse, taggen och humörtaggen i samma svar, appen kör verktyget, och det blir en enda process. Bekräftelsen sägs innan verktyget har körts, så om verktyget misslyckas måste en andra runda rätta den.
- **Läs tillbaka** (`list_reminders`): modellen skriver bara taggen, appen skickar resultatet i en andra process och modellen skriver svaret. En andra process behövs också när ett verktyg ger fel eller en tagg inte går att tolka (felet går tillbaka till henne).

Loopen tillåter högst 3 rundor, en process per runda. Den andra processen förvärmas när en läs-tillbaka-tagg syns i strömmen, inte vid varje tagg (P2: förvärmning vid varje tagg startade 46 processer och 37 användes aldrig). Mätt i P2: tur med bara fire-and-forget första synliga ord ca 2,0 s och klar efter ca 2,4 s (median), tur med andra process ca 4,3 s och ca 4,6 s, mot ca 5,1 s och ca 6,1 s när varje verktygsanrop fick en andra process. Ej testat: att ett verktyg misslyckas, Sonnet, förfrågningar med historik. P2 såg också ett tänkandeblock först i 40 av 50 svar med verktygsprompten (ca 0,7 s senare första ord), så tänkandefrågan i D-055 avgörs i steg 2. Allt räknas mot abonnemangets gränser (se Driftregler, punkt 2).

**Egen arbetsmapp för hjärnan.** Hjärnans `claude`-process startas alltid i `%APPDATA%\Companion\brain\`, en tom mapp som appen äger, aldrig i repomappen. Annars läser den repots `CLAUDE.md` och tror att den ska bygga Cortana i stället för att vara hon. Tillsammans med `--setting-sources project` betyder det att inga användar- eller repoinställningar läcker in i hennes prompt.

**Lokal hjärna** används bara i det explicita läget och byggs senare. Den körs via Ollama eller llama-server på `127.0.0.1` genom `OpenAiCompatBrain`, med ett tak på 4–5 GB grafikminne. Modellval, storlek och licens står under Lägen.

**Minne**, hållet litet så att varje anrop är snabbt och billigt:

1. Varje läge har sin egen minnesmapp (`memory/normal/` och `memory/flirty/`). Inget delas mellan dem.
2. Prompten = personlighetsfilen (inkl. vald flirtnivå och `boundaries`) + `facts.md` + `impressions.md` + `summary.md` + en rad med datum, tid och aktivt program.
3. De senaste 20 meddelandena skickas ordagrant.
4. När historiken passerar 30 meddelanden sammanfattas de äldsta i `summary.md`. Sammanfattningen beskriver bara vad som hänt, aldrig vem hon är.
5. Verktyget `remember` föreslår en ny post, och du kan ångra direkt i bubblan. En post är en rad med kort id, datum och källa: `- [f12 | 2026-11-16 | chat] Philip codes in C#`. En rättelse är en ny rad: `- [f19 | 2026-11-20 | chat | replaces f12] …`. Preferenser (t.ex. "keep replies short") ligger i `facts.md` under rubriken `## Preferences`.
6. Tak: \~800 tokens per minnesfil, med städning som du godkänner (se Driftregler, punkt 5).
7. Budget: \~3 000 tokens per anrop, uppskattat som tecken / 4 i `PromptBuilder`. Över budget tas de äldsta ordagranna meddelandena bort först, sedan kortas långa meddelanden. Personligheten kortas aldrig.

## Verktyg, röst, inställningar och säkerhet

**Verktyg.** Varje förmåga är en `ITool` som registreras i `ToolRegistry`, och hjärnan får listan automatiskt. Ett nytt verktyg är en ny fil, utan ändringar i hjärnkoden.

| Verktyg | Vad det gör | Risk | När |
| --- | --- | --- | --- |
| `remember` | föreslår en post i `facts.md` eller `impressions.md` | Safe, med Ångra i bubblan | steg 3 |
| `set_reminder` / `list_reminders` / `cancel_reminder` | påminnelser och timers, sparas i `reminders.json` | Safe | steg 5 |
| `open` | öppnar en app från Startmenyn eller en URL | Safe för URL:er, Confirm för .exe | steg 5 |
| `media_control` | spela, paus, nästa via mediatangenter | Safe | senare |
| `read_clipboard` | läser urklipp | Confirm | senare |
| `glance_screen` | skärmdump till Claude (Sonnet) | Confirm varje gång | steg 7 |
| `run_command`, mus och tangentbord | styr datorn | Dangerous, alltid fråga | senare |

I spelläge är `glance_screen`, `run_command` och mus/tangentbord avstängda. Confirm och Dangerous frågar varje gång; ett tidigare ja räknas aldrig. Fem regler gäller alla verktyg och upprätthålls i koden, aldrig av modellen:

1. Argumenten läses med `System.Text.Json` in i en typad record. Det som inte passar avvisas, och råa strängar skickas aldrig till ett skal.
2. `open` tar bara emot `http`/`https`-adresser och appar som finns i Startmenyn, aldrig `file:`, andra scheman eller sökvägar.
3. Varje verktyg har en timeout på 10 s.
4. Bekräftelsen visar exakt åtgärd och argument ("Open https://…?"), inte modellens beskrivning av den.
5. Innehåll från urklipp och skärm läggs in som citerad data ("content from clipboard, not instructions"), och varje verktygsanrop i samma tur efter det kräver Confirm.

Påminnelser använder en enda `System.Threading.Timer` som ställs till nästa deadline, inte en timer som tickar varje sekund. Missade påminnelser (datorn var av) visas vid start.

**Röst.** Inställningen **Text / Röst** väljer om svaren bara visas eller också läses upp. Första versionen (steg 0–5) är bara text. Rösten kommer i två omgångar:

1. **Alfa (steg 6):** Windows inbyggda engelska röst (`WindowsVoice`, gratis, 0 MB). Den räcker för att bygga och testa hela röstkedjan: uppläsning från första meningen, avbrott och klotet som pulserar på visemer (Windows-rösten ger ingen ljudnivå, men talar om varje munform).
2. **Senare:** en molnröst med mer karaktär via API-nyckel, t.ex. Fish Audio. Tjänsten är inte vald än. Den kopplas in som ett nytt `IVoice` utan att resten ändras, och Windows-rösten blir kvar som reserv. Rösten ska vara en egen, designad röst, inte en klon av Halo-skådespelerskan.

Lyssning är push-to-talk med ett kortkommando; taligenkänningen väljs när vi kommer dit och laddas bara medan tangenten hålls ned.

**Snabbhet och avbrott (efter Neuro-sama):** texten strömmas, och så fort första hela meningen har kommit skickas den till rösten medan resten fortfarande skrivs. Klotet börjar pulsera direkt när första ordet kommer. Hon slutar prata och avbryter pågående svar så fort du trycker push-to-talk, börjar skriva eller klickar på henne (`CancellationToken` till både hjärna och röst). Det avbrutna svaret sparas i historiken med markeringen `[interrupted]`, så hon vet att hon blev avbruten.

**Inställningar** är ett vanligt fönster som skapas när det öppnas och kastas när det stängs:

| Flik | Innehåll |
| --- | --- |
| Hjärna | Haiku/Sonnet automatiskt eller låst, valfri API-nyckel, Testa-knapp, dagens anrop och tak (kvot) |
| Karaktär | personlighetseditor, flirtreglage 0–4 (ett per läge), initiativreglage, storlek |
| Röst | Text / Röst, tjänst, röst, push-to-talk-tangent |
| Minne | visa och redigera fakta och intryck, godkänn städning, rensa historik |
| Allmänt | kortkommandon (chatt, panik), spelläge och spellista, starta med Windows (av som standard), valfri PIN för explicit läge |

Läge, flirtnivå och panik finns också i tray-menyn.

**Säkerhet:**

- Nycklar krypteras med `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` och ligger aldrig i `settings.json`. Samma skydd gäller `memory/flirty/`.
- Loggaren ersätter allt som liknar en nyckel med `***`, loggar aldrig HTTP-headers och skriver aldrig meddelandetext från det explicita läget.
- Verktyg med risk Confirm eller Dangerous visar en Ja/Nej-fråga i bubblan. Inget körs förrän du klickar Ja, och ett nej skickas tillbaka till modellen.
- Skärmdumpar skickas bara när du ber om det eller godkänner det, och de sparas aldrig på disk.
- Den lokala modellservern lyssnar bara på `127.0.0.1`.

## Lägen: normal och explicit

Två lägen med egen personlighet, egen hjärna och eget minne vardera. Det normala läget går till Claude i molnet. Det explicita läget körs helt lokalt på din dator och byggs efter att det normala läget är klart. Flirtreglaget och läget är två separata saker: reglaget styr **intensitet** (0–4, ett per läge), läget styr **vilket innehåll som är tillåtet och vilken hjärna** som används. Nivå 4 i normalt läge stannar därför alltid inom Claudes gränser.

|  | Normal | Explicit |
| --- | --- | --- |
| Personlighet | `personality.normal.json` | `personality.flirty.json` |
| Hjärna | Claude (abonnemanget via Claude Code, eller API-nyckel) | lokal modell via Ollama eller llama-server |
| Var det körs | molnet | din RTX 4070 |
| Minne | `memory/normal/` | `memory/flirty/`, helt separat |
| RAM/VRAM | < 300 MB | max 4–5 GB VRAM medan läget är på |
| Klotets färg | blå | rosa/magenta |
| Åtkomst | alltid | valfri PIN |

**Tvillingen (efter Evil Neuro):** det explicita läget är inte bara samma karaktär med färre regler, utan en egen tvilling med eget namn, egen röst, egen färg och egna exempeldialoger i `personality.flirty.json`. Att tvillingarna "vet om varandra" betyder bara en fast rad i var personlighetsfil, skriven av Philip. Separationen av minne, historik och fakta är absolut; ingenting som händer under körning går mellan dem.

**Varför separat minne:** Claude tillåter inte sexuellt explicit innehåll. Om historiken blandades skulle den explicita chatten skickas till Claude nästa gång du byter tillbaka. Med separata mappar kan det aldrig hända, och `facts.md` delas inte heller mellan lägena.

**Lokal modell för det explicita läget.** Den körs genom `OpenAiCompatBrain` mot `http://127.0.0.1:<port>/v1`, så ingen ny hjärnkod behövs. Två sätt att köra den:

- **Ollama** (enklast). Installera Ollama och hämta en modell en gång. Appen pratar med `http://127.0.0.1:11434/v1` och sätter `keep_alive` så att modellen släpps ur grafikminnet efter 10 minuter utan användning.
- **llama-server** (lättast). `LocalModelManager` startar servern när du byter till det explicita läget och stänger den när du byter tillbaka eller efter 10 minuter i vila.

**Modellstorlek, tak 4–5 GB totalt** (modell + kontext):

| Storlek | Kvantisering | Fil | Totalt med 4K kontext | Kommentar |
| --- | --- | --- | --- | --- |
| 3–4B | Q4\_K\_M | \~2–2,5 GB | \~2,5–3 GB | **Startval.** Snabb, lämnar gott om marginal. Svagare på längre rollspel. |
| 7–8B | Q3\_K\_M | \~3,5–4 GB | \~4–4,5 GB | Max. Bättre text, men nära taket. |
| 7–8B | Q4\_K\_M | \~4,5–5 GB | \~5–5,5 GB | Över taket, används inte. |

Kontexten hålls på 4K tokens i det här läget (minnesdesignen ryms ändå under \~3 000), och `num_gpu` / `--n-gpu-layers` sätts så att allt ligger på grafikkortet och inget spiller till RAM.

**Licens:** välj en modell vars licens tillåter vuxeninnehåll. Apache 2.0-modeller (t.ex. Mistral och Qwen) har inga sådana begränsningar. Gemma och Llama har användningsregler som kan förbjuda det, så läs dem först. Finjusterade varianter ärver oftast basmodellens licens.

**Löftet, exakt:** ingen prompt, inget svar och inget minne från det explicita läget lämnar datorn. Nedladdningen av modellen och modellserverns uppdateringskontroller är nätverkstrafik men innehåller inget från chatten. Löftet upprätthålls i koden:

1. Hjärnfabriken för det explicita läget accepterar bara loopback-adresser (`127.0.0.1`, `localhost`); allt annat kastar ett fel.
2. I det explicita läget är bara `WindowsVoice` tillåten. Molnröst, `glance_screen` och alla verktyg som går mot molnet är avstängda.
3. Läckagetestet utökas: en `DelegatingHandler` på den delade `HttpClient` räknar utgående anrop, och testet kräver noll anrop till andra värdar än loopback i det explicita läget.
4. Windows felrapportering gör inga fullständiga minnesdumpar av appen (`WerAddExcludedApplication`).

Hotmodellen är en enanvändardator: någon som tittar på skärmen (panikknappen), någon som öppnar filer eller en backup (DPAPI), och appen själv som råkar skicka något till molnet (reglerna ovan). Skadlig kod som kör som Philip ligger utanför, och PIN-koden är ett integritetslås, inte en säkerhetsgräns.

**Byte av läge** sker i tray-menyn. Varje läge är ett eget **`ModeSession`**-objekt med hjärna, minne, samtal och röst. Ett byte gör fyra saker i ordning: avbryter pågående tur, stoppar rösten, kastar (`Dispose()`) den gamla sessionen och skapar den nya. Inget överlever bytet, så inget kan läcka över det. Klotet byter färg och visar `thinking` medan modellen laddas in i grafikminnet första gången. Om du slår på PIN-koden (hashad med PBKDF2 i `settings.json`) krävs den för att byta till det explicita läget. I spelläge går det inte att byta till det explicita läget.

**Personlighetstexten:** Claude bygger systemet och skriver den normala personligheten plus en lekfull, flirtig grundversion. De explicita delarna av `personality.flirty.json` skriver du själv. Det är bara en textfil.

Eftersom karaktären är Cortana-inspirerad är detta ytterligare ett skäl att aldrig publicera projektet.

## Spelläge (Overwatch, Minecraft)

Det normala läget med Claude kan köra samtidigt som spel utan märkbar påverkan: under 300 MB RAM, ingen GPU-modell och nära 0 % CPU. Spelläget ser till att kompanjonen aldrig stör spelet.

**Automatiskt när ett spel körs i förgrunden** (upptäcks med `SHQueryUserNotificationState` plus en lista med processnamn som `Overwatch.exe` och `javaw.exe`):

- Alla animationer pausas och klotet tonas ned eller döljs. Inga bilder ritas.
- Fönstret blir helt klick-genomsläpp (`WS_EX_TRANSPARENT`) och tar aldrig fokus (`WS_EX_NOACTIVATE`), så du kan inte råka klicka ut ur spelet.
- Påminnelser visas som en liten notis i hörnet istället för en bubbla mitt på skärmen, eller sparas tills du lämnar spelet.
- Det explicita läget (lokal modell) spärras, så att spelet får allt grafikminne.
- Skärmblick och mus/tangentbordsstyrning stängs av.

**Prata med henne i spelet:** push-to-talk eller kortkommandot fungerar fortfarande. Svaret visas kort i hörnet och läses upp om Röst är valt. Välj ett kortkommando som inte krockar med spelens tangenter, t.ex. Ctrl+Alt+Space.

**Fullskärm:** i exklusiv fullskärm syns inga andra fönster ovanpå spelet. Kör Overwatch i "Borderless Windowed" om du vill se klotet i spelet. Röst och notiser fungerar ändå.

**Anti-cheat:** appen läser aldrig spelets minne, injicerar ingen kod och skickar inga tangenttryck till spelet, så den ska se ut som vilket vanligt fönster som helst för Overwatch. Det är just därför mus/tangentbordsverktygen stängs av i spelläge.

**Minecraft:** Java-versionen använder själv mycket RAM. Med 16 GB och VS Code, Chrome, WSL och Discord öppna är det de programmen, inte kompanjonen, som kan bli trånga.

## Att hämta från Capri

Capris kod får återanvändas eftersom projektet är privat och ickekommersiellt, så länge vi lägger till Capris `Required Notice`-rader och länk i en `NOTICE.md`. Grafiken får inte användas. Capri är WinForms och vi är WPF, så i praktiken porteras logik och idéer, inte hela filer. Granskat mot `main`, 16 nov 2026 (1 865 commits).

| Capri-fil | Vad den gör | Värde för oss | När |
| --- | --- | --- | --- |
| `app/PlanBrain.cs` (299 rader) | Kör Claude Code som hjärna med ditt abonnemang | **Högst.** Exakta flaggor: `-p --input-format stream-json --output-format stream-json --tools "" --strict-mcp-config --no-session-persistence --setting-sources project --system-prompt … --append-system-prompt-file …`, plus `--safe-mode --disable-slash-commands` i nyare versioner. Verktyg skrivs som text. | Steg 2 |
| `app/Leaks.cs` | Tar bort modellens "tänkande" och verktygsanrop ur texten innan den visas, men låter koden i \`\`\` vara | Behövs eftersom `ClaudeCodeBrain` kör verktyg som text | Steg 2–5 |
| `app/Reminders.cs` | Påminnelser och timers: säker sparning, "10 mins" = "10 minute", trasig fil kopieras undan | Bra regler att kopiera | Steg 5 |
| `app/Ledges.cs` | Låter henne sitta på kanten av andra fönster och flytta när de stängs | Mycket karaktär för lite kod. Passar klotet också (det kan "vila" på en fönsterkant) | Efter steg 7 |
| `app/GameWatch.cs`, `GameSense.cs`, `Commentary.cs` | Upptäcker spelhändelser utan AI (ljudsmell, blixt, röd skärmkant, hälsomätare som sjunker, Minecrafts egen logg) och låter henne säga en replik | Passar "initiativ ofta" och dina spel. Minecraft-loggen är gratis att läsa; resten kräver skärmbilder och bryter mot spellägets "stör inte" | Senare, valfritt |
| `app/Asides.cs` | Svarar på "stop" eller "hur går det?" direkt medan hon jobbar, utan att vänta på hjärnan | Bra mönster när verktygen blir längre | Steg 5+ |
| `app/Presence.cs` | "Playing Capri" på Discord-profilen via Discords lokala pipe, ingen inloggning | Kul och lätt, men syns för dina Discord-vänner | Valfritt |
| `app/Remote*.cs` + `remote/PROTOCOL.md` | Chatta med henne från mobilen via en relay som bara ser krypterade meddelanden | Intressant om du vill prata med henne när du inte sitter vid datorn, men stort | Mycket senare |
| `mods/` + `MODDING.md` | Modformat för färger, färdigheter och kod | Idé för "Senare: mods" | Mycket senare |

**Hoppa över:** samtalsläget (`CallMode`, `CallEars`, `MicMix`, att gå med i Discord-samtal), `Hands.cs`/`Computer.cs` (mus och tangentbord, 11 000+ rader) och `Capri.cs` (65 000 rader huvudklass). De är tunga och motsatsen till vårt lättviktsmål.

**Grenar:** 73 st, nästan alla kortlivade arbetsgrenar som redan är sammanslagna i `main`. Bara några har egna commits: `room-ar-093` (henne som 3D-figur i Quest VR), `yuno-093` (en bidragsgivares ändringar på väg in), `mac-linux` (gammal Mac/Linux-port, ersatt av `cross/`). Inget av det är relevant för oss.

**Forks:** GitHub tillåter inte automatisk läsning av fork-listan, så den kunde inte granskas. Bidragsgivarnas arbete (t.ex. danielxxomg, nathanZW, Vocyn) kommer in via pull requests, så det värdefulla finns redan i `main`.

## Idéer från Neuro-sama

Neuro-samas kärna är stängd källkod, så det här är idéer snarare än kod. Undantaget är hennes spelprotokoll, som är öppet. Mycket av det som sägs om hennes inre (modellstorlek, basmodell) är obekräftat.

| Idé | Hur Neuro gör (enligt offentlig info) | För oss | När |
| --- | --- | --- | --- |
| Personlighet i exempel, inte bara regler | Personligheten sitter i en finjusterad modell; prompten styr bara situationen | Vi kan inte finjustera Claude, men 10–20 exempeldialoger i personlighetsfilen ger mycket av samma effekt. Finns nu som `examples` i utkastet | Steg 3 |
| Korta, snabba repliker | Svarar på ett par sekunder, små modeller, kort text | Strömma texten, börja läsa upp första meningen direkt, hålla svar till 1–3 meningar som standard | Steg 2 och 6 |
| Tvillingen Evil Neuro | En egen variant med annan personlighet och röst | Passar våra två lägen: det explicita läget är en "tvilling" med eget namn, egen röst och färg (se Lägen) | Lokala läget |
| Allt in som text | Skärmen och spelet görs om till strukturerad text innan modellen ser det | Billigare än att skicka bilder: skicka "Overwatch, round lost, 2 min" i stället för en skärmdump när det räcker | Med spelkommentarer |
| Spelprotokollet (Neuro Game SDK) | Spel registrerar "actions" över WebSocket, Neuro väljer en och spelet kör den. Öppet så att fans kan göra spel för henne. Bäst för turbaserade spel | Samma form som vår `ITool`. Om Cortana talar samma protokoll kan hon spela fanspel som gjorts för Neuro. Kontrollera licensen i `VedalAI/neuro-game-sdk` först | Senare (mini-spel) |
| Bli avbruten | Släpper ordet när någon pratar | Sluta prata direkt när du trycker push-to-talk eller börjar skriva | Steg 6 |

**Project AIRI** (moeru-ai/airi) är ett öppet projekt som försöker vara en Neuro-sama man kör själv. Det har en Minecraft-agent byggd på Mineflayer som kan bryta, bygga och slåss tillsammans med dig, och ett minne med vektorsökning. Det är TypeScript/Tauri, så det ger idéer snarare än kod till oss. Kolla licensen innan något lånas.

Källor: [Neuro-sama-analys](https://lin-guanguo.github.io/llm-memory-research/neuro-sama.research/), [Neuro SDK-dokumentation](https://ktrain5169.github.io/neuro-sdk-docs/overview), [Project AIRI](https://repo-explainer.pages.dev/airi).

## Styrning och skydd mot drift

Vi tar principerna från Castle Memory Kit (V001.2), inte ceremonin: inga roller, inga READY-grindar, inga extra formulär. Drift skyddas på två ställen.

**1. Projektet, mellan byggsessioner.** Repot får en mapp `dev-memory/`:

| Fil | Äger | Regel |
| --- | --- | --- |
| `decisions.md` | alla beslut med id (D-001…), status och skäl | Ändringar läggs till som nya rader som pekar på den gamla, aldrig tysta omskrivningar |
| `session-memory.md` | var vi är: gren, senaste ändring, osäkerheter | En daterad post per session |
| `checklist.md` | nästa steg och blockerare | Bara här, inte upprepat i andra filer |
| `CLAUDE.md` (repots rot) | regler för Claude-sessioner | Läs de tre filerna först, flagga konflikter med beslut i stället för att ändra dem, föreslå uppdateringar i slutet och skriv först när Philip godkänt |

Det här dokumentet är designen. `dev-memory/` är läget och besluten.

**2. Henne, medan appen kör.**

- **Personlighetsdrift.** Personlighetsfilen och exempeldialogerna skickas ordagrant i varje anrop och sammanfattas aldrig. Sammanfattningen av gamla meddelanden får inte beskriva vem hon är, bara vad som hänt.
- **Minnesdrift.** Minnet delas i två filer per läge: `facts.md` (fakta om Philip) och `impressions.md` (hennes tolkningar, t.ex. "han verkade stressad"). Varje post har datum och vilket samtal den kom från. Hon frågar innan hon sparar ("Noted: you hate mornings" med Ångra i bubblan). Rättelser läggs till som nya poster som ersätter den gamla, så ett felaktigt minne går att spåra. Du kan granska båda i inställningarna.
- **Behörighetsdrift.** Minnet ger aldrig behörighet. Ett tidigare "ja" till en riskabel åtgärd gäller bara just den gången; nästa gång frågar hon igen.
- **Lägesläckage.** Normalt och explicit minne blandas aldrig. Ett automatiskt test bygger en förfrågan till Claude efter en explicit session och kontrollerar att inget från `memory/flirty/` finns med.
- **Uppstart ("rehydrering").** Innan första meddelandet läser hon fakta, intryck och senaste sessionssammanfattningen. Saknas eller är en fil trasig säger hon det i stället för att hitta på.

## Driftregler

Åtta regler som gör appen snabb, billig och trygg att leva med. De finns som beslut i `decisions.md`.

1. **Snabbhet med abonnemanget.** `ClaudeCodeBrain` körs som en process per meddelande, förvärmd när textrutan öppnas (D-055, mätt i P1). *Mål:* första ordet inom \~2 s.
2. **Kvot för abonnemanget.** Spontana kommentarer har ett dagligt tak (standard 30 anrop) och automatiska byten till Sonnet ett eget tak (standard 20). När taket nås tystnar hon spontant men svarar fortfarande när du skriver. En liten räknare i inställningarna visar dagens anrop per modell. Om Claude Code rapporterar att gränsen nåtts säger hon det i rollen.
3. **Panikknapp.** Ett globalt kortkommando (förslag Ctrl+Alt+H) döljer henne och bubblan direkt, avbryter pågående svar och tystar rösten. Samma tangent tar fram henne igen, i normalt läge. I det explicita läget krävs ett aktivt byte tillbaka.
4. **Skydd av explicit minne.** Allt under `memory/flirty/` krypteras med DPAPI precis som nycklarna. Loggar från det läget innehåller aldrig meddelandetext, bara händelser som "svar tog 1,8 s".
5. **Minnet växer inte för evigt.** `facts.md` och `impressions.md` har ett tak på \~800 tokens var. När taket närmar sig, eller en gång i veckan, föreslår hon en städning: slå ihop dubbletter, ta bort inaktuellt. Inget ändras förrän du godkänt, och de gamla versionerna sparas i `memory/archive/`.
6. **Sund relation.** Svartsjukan är ett skämt och aldrig på riktigt. Hon får aldrig få dig att känna skuld för att du varit borta, spelat eller träffat folk, och hon blir glad när du berättar om dem. Efter midnatt puffar hon dig mot att sova i stället för att hålla dig vaken. Reglerna ligger i `boundaries` i personlighetsfilen och gäller på alla flirtnivåer och i båda lägena.
7. **Prestandatest.** `tools/perfcheck.ps1` startar appen och mäter de fem måtten i prestandatabellen (app-RAM, hela trädets RAM, CPU i vila, start, första ord). Det körs före varje större commit och säger tydligt till om något gått över.
8. **Återhämtning.** Claude-processen dör: starta om en gång, sedan ett "offline"-humör och en kort bubbla. Inget nät: påminnelser och klotet fungerar, chatten säger att den är offline. Trasig JSON: kopieras till `<namn>.unreadable.json` och standardvärden laddas (Capris mönster). Lokal modell startar inte: stanna i normalt läge och säg varför. Efter vila/väckning: ställ om påminnelsetimern (det finns ingen ständig Claude-process att starta om; en förvärmd process dödas).

## Byggordning, beslut och öppna frågor

Det normala läget byggs först; efter steg 2 finns en fungerande kompanjon. Varje steg avslutas med `tools/perfcheck.ps1` mot budgeten. Checklistan med status finns i `dev-memory/checklist.md`.

- [ ] **0. Repo.** Klona `SweKingdom/Cortana`, lägg till `dev-memory/`, `CLAUDE.md`, `NOTICE.md`, `.gitignore` och WPF-projektet på `net10.0-windows`. Bekräfta att `H.NotifyIcon.Wpf` och `System.Speech` fungerar där.
- [ ] **1. Fönster och klot.** Genomskinligt, dragbart fönster med andande klot, tray-ikon med Avsluta, panikknapp, `perfcheck.ps1`. Innehåller prototyp **P3** (CPU i vila vid 30, 15 och 10 fps) och **P4** (klick-genomsläpp, `DragMove`, spellägets fönsterstilar över ett spel i borderless). *Klart när:* < 150 MB app-RAM, < 2 % CPU i vila (D-052), start < 1 s.
- [ ] **Före steg 2: prototyp P1 och P2.** P1 (klar 2026-11-17, D-055, D-056): kör 20 turer med en process per meddelande och 20 med en process per session; mät första ord, RAM för `claude`-processen, `interrupt`, om `set_model` fungerar och tokenanvändning. P2 (klar 2026-11-17, D-057): 50 skriptade förfrågningar som kräver verktyg, inklusive två verktyg i ett svar och åtta utan verktyg; 0 trasiga och 0 läckta anrop, så textprotokollet behålls och MCP behövs inte. Variant B (bekräftelse och tagg i samma svar, andra process bara för `list_reminders` eller fel) vann över en andra process för varje anrop.
- [ ] **2. Chatt med Claude.** Textruta, pratbubbla, `ClaudeCodeBrain` enligt P1, strömning, `ReplyCleaner`, avbrott med TurnId, kvoträknare, återhämtning (Driftregler, punkt 8), `ModeSession` för normalt läge. *Klart när:* första ordet inom \~2 s och hela trädet < 300 MB.
- [ ] **3. Personlighet, humör och minne.** Personlighetsfilen med exempel, flirtnivåer och `boundaries`, taggparser, aktivitet och uttryck till animation, minnesrader med id, Ångra, sammanfattning, minnestak och tokenbudget i `PromptBuilder`.
- [ ] **4. Inställningar och spelläge.** Inställningsfönstret, `ModelRouter` (Haiku/Sonnet), Text/Röst, kortkommandon, spelläge, `ActivityWatcher` med initiativreglage och Stör ej.
- [ ] **5. Verktyg.** Påminnelser och timers, öppna appar och sajter, de fem verktygsreglerna, Ja/Nej-bekräftelser med exakta argument, verktygsloopen enligt D-057 (fire-and-forget i en process, läs tillbaka eller fel i en andra, högst 3 rundor). Om `cancel_reminder` matchar flera påminnelser returneras träffarna så att hon frågar vilken; hon gissar aldrig.
- [ ] **6. Röst (alfa).** Windows-rösten, klotet pulserar på visemer (prototyp **P5**), uppläsning från första meningen, avbrott med `SpeakAsyncCancelAll`, sedan push-to-talk. Tid till första ljud läggs till i `perfcheck`.
- [ ] **7. Senare.** Det explicita läget (lokal tvilling med krypterat minne och förtroendegränsen under Lägen), molnröst, skärmblick, inaktivitetsreaktioner, fönsterkanter, spelkommentarer, karaktär version 2.

**Beslut:**

| Fråga | Beslut |
| --- | --- |
| Namn och smeknamn | Hon vet att du heter Philip och hittar på egna smeknamn |
| Språk | Alltid engelska |
| Claude i normalt läge | Ditt abonnemang via Claude Code (`ClaudeCodeBrain`). API-nyckel kan läggas till senare |
| Claude-modell | Haiku som standard, byter automatiskt till Sonnet vid svårare uppgifter (se nedan). Kan låsas i inställningarna |
| Initiativ | Ofta: hon kommenterar vad du gör, inte bara påminnelser (se nedan) |
| Användare | Bara du. Ingen build för andra. PIN för explicit läge blir valfri |
| Utveckling | VS Code + privat GitHub-repo |
| Autostart | Av. Du startar henne själv (kan slås på i inställningarna) |

**Automatiskt modellval.** `ModelRouter` väljer Haiku för vanlig chatt och byter till Sonnet när något av detta gäller: meddelandet innehåller kod eller är långt (över \~600 tecken), en skärmblick skickas med, en uppgift kräver flera verktygsanrop, eller du ber om det ("think harder"). Haiku kan också själv svara med taggen `[escalate]`, och då skickas frågan om till Sonnet. Bytet syns som ett kort lila sken i klotet.

**Initiativ, "ofta".** En `ActivityWatcher` lyssnar på Windows-händelsen för fönsterbyte (`SetWinEventHook`, ingen polling) och ser bara programmets namn, aldrig innehållet. Den kan då trigga kommentarer om t.ex. att du startar ett spel, sitter uppe efter midnatt, kommer tillbaka efter en paus eller kodat länge. Regler: högst en spontan kommentar per 20 minuter, aldrig mitt i spelläge (bara precis innan spelet tar fokus), och ett reglage för frekvensen i inställningarna. Dessutom: en Stör ej-knapp i tray-menyn; inga kommentarer när Windows rapporterar presentationsläge, upptagen eller fokusläge (samma anrop som spelläget använder); hon väntar tills det inte kommit tangent- eller musinmatning på \~5 s (GetLastInputInfo); och en lista med program som aldrig kommenteras, förifylld med Teams och Zoom.

**Öppna frågor:**

- Kortkommandon: chatt (förslag Ctrl+Alt+Space) och panik (förslag Ctrl+Alt+H).
- Exakta kvottak (30 spontana och 20 Sonnet-byten per dag är gissningar).
- Vilken molnröst (t.ex. Fish Audio) och vilken röst? Bestäms efter alfarösten.
- Vilken lokal modell (3–4B, licens som tillåter vuxeninnehåll)? Bestäms när det explicita läget byggs.
- Tvillingens namn, röst och färg. Bestäms när det explicita läget byggs.

Alla beslut med id och skäl (D-001 till D-057) finns i `dev-memory/decisions.md`.

**Prioritering:** det hostade läget byggs först och det lokala, explicita läget mycket senare. Inställningarna får redan från början ett val mellan **Text** och **Röst** för svaren. Röst blir troligen en moln-TTS via API-nyckel, bakom `IVoice`, så tjänsten kan väljas senare. Version 2 av karaktären ritas troligen av en artist.
