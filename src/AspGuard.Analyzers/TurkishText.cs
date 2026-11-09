using System.Text;

namespace AspGuard.Analyzers;

/// <summary>
/// Turkish summaries. Follows the bare style "Kişi ekler." for a singular object, uses the accusative for plurals
/// ("Kullanıcıları getirir."), compounds noun groups ("sipariş öğesi") and turns prepositions into case suffixes or
/// postpositions ("Sepete öğe ekler.", "Kimliğe göre kullanıcı getirir.").
/// </summary>
internal static class TurkishText
{
    private enum Case
    {
        Nominative,
        Accusative,
        Dative,
        Ablative,
        Locative,
    }

    /// <summary>English verb → (Turkish stem, third-person aorist).</summary>
    internal static readonly Dictionary<string, (string Stem, string Aorist)> Verbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["add"] = ("ekle", "ekler"), ["insert"] = ("ekle", "ekler"), ["append"] = ("ekle", "ekler"), ["attach"] = ("ekle", "ekler"),
        ["create"] = ("oluştur", "oluşturur"), ["build"] = ("oluştur", "oluşturur"), ["generate"] = ("üret", "üretir"),
        ["delete"] = ("sil", "siler"), ["remove"] = ("kaldır", "kaldırır"), ["detach"] = ("ayır", "ayırır"),
        ["update"] = ("güncelle", "günceller"), ["edit"] = ("düzenle", "düzenler"), ["modify"] = ("değiştir", "değiştirir"),
        ["change"] = ("değiştir", "değiştirir"), ["replace"] = ("değiştir", "değiştirir"), ["rename"] = ("yeniden adlandır", "yeniden adlandırır"),
        ["get"] = ("getir", "getirir"), ["fetch"] = ("getir", "getirir"), ["retrieve"] = ("getir", "getirir"), ["read"] = ("oku", "okur"),
        ["find"] = ("bul", "bulur"), ["search"] = ("ara", "arar"), ["lookup"] = ("ara", "arar"), ["list"] = ("listele", "listeler"),
        ["load"] = ("yükle", "yükler"), ["upload"] = ("yükle", "yükler"), ["download"] = ("indir", "indirir"),
        ["save"] = ("kaydet", "kaydeder"), ["store"] = ("sakla", "saklar"), ["persist"] = ("kaydet", "kaydeder"), ["register"] = ("kaydet", "kaydeder"),
        ["log"] = ("kaydet", "kaydeder"), ["write"] = ("yaz", "yazar"), ["print"] = ("yazdır", "yazdırır"),
        ["set"] = ("ayarla", "ayarlar"), ["configure"] = ("yapılandır", "yapılandırır"), ["setup"] = ("kur", "kurar"), ["install"] = ("kur", "kurar"),
        ["check"] = ("kontrol et", "kontrol eder"), ["validate"] = ("doğrula", "doğrular"), ["verify"] = ("doğrula", "doğrular"),
        ["ensure"] = ("sağla", "sağlar"), ["confirm"] = ("onayla", "onaylar"), ["approve"] = ("onayla", "onaylar"),
        ["reject"] = ("reddet", "reddeder"), ["decline"] = ("reddet", "reddeder"), ["accept"] = ("kabul et", "kabul eder"),
        ["cancel"] = ("iptal et", "iptal eder"), ["refund"] = ("iade et", "iade eder"), ["pay"] = ("öde", "öder"),
        ["charge"] = ("ücretlendir", "ücretlendirir"), ["transfer"] = ("aktar", "aktarır"), ["submit"] = ("gönder", "gönderir"),
        ["send"] = ("gönder", "gönderir"), ["notify"] = ("bildir", "bildirir"), ["publish"] = ("yayınla", "yayınlar"),
        ["subscribe"] = ("abone ol", "abone olur"), ["unsubscribe"] = ("aboneliği iptal et", "aboneliği iptal eder"), ["invite"] = ("davet et", "davet eder"),
        ["calculate"] = ("hesapla", "hesaplar"), ["compute"] = ("hesapla", "hesaplar"), ["count"] = ("say", "sayar"),
        ["sum"] = ("topla", "toplar"), ["increase"] = ("artır", "artırır"), ["increment"] = ("artır", "artırır"),
        ["decrease"] = ("azalt", "azaltır"), ["decrement"] = ("azalt", "azaltır"), ["compare"] = ("karşılaştır", "karşılaştırır"),
        ["evaluate"] = ("değerlendir", "değerlendirir"), ["convert"] = ("dönüştür", "dönüştürür"), ["transform"] = ("dönüştür", "dönüştürür"),
        ["map"] = ("eşle", "eşler"), ["parse"] = ("ayrıştır", "ayrıştırır"), ["format"] = ("biçimlendir", "biçimlendirir"),
        ["serialize"] = ("serileştir", "serileştirir"), ["deserialize"] = ("seriden çöz", "seriden çözer"), ["encode"] = ("kodla", "kodlar"),
        ["decode"] = ("çöz", "çözer"), ["encrypt"] = ("şifrele", "şifreler"), ["decrypt"] = ("şifresini çöz", "şifresini çözer"),
        ["hash"] = ("özetle", "özetler"), ["sign"] = ("imzala", "imzalar"), ["process"] = ("işle", "işler"), ["handle"] = ("işle", "işler"),
        ["execute"] = ("çalıştır", "çalıştırır"), ["run"] = ("çalıştır", "çalıştırır"), ["invoke"] = ("çağır", "çağırır"), ["call"] = ("çağır", "çağırır"),
        ["start"] = ("başlat", "başlatır"), ["begin"] = ("başlat", "başlatır"), ["initialize"] = ("başlat", "başlatır"), ["init"] = ("başlat", "başlatır"),
        ["stop"] = ("durdur", "durdurur"), ["pause"] = ("duraklat", "duraklatır"), ["resume"] = ("sürdür", "sürdürür"),
        ["finish"] = ("bitir", "bitirir"), ["complete"] = ("tamamla", "tamamlar"), ["end"] = ("sonlandır", "sonlandırır"),
        ["reset"] = ("sıfırla", "sıfırlar"), ["clear"] = ("temizle", "temizler"), ["clean"] = ("temizle", "temizler"), ["purge"] = ("temizle", "temizler"),
        ["sort"] = ("sırala", "sıralar"), ["order"] = ("sırala", "sıralar"), ["filter"] = ("filtrele", "filtreler"), ["group"] = ("grupla", "gruplar"),
        ["merge"] = ("birleştir", "birleştirir"), ["join"] = ("birleştir", "birleştirir"), ["combine"] = ("birleştir", "birleştirir"),
        ["split"] = ("böl", "böler"), ["copy"] = ("kopyala", "kopyalar"), ["clone"] = ("kopyala", "kopyalar"), ["duplicate"] = ("çoğalt", "çoğaltır"),
        ["move"] = ("taşı", "taşır"), ["open"] = ("aç", "açar"), ["close"] = ("kapat", "kapatır"), ["lock"] = ("kilitle", "kilitler"),
        ["unlock"] = ("kilidini aç", "kilidini açar"), ["block"] = ("engelle", "engeller"), ["unblock"] = ("engeli kaldır", "engeli kaldırır"),
        ["ban"] = ("yasakla", "yasaklar"), ["archive"] = ("arşivle", "arşivler"), ["restore"] = ("geri yükle", "geri yükler"),
        ["undo"] = ("geri al", "geri alır"), ["redo"] = ("yinele", "yineler"), ["retry"] = ("yeniden dene", "yeniden dener"),
        ["assign"] = ("ata", "atar"), ["unassign"] = ("atamayı kaldır", "atamayı kaldırır"), ["apply"] = ("uygula", "uygular"),
        ["enable"] = ("etkinleştir", "etkinleştirir"), ["activate"] = ("etkinleştir", "etkinleştirir"),
        ["disable"] = ("devre dışı bırak", "devre dışı bırakır"), ["deactivate"] = ("devre dışı bırak", "devre dışı bırakır"),
        ["show"] = ("göster", "gösterir"), ["display"] = ("göster", "gösterir"), ["render"] = ("görüntüle", "görüntüler"), ["view"] = ("görüntüle", "görüntüler"),
        ["hide"] = ("gizle", "gizler"), ["refresh"] = ("yenile", "yeniler"), ["reload"] = ("yeniden yükle", "yeniden yükler"),
        ["renew"] = ("yenile", "yeniler"), ["sync"] = ("eşitle", "eşitler"), ["synchronize"] = ("eşitle", "eşitler"),
        ["import"] = ("içe aktar", "içe aktarır"), ["export"] = ("dışa aktar", "dışa aktarır"), ["link"] = ("bağla", "bağlar"),
        ["connect"] = ("bağla", "bağlar"), ["disconnect"] = ("bağlantıyı kes", "bağlantıyı keser"), ["unlink"] = ("bağlantıyı kaldır", "bağlantıyı kaldırır"),
        ["login"] = ("oturum aç", "oturum açar"), ["signin"] = ("oturum aç", "oturum açar"), ["logout"] = ("oturumu kapat", "oturumu kapatır"),
        ["signout"] = ("oturumu kapat", "oturumu kapatır"), ["authenticate"] = ("kimlik doğrula", "kimlik doğrular"),
        ["authorize"] = ("yetkilendir", "yetkilendirir"), ["resolve"] = ("çöz", "çözer"), ["schedule"] = ("planla", "planlar"),
        ["plan"] = ("planla", "planlar"), ["track"] = ("izle", "izler"), ["monitor"] = ("izle", "izler"), ["watch"] = ("izle", "izler"),
        ["mark"] = ("işaretle", "işaretler"), ["select"] = ("seç", "seçer"), ["choose"] = ("seç", "seçer"), ["prepare"] = ("hazırla", "hazırlar"),
        ["release"] = ("serbest bırak", "serbest bırakır"), ["dispose"] = ("serbest bırak", "serbest bırakır"), ["fill"] = ("doldur", "doldurur"),
        ["cache"] = ("önbelleğe al", "önbelleğe alır"), ["reserve"] = ("rezerve et", "rezerve eder"), ["return"] = ("döndür", "döndürür"),
        ["receive"] = ("al", "alır"), ["measure"] = ("ölç", "ölçer"), ["test"] = ("test et", "test eder"),
        ["share"] = ("paylaş", "paylaşır"), ["rate"] = ("puanla", "puanlar"), ["review"] = ("incele", "inceler"), ["sanitize"] = ("temizle", "temizler"),
        ["normalize"] = ("normalleştir", "normalleştirir"), ["seed"] = ("başlangıç verisi ekle", "başlangıç verisi ekler"), ["migrate"] = ("taşı", "taşır"),
        ["queue"] = ("kuyruğa ekle", "kuyruğa ekler"), ["enqueue"] = ("kuyruğa ekle", "kuyruğa ekler"), ["dequeue"] = ("kuyruktan al", "kuyruktan alır"),
        ["push"] = ("ekle", "ekler"), ["pop"] = ("çıkar", "çıkarır"), ["extract"] = ("çıkar", "çıkarır"), ["collect"] = ("topla", "toplar"),
        ["upsert"] = ("ekle veya güncelle", "ekler veya günceller"),     };

    private sealed class Word(string text, bool adjective = false, string? oblique = null, bool front = false, bool plural = false)
    {
        public string Text { get; } = text;

        public bool Adjective { get; } = adjective;

        /// <summary>Stem before a vowel-initial suffix when it is irregular (kayıt → kayd, renk → reng).</summary>
        public string? Oblique { get; } = oblique;

        /// <summary>Takes front-vowel suffixes despite a back vowel (rol → rolü).</summary>
        public bool Front { get; } = front;

        /// <summary>The Turkish text is already plural (people → kişiler).</summary>
        public bool Plural { get; } = plural;
    }

    private static Word N(string text, string? oblique = null, bool front = false) => new(text, oblique: oblique, front: front);

    private static Word A(string text) => new(text, adjective: true);

    private static readonly Dictionary<string, Word> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        // People and organisations.
        ["person"] = N("kişi"), ["people"] = new("kişiler", plural: true), ["user"] = N("kullanıcı"), ["customer"] = N("müşteri"),
        ["client"] = N("istemci"), ["employee"] = N("çalışan"), ["staff"] = N("personel"), ["member"] = N("üye"), ["admin"] = N("yönetici"),
        ["manager"] = N("yönetici"), ["owner"] = N("sahip"), ["author"] = N("yazar"), ["student"] = N("öğrenci"), ["teacher"] = N("öğretmen"),
        ["patient"] = N("hasta"), ["doctor"] = N("doktor"), ["guest"] = N("misafir"), ["child"] = N("çocuk", "çocuğ"),
        ["children"] = new("çocuklar", plural: true), ["parent"] = N("ebeveyn"), ["family"] = N("aile"), ["contact"] = N("kişi"),
        ["company"] = N("şirket"), ["department"] = N("departman"), ["team"] = N("takım"), ["group"] = N("grup"), ["branch"] = N("şube"),
        ["supplier"] = N("tedarikçi"), ["vendor"] = N("satıcı"), ["seller"] = N("satıcı"), ["buyer"] = N("alıcı"), ["agent"] = N("acente"),
        ["broker"] = N("aracı"), ["beneficiary"] = N("lehtar"), ["insured"] = N("sigortalı"), ["role"] = N("rol", front: true),
        ["tenant"] = N("kiracı"), ["organization"] = N("kurum"), ["partner"] = N("iş ortağı"),

        // Commerce and finance.
        ["order"] = N("sipariş"), ["product"] = N("ürün"), ["item"] = N("öğe"), ["cart"] = N("sepet"), ["basket"] = N("sepet"),
        ["invoice"] = N("fatura"), ["payment"] = N("ödeme"), ["account"] = N("hesap"), ["price"] = N("fiyat"), ["stock"] = N("stok"),
        ["inventory"] = N("envanter"), ["warehouse"] = N("depo"), ["shipment"] = N("sevkiyat"), ["delivery"] = N("teslimat"), ["cargo"] = N("kargo"),
        ["discount"] = N("indirim"), ["coupon"] = N("kupon"), ["tax"] = N("vergi"), ["currency"] = N("para birimi"), ["amount"] = N("tutar"),
        ["balance"] = N("bakiye"), ["transaction"] = N("işlem"), ["refund"] = N("iade"), ["salary"] = N("maaş"), ["brand"] = N("marka"),
        ["category"] = N("kategori"), ["store"] = N("mağaza"), ["shop"] = N("mağaza"), ["contract"] = N("sözleşme"), ["quote"] = N("teklif"),
        ["offer"] = N("teklif"), ["proposal"] = N("teklif"), ["subscription"] = N("abonelik"), ["plan"] = N("plan"), ["license"] = N("lisans"),
        ["budget"] = N("bütçe"), ["expense"] = N("gider"), ["income"] = N("gelir"), ["revenue"] = N("gelir"), ["cost"] = N("maliyet"), ["fee"] = N("ücret"),
        ["card"] = N("kart"), ["bank"] = N("banka"), ["loan"] = N("kredi"), ["credit"] = N("kredi"), ["debt"] = N("borç"),

        // Insurance.
        ["claim"] = N("tazminat"), ["policy"] = N("poliçe"), ["insurance"] = N("sigorta"), ["premium"] = N("prim"), ["coverage"] = N("teminat"),
        ["damage"] = N("hasar"), ["accident"] = N("kaza"), ["vehicle"] = N("araç"), ["car"] = N("araba"), ["risk"] = N("risk"),

        // Content and communication.
        ["message"] = N("mesaj"), ["notification"] = N("bildirim"), ["comment"] = N("yorum"), ["post"] = N("gönderi"), ["article"] = N("makale"),
        ["blog"] = N("blog"), ["page"] = N("sayfa"), ["image"] = N("görsel"), ["photo"] = N("fotoğraf"), ["video"] = N("video"), ["file"] = N("dosya"),
        ["folder"] = N("klasör"), ["document"] = N("belge"), ["attachment"] = N("ek"), ["report"] = N("rapor"), ["note"] = N("not"),
        ["tag"] = N("etiket"), ["review"] = N("değerlendirme"), ["rating"] = N("puan"), ["question"] = N("soru"), ["answer"] = N("cevap"),
        ["email"] = N("e-posta"), ["mail"] = N("posta"), ["sms"] = N("SMS"), ["template"] = N("şablon"), ["book"] = N("kitap"), ["course"] = N("kurs"),
        ["lesson"] = N("ders"), ["exam"] = N("sınav"), ["menu"] = N("menü"), ["link"] = N("bağlantı"), ["title"] = N("başlık"), ["description"] = N("açıklama"),
        ["content"] = N("içerik"), ["text"] = N("metin", "metn"), ["language"] = N("dil"), ["translation"] = N("çeviri"),

        // Time and scheduling.
        ["appointment"] = N("randevu"), ["meeting"] = N("toplantı"), ["event"] = N("etkinlik"), ["calendar"] = N("takvim"), ["reminder"] = N("hatırlatıcı"),
        ["schedule"] = N("program"), ["task"] = N("görev"), ["job"] = N("iş"), ["shift"] = N("vardiya"), ["holiday"] = N("tatil"), ["leave"] = N("izin"),
        ["date"] = N("tarih"), ["time"] = N("zaman"), ["day"] = N("gün"), ["month"] = N("ay"), ["year"] = N("yıl"), ["week"] = N("hafta"),
        ["reservation"] = N("rezervasyon"), ["booking"] = N("rezervasyon"), ["flight"] = N("uçuş"), ["hotel"] = N("otel"), ["room"] = N("oda"),
        ["ticket"] = N("bilet"), ["project"] = N("proje"),

        // Places.
        ["address"] = N("adres"), ["location"] = N("konum"), ["country"] = N("ülke"), ["city"] = N("şehir", "şehr"), ["region"] = N("bölge"), ["area"] = N("alan"),

        // Technical.
        ["id"] = N("kimlik"), ["key"] = N("anahtar"), ["value"] = N("değer"), ["name"] = N("ad"), ["code"] = N("kod"), ["type"] = N("tür"),
        ["status"] = N("durum"), ["state"] = N("durum"), ["detail"] = N("detay"), ["info"] = N("bilgi"), ["information"] = N("bilgi"), ["data"] = N("veri"),
        ["record"] = N("kayıt", "kayd"), ["log"] = N("günlük"), ["list"] = N("liste"), ["number"] = N("numara"),
        ["phone"] = N("telefon"), ["password"] = N("parola"), ["token"] = N("token"), ["session"] = N("oturum"), ["permission"] = N("izin"),
        ["setting"] = N("ayar"), ["settings"] = new("ayarlar", plural: true), ["config"] = N("yapılandırma"), ["configuration"] = N("yapılandırma"),
        ["option"] = N("seçenek"), ["profile"] = N("profil"), ["avatar"] = N("avatar"), ["feature"] = N("özellik"), ["module"] = N("modül"),
        ["service"] = N("hizmet"), ["request"] = N("talep"), ["response"] = N("yanıt"), ["result"] = N("sonuç"), ["error"] = N("hata"),
        ["exception"] = N("hata"), ["warning"] = N("uyarı"), ["history"] = N("geçmiş"), ["audit"] = N("denetim"), ["database"] = N("veritabanı"),
        ["table"] = N("tablo"), ["row"] = N("satır"), ["column"] = N("sütun"), ["cache"] = N("önbellek"), ["queue"] = N("kuyruk", "kuyruğ"),
        ["query"] = N("sorgu"), ["filter"] = N("filtre"), ["search"] = N("arama"), ["color"] = N("renk", "reng"), ["size"] = N("boyut"),
        ["weight"] = N("ağırlık"), ["quantity"] = N("miktar"), ["unit"] = N("birim"), ["model"] = N("model"), ["score"] = N("skor"),
        ["level"] = N("seviye"), ["game"] = N("oyun"), ["verification"] = N("doğrulama"), ["version"] = N("sürüm"),
        ["entity"] = N("varlık"), ["entry"] = N("kayıt", "kayd"), ["count"] = N("sayı"), ["url"] = N("adres"),
        ["path"] = N("yol"), ["change"] = N("değişiklik"), ["catalog"] = N("katalog"), ["device"] = N("cihaz"), ["step"] = N("adım"),

        // Adjectives.
        ["all"] = A("tüm"), ["active"] = A("aktif"), ["inactive"] = A("pasif"), ["new"] = A("yeni"), ["old"] = A("eski"), ["current"] = A("mevcut"),
        ["first"] = A("ilk"), ["last"] = A("son"), ["latest"] = A("en son"), ["recent"] = A("son"), ["pending"] = A("bekleyen"),
        ["approved"] = A("onaylanmış"), ["deleted"] = A("silinmiş"), ["expired"] = A("süresi dolmuş"), ["default"] = A("varsayılan"),
        ["public"] = A("herkese açık"), ["private"] = A("özel"), ["unread"] = A("okunmamış"), ["completed"] = A("tamamlanmış"),
        ["available"] = A("uygun"), ["paid"] = A("ödenmiş"), ["unpaid"] = A("ödenmemiş"), ["daily"] = A("günlük"), ["weekly"] = A("haftalık"),
        ["monthly"] = A("aylık"), ["yearly"] = A("yıllık"), ["main"] = A("ana"), ["next"] = A("sonraki"), ["previous"] = A("önceki"),
        ["random"] = A("rastgele"), ["valid"] = A("geçerli"), ["invalid"] = A("geçersiz"), ["empty"] = A("boş"), ["full"] = A("tam"),
        ["open"] = A("açık"), ["closed"] = A("kapalı"), ["visible"] = A("görünür"), ["hidden"] = A("gizli"), ["enabled"] = A("etkin"),
        ["disabled"] = A("devre dışı"), ["online"] = A("çevrim içi"), ["offline"] = A("çevrim dışı"), ["unique"] = A("benzersiz"),
        ["selected"] = A("seçili"), ["favorite"] = A("favori"), ["popular"] = A("popüler"), ["top"] = A("en iyi"), ["total"] = A("toplam"),
    };

    private const string BackVowels = "aıou";
    private const string FrontVowels = "eiöü";
    private const string HardConsonants = "fstkçşhp";

    public static string Method(List<string> words)
    {
        var first = words[0].ToLowerInvariant();
        var rest = words.Skip(1).ToList();

        switch (first)
        {
            case "is" when rest.Count > 0:
                return $"{Build(rest).Text} olup olmadığını belirtir";
            case "has" when rest.Count > 0:
                return $"{Build(rest).Text} içerip içermediğini belirtir";
            case "can" when rest.Count > 0:
                return $"{Action(rest)} işleminin yapılıp yapılamayacağını belirtir";
            case "should" when rest.Count > 0:
                return $"{Action(rest)} işleminin yapılması gerekip gerekmediğini belirtir";
            case "on" when rest.Count > 0:
                return $"{Build(rest).Text} olayını işler";
            case "try" when rest.Count > 0 && Verbs.ContainsKey(rest[0]):
                return $"{Action(rest)}{Harmony4(Gerund(Verbs[rest[0]].Stem), "y")} dener";
            case "to" when rest.Count > 0:
                return $"{Build(rest).Text} biçimine dönüştürür";
        }

        if (!Verbs.TryGetValue(first, out var verb))
        {
            return $"{Build(words).Text} işlemini gerçekleştirir";
        }

        // "AddOrUpdateUser" → "kullanıcı ekler veya günceller".
        if (rest.Count >= 2 && rest[0].ToLowerInvariant() is "or" or "and" && Verbs.TryGetValue(rest[1], out var second))
        {
            var conjunction = rest[0].ToLowerInvariant() == "or" ? "veya" : "ve";
            return Join(Complements(rest.Skip(2).ToList()), $"{verb.Aorist} {conjunction} {second.Aorist}");
        }

        return Join(Complements(rest), verb.Aorist);
    }

    /// <summary>"DeleteUser" after can/should/try → "kullanıcı silme".</summary>
    private static string Action(List<string> words)
    {
        if (!Verbs.TryGetValue(words[0], out var verb))
        {
            return Build(words).Text;
        }

        return Join(Complements(words.Skip(1).ToList()), Gerund(verb.Stem));
    }

    private static string Gerund(string stem) => stem + (LastVowelIsBack(stem, false) ? "ma" : "me");

    /// <summary>Prepositional phrases first, then the object: "kimliğe göre kullanıcı".</summary>
    private static string Complements(List<string> words)
    {
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var (obj, phrases) = DocumentationText.Parse(words, 0);
        var parts = new List<string>();
        foreach (var (preposition, phraseWords) in phrases)
        {
            var phrase = Build(phraseWords);
            parts.Add(preposition switch
            {
                "by" => Apply(phrase, Case.Dative) + " göre",
                "for" => phrase.Text + " için",
                "with" => phrase.Text + " ile",
                "from" => Apply(phrase, Case.Ablative),
                "to" or "into" => Apply(phrase, Case.Dative),
                _ => Apply(phrase, Case.Locative),
            });
        }

        if (obj.Count > 0)
        {
            var phrase = Build(obj);
            // Unknown (English) words stay as written: no accusative glued onto them.
            parts.Add(phrase.Plural && phrase.Known ? Apply(phrase, Case.Accusative) : phrase.Text);
        }

        return string.Join(" ", parts);
    }

    private static string Join(string left, string right) => left.Length == 0 ? right : left + " " + right;

    private sealed class Phrase(string text, bool known, bool possessive, bool plural, Word? last)
    {
        public string Text { get; } = text;

        public bool Known { get; } = known;

        /// <summary>Ends in the compound (3rd person possessive) suffix: "sipariş öğesi".</summary>
        public bool Possessive { get; } = possessive;

        public bool Plural { get; } = plural;

        /// <summary>The last word when the phrase ends in a bare dictionary word (for irregular stems).</summary>
        public Word? Last { get; } = last;
    }

    private static Phrase Build(List<string> words)
    {
        var entries = new List<(Word? Word, bool Plural, string Raw)>();
        foreach (var raw in words)
        {
            entries.Add(Lookup(raw));
        }

        var plural = entries.Count > 0 && (entries[entries.Count - 1].Plural || entries[entries.Count - 1].Word?.Plural == true);
        if (entries.Any(e => e.Word is null))
        {
            var text = string.Join(" ", entries.Select(e => e.Word?.Text ?? (DocumentationText.IsAcronym(e.Raw) ? e.Raw : e.Raw.ToLowerInvariant())));
            return new Phrase(text, known: false, possessive: false, plural, last: null);
        }

        var lastEntry = entries[entries.Count - 1];
        var last = lastEntry.Word!;
        var nouns = entries.Count(e => !e.Word!.Adjective);
        var head = string.Join(" ", entries.Take(entries.Count - 1).Select(e => e.Word!.Text));

        string lastText;
        var possessive = false;
        Word? bare = null;
        if (!last.Adjective && nouns >= 2)
        {
            // Noun compound: "sipariş öğesi", plural "sipariş öğeleri".
            possessive = true;
            lastText = lastEntry.Plural ? Plural(last.Text, last.Front) + Harmony4Char(Plural(last.Text, last.Front), false) : Possessive(last);
        }
        else if (lastEntry.Plural && !last.Plural)
        {
            lastText = Plural(last.Text, last.Front);
        }
        else
        {
            lastText = last.Text;
            bare = last;
        }

        return new Phrase(head.Length == 0 ? lastText : head + " " + lastText, known: true, possessive, plural, bare);
    }

    private static (Word? Word, bool Plural, string Raw) Lookup(string raw)
    {
        if (Words.TryGetValue(raw, out var word))
        {
            return (word, false, raw);
        }

        foreach (var (suffix, replacement) in new[] { ("ies", "y"), ("ses", "s"), ("xes", "x"), ("ches", "ch"), ("shes", "sh"), ("s", string.Empty) })
        {
            if (raw.Length > suffix.Length + 1 && raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && Words.TryGetValue(raw.Substring(0, raw.Length - suffix.Length) + replacement, out word))
            {
                return (word, true, raw);
            }
        }

        return (null, raw.Length > 2 && raw.EndsWith("s", StringComparison.Ordinal) && !raw.EndsWith("ss", StringComparison.Ordinal), raw);
    }

    private static string Apply(Phrase phrase, Case @case)
    {
        if (@case == Case.Nominative)
        {
            return phrase.Text;
        }

        var text = phrase.Text;
        if (!phrase.Known)
        {
            // Unknown words keep their spelling; the suffix is attached with an apostrophe, as with proper names.
            return text + "'" + Suffix(text, @case, afterPossessive: false, front: false, out _);
        }

        if (phrase.Possessive)
        {
            return text + "n" + Suffix(text, @case, afterPossessive: true, front: false, out _);
        }

        var front = phrase.Last?.Front == true;
        var suffix = Suffix(text, @case, afterPossessive: false, front, out var vowelInitial);
        if (vowelInitial && phrase.Last is { } last)
        {
            text = text.Substring(0, text.Length - last.Text.Length) + VowelStem(last);
        }

        return text + suffix;
    }

    private static string Suffix(string text, Case @case, bool afterPossessive, bool front, out bool vowelInitial)
    {
        var endsWithVowel = text.Length > 0 && IsVowel(text[text.Length - 1]);
        var back = LastVowelIsBack(text, front);
        var two = back ? "a" : "e";
        var hard = text.Length > 0 && HardConsonants.IndexOf(text[text.Length - 1]) >= 0 && !afterPossessive;
        vowelInitial = false;
        switch (@case)
        {
            case Case.Accusative:
                vowelInitial = !endsWithVowel && !afterPossessive;
                return (endsWithVowel && !afterPossessive ? "y" : string.Empty) + Harmony4Char(text, front);
            case Case.Dative:
                vowelInitial = !endsWithVowel && !afterPossessive;
                return (endsWithVowel && !afterPossessive ? "y" : string.Empty) + two;
            case Case.Ablative:
                return (hard ? "t" : "d") + two + "n";
            default:
                return (hard ? "t" : "d") + two;
        }
    }

    private static string Plural(string word, bool front) => word + (LastVowelIsBack(word, front) ? "lar" : "ler");

    /// <summary>Compound suffix: öğe → öğesi, kimlik → kimliği, rol → rolü.</summary>
    private static string Possessive(Word word)
    {
        var text = word.Text;
        if (IsVowel(text[text.Length - 1]))
        {
            return text + "s" + Harmony4Char(text, word.Front);
        }

        return VowelStem(word) + Harmony4Char(text, word.Front);
    }

    /// <summary>Stem before a vowel-initial suffix: kitap → kitab, kimlik → kimliğ, kayıt → kayd.</summary>
    private static string VowelStem(Word word)
    {
        if (word.Oblique is not null)
        {
            return word.Oblique;
        }

        var text = word.Text;
        if (text.IndexOf(' ') >= 0 || text.Count(IsVowel) < 2)
        {
            return text;
        }

        return text[text.Length - 1] switch
        {
            'k' => text.Substring(0, text.Length - 1) + "ğ",
            'p' => text.Substring(0, text.Length - 1) + "b",
            'ç' => text.Substring(0, text.Length - 1) + "c",
            _ => text,
        };
    }

    private static string Harmony4(string text, string bufferAfterVowel) =>
        (text.Length > 0 && IsVowel(text[text.Length - 1]) ? bufferAfterVowel : string.Empty) + Harmony4Char(text, false);

    private static string Harmony4Char(string text, bool front)
    {
        var vowel = LastVowel(text);
        if (front)
        {
            return vowel is 'o' or 'u' or 'ö' or 'ü' ? "ü" : "i";
        }

        return vowel switch
        {
            'a' or 'ı' => "ı",
            'o' or 'u' => "u",
            'ö' or 'ü' => "ü",
            _ => "i",
        };
    }

    private static bool LastVowelIsBack(string text, bool front) => !front && BackVowels.IndexOf(LastVowel(text)) >= 0;

    private static char LastVowel(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = char.ToLowerInvariant(text[i]);
            if (IsVowel(c))
            {
                return c;
            }
        }

        return 'e';
    }

    private static bool IsVowel(char c) => BackVowels.IndexOf(c) >= 0 || FrontVowels.IndexOf(c) >= 0;
}
