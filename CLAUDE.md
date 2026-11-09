# CLAUDE.md — AspGuard (ASP.NET Core güvenlik + EF Core N+1 + doküman üretimi)

> Not: Proje adı **AspGuard** (eski adlar: "QueryGuard" → "NPlusGuard"). Kapsam N+1'in ötesine geçtiği için şemsiye isme geçildi; NuGet/GitHub'da çakışma kontrol edildi (Kasım 2026).

## Proje özeti

AspGuard, ASP.NET Core + EF Core uygulamalarına **tek satırla** (`builder.Services.AddAspGuard();`) şunları ekleyen açık kaynak bir kütüphanedir:

1. **Güvenlik middleware'leri** (`IStartupFilter` ile otomatik): güvenlik header'ları, HSTS ve HTTPS yönlendirme (Development dışında), cookie taşıyan isteklerde CSRF koruması (`Sec-Fetch-Site`/`Origin`), IP başına rate limit, `Server` header'ının kaldırılması. Hepsi `AspGuardOptions.Security` ile ayarlanır/kapatılır.
2. **Runtime N+1 dedektörü:** Her HTTP isteği bir scope; tüm DbContext'ler EF Core'un public `DiagnosticListener` olaylarıyla otomatik izlenir (DbContext'e `UseAspGuard` gerekmez). Aynı "şekildeki" sorgu tekrarlanınca uyarır ve kod satırını gösterir. Varsayılan: yalnızca Development'ta açık.
3. **Roslyn analyzer + code fix'ler:** döngüde sorgu, raw SQL injection, bloklayan async, bellekte filtreleme ve **AG030: metot isminden XML yorum üretimi** (`AddPerson` → `/// Kişi ekler.`), kural tabanlı, TR (varsayılan) / EN.

Hedef kitle: ASP.NET Core geliştiricileri. Ana değer önerisi: **"Tek satır kurulum: temel güvenlik, N+1 tespiti ve otomatik doküman."**

## Genel kurallar (Claude Code için)

- **Fazları sırayla, onay beklemeden ve durmadan uygula.** Bir sonraki faza geçmeden önce `dotnet build` (uyarısız) ve `dotnet test` tamamen temiz geçmeli; kırık bir şey bırakıp ilerleme. Her faz bitince kökteki `PROGRESS.md`'yi güncelle (biten faz, kalanlar, bilinen sorunlar) ki bağlam dolsa bile oradan devam edilebilsin. Karar noktasında kullanıcıya sorma: bu dokümana en uygun seçeneği seç ve gerekçesini kökteki `DECISIONS.md`'ye kısa bir madde olarak yaz. Tüm fazlar bitince tek bir final özeti ver (ne yapıldı, test/benchmark sonuçları, yerel deneme adımları, sınırlamalar).
- Bir faza başlamadan önce kısa bir plan çıkar; plan bu dokümandan saparsa nedenini söyle.
- **Kod, yorumlar, XML doc, README, commit mesajları İngilizce** olacak. Benimle sohbette Türkçe konuş.
- Yeni bir NuGet bağımlılığı eklemeden önce sor. Paket sürümlerini ezbere yazma; güncel kararlı sürümü kontrol et.
- Public API'yi minimum tut. Public olan her şeyin XML doc'u olsun. Gerekmeyen her şey `internal`.
- Her davranış değişikliği test ile gelsin. `dotnet build` ve `dotnet test` temiz geçmeden bir işi bitmiş sayma. Uyarıları (warnings) hata gibi ele al.
- Reflection hileleri, EF Core internal API'leri (`Microsoft.EntityFrameworkCore.Internal`) ve `#pragma` ile uyarı bastırma kullanma. Zorunlu kalırsan önce sor.
- Runtime kütüphanesi production'da kapalıyken **sıfıra yakın maliyetli** olmalı; açıkken de maliyeti ölçülmüş olmalı (Faz 4'te benchmark).
- NuGet'e veya GitHub'a hiçbir şey **yayınlama/push etme**. Yayınlamayı ben yapacağım.
- Git komutu çalıştırma (init/commit/push yok); commit'i kullanıcı yapacak. GitHub Actions workflow'larını yaz ama çalıştırmaya çalışma.

