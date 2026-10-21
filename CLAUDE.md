# CLAUDE.md — NPlusGuard (EF Core N+1 Detector)

> Not: Proje adı NPlusGuard olarak kesinleşti (eski çalışma adı "QueryGuard", NuGet/GitHub'da çakışma çıktığı için değiştirildi).

## Proje özeti

NPlusGuard, EF Core kullanan .NET uygulamalarında **N+1 sorgu problemini** ve **aşırı sorgu sayısını** tespit eden açık kaynak bir kütüphanedir. İki parçadan oluşur:

1. **Runtime dedektör:** EF Core interceptor'ı ile bir kapsam (HTTP isteği, test, manuel scope) içinde atılan sorguları izler; aynı "şekildeki" sorgunun tekrar tekrar atıldığını görünce uyarır ve sorunun oluştuğu kod satırını gösterir.
2. **Roslyn analyzer:** Derleme anında döngü içinde veritabanı sorgusu çalıştırılan kodu tespit edip uyarı verir.

Hedef kitle: EF Core kullanan ASP.NET Core geliştiricileri. Ana değer önerisi: **"3 satır kurulum, N+1'i production'a çıkmadan yakala."**

## Genel kurallar (Claude Code için)

- **Fazlar halinde çalış.** Her fazın sonunda dur, ne yaptığını kısaca özetle, testlerin geçtiğini göster ve onayımı bekle. Onay almadan sonraki faza geçme.
- Bir faza başlamadan önce kısa bir plan çıkar; plan bu dokümandan saparsa nedenini söyle.
- **Kod, yorumlar, XML doc, README, commit mesajları İngilizce** olacak. Benimle sohbette Türkçe konuş.
- Yeni bir NuGet bağımlılığı eklemeden önce sor. Paket sürümlerini ezbere yazma; güncel kararlı sürümü kontrol et.
- Public API'yi minimum tut. Public olan her şeyin XML doc'u olsun. Gerekmeyen her şey `internal`.
- Her davranış değişikliği test ile gelsin. `dotnet build` ve `dotnet test` temiz geçmeden bir işi bitmiş sayma. Uyarıları (warnings) hata gibi ele al.
- Reflection hileleri, EF Core internal API'leri (`Microsoft.EntityFrameworkCore.Internal`) ve `#pragma` ile uyarı bastırma kullanma. Zorunlu kalırsan önce sor.
- Runtime kütüphanesi production'da kapalıyken **sıfıra yakın maliyetli** olmalı; açıkken de maliyeti ölçülmüş olmalı (Faz 4'te benchmark).
- NuGet'e veya GitHub'a hiçbir şey **yayınlama/push etme**. Yayınlamayı ben yapacağım.

## Teknik kararlar

- Runtime kütüphaneleri: `net8.0;net10.0` multi-target (EF Core 8 ve EF Core 10 için koşullu paket referansları).
- Analyzer: `netstandard2.0` (Roslyn analyzer zorunluluğu), `Microsoft.CodeAnalysis.CSharp`.
- Test: xUnit. EF testleri için **SQLite in-memory** (Docker gerektirmesin). Analyzer testleri için `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`.
- Benchmark: BenchmarkDotNet.
- Nullable reference types açık, `TreatWarningsAsErrors` açık, `Directory.Build.props` ve `Directory.Packages.props` (central package management) kullanılsın.

## Hedef klasör yapısı

```
/src
  NPlusGuard/                    # Çekirdek: interceptor, scope, tespit mantığı, raporlama
  NPlusGuard.AspNetCore/         # Middleware, DI uzantıları, response header
  NPlusGuard.Analyzers/          # Roslyn analyzer (netstandard2.0)
/tests
  NPlusGuard.Tests/
  NPlusGuard.AspNetCore.Tests/
  NPlusGuard.Analyzers.Tests/
/samples
  NPlusGuard.Sample.Api/         # Kasıtlı N+1 içeren ve düzeltilmiş endpoint'li örnek API
/benchmarks
  NPlusGuard.Benchmarks/
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

## Hedeflenen kullanım (taslak, Faz 0'da netleştirilecek)

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

## Yayınlamadan önce kendi projemde deneme

Faz 4 sonunda şu iki yöntem için README'ye "Local testing" bölümü ekle ve bana adımları göster:

1. **Local NuGet feed:** `dotnet pack -c Release -o ./nupkg`, ardından hedef projede `dotnet nuget add source <yol>/nupkg -n NPlusGuardLocal` ve önizleme sürümüyle (`0.1.0-preview.1`) paket ekleme.
2. **Project reference:** Runtime için normal `ProjectReference`; analyzer için `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` ile referans.

## Kapsam dışı (şimdilik)

- EF Core dışındaki ORM'ler (Dapper vb.).
- Code fix provider'lar.
- Dashboard/UI.
- Otomatik sorgu düzeltme.