## Teknik kararlar

- Runtime kütüphaneleri: `net8.0;net10.0` multi-target (EF Core 8 ve EF Core 10 için koşullu paket referansları).
- Analyzer: `netstandard2.0` (Roslyn analyzer zorunluluğu), `Microsoft.CodeAnalysis.CSharp`.
- Test: xUnit. EF testleri için **SQLite in-memory** (Docker gerektirmesin). Analyzer testleri için `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`.
- Benchmark: BenchmarkDotNet.
- Nullable reference types açık, `TreatWarningsAsErrors` açık, `Directory.Build.props` ve `Directory.Packages.props` (central package management) kullanılsın.

## Hedef klasör yapısı

```
/src
  AspGuard.Core/                 # ASP.NET'siz çekirdek: interceptor, EF diagnostic observer, scope, tespit, options
  AspGuard/                      # Kullanıcının kurduğu paket: AddAspGuard, startup filter, güvenlik middleware'leri, analyzer'ları paketler
  AspGuard.Analyzers/            # Roslyn analyzer (netstandard2.0), doküman metni üreteci dahil
  AspGuard.Analyzers.CodeFixes/  # Code fix provider'lar (analyzer'dan ayrı assembly: RS1038)
/tests
  AspGuard.Core.Tests/
  AspGuard.Tests/
  AspGuard.Analyzers.Tests/
/samples
  AspGuard.Sample.Api/           # Kasıtlı N+1 içeren ve düzeltilmiş endpoint'li örnek API
/benchmarks
  AspGuard.Benchmarks/
/docs
README.md
LICENSE (MIT)
CONTRIBUTING.md
CHANGELOG.md
```

## Tespit mantığı (runtime)

**Sorgu şekli (query shape):** Bir sorgunun SQL metni normalize edilir (whitespace sadeleştirme, sayı/string literal'lerini placeholder ile değiştirme, `IN (...)` listelerini tek eleman gibi daraltma) ve hash'lenir. Parametre değerleri farklı ama şekli aynı olan sorgular "aynı" sayılır.

**N+1 kuralı:** Aynı scope içinde aynı şekildeki sorgu **eşik değer kadar veya daha fazla** (varsayılan: 5) çalıştırıldıysa N+1 şüphesi raporlanır. Mümkünse aynı çağrı yerinden (call site) gelen tekrarlar daha güçlü sinyal olarak işaretlenir.

**Aşırı sorgu kuralı:** Bir scope'taki toplam sorgu sayısı eşik değeri (varsayılan: 30) geçerse uyarı.

**Çağrı yeri tespiti:** Stack trace'ten EF Core, System, Microsoft ve NPlusGuard frame'leri atlanarak kullanıcının kendi kodundaki ilk frame bulunur (dosya:satır). Stack trace yakalamak pahalıdır; bu yüzden ayarlanabilir olmalı (varsayılan: sadece Development'ta açık) ve mümkünse sadece bir şekil eşiğe yaklaştığında yakalanmalı. Bu dengeyi Faz 1'de tasarla ve gerekçesini anlat.

**Scope:** `AsyncLocal` tabanlı. Kaynakları:
- ASP.NET Core middleware: her HTTP isteği bir scope.
- Manuel: `using var scope = NPlusGuardScope.Begin("name");`
- Bilinçli toplu işlemler için bastırma: `using (NPlusGuardScope.Suppress()) { ... }`

**Raporlama modları:**
- `Log` (varsayılan): `ILogger` ile yapılandırılmış uyarı (sorgu şekli, tekrar sayısı, çağrı yeri, örnek SQL).
- `Throw`: Testlerde/CI'da kullanılmak üzere exception fırlatır.
- `OnDetected` (opsiyonel): Kullanıcının kendi handler'ı; atanmışsa her iki modda da ek olarak çağrılır. (`Mode` yalnızca `Log | Throw`.)
- ASP.NET Core'da opsiyonel `X-NPlusGuard-Count` gibi response header'ları.

**Lazy loading** kaynaklı N+1'ler de aynı mekanizmayla yakalanmalı (testte gösterilsin).

## Kullanım

Güncel kullanım: `builder.Services.AddAspGuard();` (başka satır gerekmez). Aşağıdaki taslak tarihsel (NPlusGuard dönemi):

```csharp
// Program.cs
builder.Services.AddNPlusGuard(o =>
{
    o.NPlusOneThreshold = 5;
    o.MaxQueriesPerScope = 30;
    o.Mode = NPlusGuardMode.Log;                      // Log | Throw
    o.Enabled = builder.Environment.IsDevelopment();  // false iken interceptor ilk satırda çıkar
    o.CaptureCallSite = CallSiteCapture.OnThreshold;  // Never | OnThreshold | Always
    o.OnDetected = report => { };                     // opsiyonel, her iki modda da çağrılır
});

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
           .UseNPlusGuard(sp));

app.UseNPlusGuard();
```

```csharp
// Test içinde
using var scope = NPlusGuardScope.Begin();
await client.GetAsync("/orders");
scope.AssertNoNPlusOne();
scope.AssertMaxQueries(3);
```

## Analyzer kuralları

| ID | Açıklama | Varsayılan seviye |
|---|---|---|
| QG001 | `foreach`/`for`/`while`/`do` gövdesi içinde `IQueryable` sorgusunun çalıştırılması (`ToList`, `ToListAsync`, `First*`, `Single*`, `Count*`, `Any*`, `Sum` vb.) veya `DbSet.Find/FindAsync` çağrısı | Warning |
| QG002 | Bellekteki bir koleksiyon üzerinde `Select`/`ForEach` lambda'sı içinde sorgu çalıştırılması | Warning |

Kurallar:
- Sorgunun EF Core'a ait olduğunu **semantic model** ile doğrula (`IQueryable<T>` + EF Core extension metodları, `DbSet<T>`, `DbContext` türevleri). Metot ismine bakarak tahmin etme; LINQ-to-Objects çağrıları false positive üretmemeli.
- False positive testleri en az true positive testleri kadar olsun.
- Her kuralın `docs/rules/QG001.md` gibi bir dokümanı olsun ve diagnostic'in `helpLinkUri`'si oraya işaret etsin.
- Code fix şimdilik kapsam dışı (ileride `Include` önerisi eklenebilir).

## Fazlar

### Faz 0 — İskelet
- İsim kontrolü (NuGet/GitHub), gerekirse alternatif öner.
- Solution, projeler, `Directory.Build.props`, central package management, `.editorconfig`, `.gitignore`, MIT LICENSE.
- Boş ama derlenen projeler ve geçen birer örnek test.
- Public API taslağını (yukarıdaki kullanım) netleştirip bana sun.

### Faz 1 — Çekirdek runtime dedektör
- `DbCommandInterceptor` (sync + async), scope yönetimi, SQL normalizasyonu, N+1 ve aşırı sorgu kuralları, çağrı yeri tespiti, Log/Throw modları ve `OnDetected` callback'i, Suppress.
- Testler: gerçek N+1 senaryosu (döngüde sorgu), `Include` ile düzeltilmiş senaryo (uyarı yok), lazy loading N+1, eşik altı durum, suppress, paralel/async scope izolasyonu, normalizasyon birim testleri.
- **Gizlilik:** Log, exception mesajı ve raporlara varsayılan olarak parametre değerleri yazılmaz; yalnızca normalize edilmiş SQL (literal'ler placeholder) yazılır. `IncludeParameterValues` (varsayılan `false`) açık olsa bile değerler yalnızca DbContext'te `EnableSensitiveDataLogging` de açıksa gösterilir. Varsayılan ayarda log çıktısında parametre değerinin geçmediğini doğrulayan test olmalı; README'de "Privacy" başlığı altında anlatılmalı.

### Faz 2 — ASP.NET Core entegrasyonu
- DI uzantıları, middleware, response header'ları, `IOptions` ile yapılandırma.
- `WebApplicationFactory` ile integration testleri.
- `samples/NPlusGuard.Sample.Api`: kasıtlı N+1 endpoint'i ve düzeltilmiş versiyonu; README'deki demo buradan çıkacak.

### Faz 3 — Roslyn analyzer
- QG001 ve QG002, semantic doğrulama, kapsamlı true/false positive testleri, kural dokümanları.
- Analyzer'ın NuGet paketine doğru yerleştirilmesi (`analyzers/dotnet/cs`).

### Faz 4 — Yayına hazırlık
- BenchmarkDotNet: NPlusGuard kapalı / açık / stack trace açık durumlarında sorgu başına ek maliyet ve allocation. Sonuçları README'ye tablo olarak ekle.
- NuGet metadata (description, tags, README, icon yeri, SourceLink, deterministic build, symbol paketi).
- GitHub Actions: her push/PR'da build + test (Linux ve Windows); `v*` tag'inde pack + NuGet publish (API key secret'tan; workflow'u yaz ama çalıştırma).
- README: tek cümlelik değer önerisi, kurulum, 5 satırlık kullanım, örnek log çıktısı, analyzer ekran görüntüsü yeri, benchmark tablosu, yapılandırma seçenekleri, sınırlamalar (dürüst ol: false positive/negative durumları).
- CONTRIBUTING.md, CHANGELOG.md, issue ve PR template'leri.

### Faz 5 — Opt-in ve legacy desteği
- `[GuardQueries]` attribute'u (sınıf ve metot seviyesinde). Options'a `Scope = AllServices | AttributedOnly` ekle; varsayılan `AllServices`.
- Analyzer, `AttributedOnly` modunda (`.editorconfig` ayarıyla) sadece işaretli sınıfları kontrol etsin.
- Baseline desteği: mevcut runtime bulgularını baseline dosyasına yazma ve sadece yeni bulguları raporlama.

### Faz 6 — Ek analyzer kuralları ve code fix'ler
- QG010: `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw`'a string birleştirme, `string.Format` veya dinamik oluşturulmuş string verilmesi (SQL injection riski). Code fix: interpolasyonlu `FromSql`/`ExecuteSql`'e dönüştür. EF Core'un EF1002 kuralıyla çakışmasın; onun yakaladığını tekrar raporlama.
- QG020: `ToList`/`ToArray`/`AsEnumerable` sonrası `Where`/`Select` ile filtreleme (tabloyu belleğe çekme).
- QG021: EF sorgularında `.Result`/`.Wait()` kullanımı.
- QG001 için code fix: döngü içinde `Find`/`FirstOrDefault(Id == ...)` kalıbını, döngü öncesinde `Contains` ile tek sorgu + sözlük/lookup'a dönüştür. Sadece güvenli şekilde tanınabilen kalıplarda öner.
- Her kural için true/false positive testleri ve `docs/rules` dokümanı. README'ye "dotnet format ile toplu düzeltme" bölümü ekle.
- Runtime'da sorguları otomatik değiştirme/yeniden yazma yapma; tüm düzeltmeler code fix olarak, geliştirici onayıyla olur.

## Yayınlamadan önce kendi projemde deneme

Faz 4 sonunda şu iki yöntem için README'ye "Local testing" bölümü ekle ve bana adımları göster:

1. **Local NuGet feed:** `dotnet pack -c Release -o ./nupkg`, ardından hedef projede `dotnet nuget add source <yol>/nupkg -n NPlusGuardLocal` ve önizleme sürümüyle (`0.1.0-preview.1`) paket ekleme.
2. **Project reference:** Runtime için normal `ProjectReference`; analyzer için `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` ile referans.

### Faz 7 — AspGuard genişlemesi (tamamlandı)
- Yeniden adlandırma (NPlusGuard → AspGuard, QG → AG kural ID'leri).
- Tek satır kurulum: `IStartupFilter` + EF `DiagnosticListener` ile otomatik DbContext izleme.
- Güvenlik middleware'leri (güvenli varsayılanlar) ve integration testleri.
- AG030: metot isminden XML yorum (kural tabanlı; local AI kullanıcı kararıyla kapsam dışı).

## Kapsam dışı (şimdilik)

- EF Core dışındaki ORM'ler (Dapper vb.).
- Dashboard/UI.
- Otomatik sorgu düzeltme (runtime'da).
- AI/LLM ile doküman üretimi (model boyutu ve IDE performansı nedeniyle; kural tabanlı üreteç seçildi).
- CORS ve kimlik doğrulama/yetkilendirme yapılandırması (uygulamaya özgü).
