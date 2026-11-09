namespace AspGuard.Analyzers.Tests;

public class DocumentationTextTests
{
    [Theory]
    [InlineData("AddPerson", "Kişi ekler.")]
    [InlineData("DeleteClaim", "Tazminat siler.")]
    [InlineData("UpdatePolicy", "Poliçe günceller.")]
    [InlineData("GetUsers", "Kullanıcıları getirir.")]
    [InlineData("GetAllUsers", "Tüm kullanıcıları getirir.")]
    [InlineData("GetActiveCustomers", "Aktif müşterileri getirir.")]
    [InlineData("GetUserById", "Kimliğe göre kullanıcı getirir.")]
    [InlineData("GetOrdersByCustomerId", "Müşteri kimliğine göre siparişleri getirir.")]
    [InlineData("AddItemToCart", "Sepete öğe ekler.")]
    [InlineData("RemoveUserFromGroup", "Gruptan kullanıcı kaldırır.")]
    [InlineData("RemoveItemFromCart", "Sepetten öğe kaldırır.")]
    [InlineData("SearchProductsInCatalog", "Katalogda ürünleri arar.")]
    [InlineData("GetOrdersForCustomer", "Müşteri için siparişleri getirir.")]
    [InlineData("CreateOrderItem", "Sipariş öğesi oluşturur.")]
    [InlineData("GetOrderItems", "Sipariş öğelerini getirir.")]
    [InlineData("GetUserRole", "Kullanıcı rolü getirir.")]
    [InlineData("GetPriceOfProduct", "Ürün fiyatı getirir.")]
    [InlineData("SaveChangesAsync", "Değişiklikleri kaydeder.")]
    [InlineData("Save", "Kaydeder.")]
    [InlineData("SendEmail", "E-posta gönderir.")]
    [InlineData("CheckPermission", "İzin kontrol eder.")]
    [InlineData("AddOrUpdateUser", "Kullanıcı ekler veya günceller.")]
    [InlineData("IsActive", "Aktif olup olmadığını belirtir.")]
    [InlineData("HasPermission", "İzin içerip içermediğini belirtir.")]
    [InlineData("CanDeleteUser", "Kullanıcı silme işleminin yapılıp yapılamayacağını belirtir.")]
    [InlineData("TryGetUser", "Kullanıcı getirmeyi dener.")]
    [InlineData("TryParseDate", "Tarih ayrıştırmayı dener.")]
    [InlineData("OnPaymentReceived", "Ödeme received olayını işler.")]
    [InlineData("get_user_by_id", "Kimliğe göre kullanıcı getirir.")]
    [InlineData("ExportPDFReport", "PDF rapor dışa aktarır.")]
    [InlineData("ApproveClaimAsync", "Tazminat onaylar.")]
    [InlineData("CalculateTotalPremium", "Toplam prim hesaplar.")]
    [InlineData("Frobnicate", "Frobnicate işlemini gerçekleştirir.")]
    [InlineData("SyncWidgetsToServer", "Server'e widgets eşitler.")]
    public void Turkish_summaries(string name, string expected) =>
        Assert.Equal(expected, DocumentationText.ForMethod(name, DocumentationText.Turkish));

    [Theory]
    [InlineData("AddPerson", "Adds the person.")]
    [InlineData("DeleteClaim", "Deletes the claim.")]
    [InlineData("GetUserById", "Gets the user by id.")]
    [InlineData("GetAllUsers", "Gets all users.")]
    [InlineData("AddItemToCart", "Adds the item to cart.")]
    [InlineData("SearchProducts", "Searches the products.")]
    [InlineData("ApplyDiscount", "Applies the discount.")]
    [InlineData("AddOrUpdateUser", "Adds or updates the user.")]
    [InlineData("IsActive", "Determines whether it is active.")]
    [InlineData("IsUserActive", "Determines whether the user is active.")]
    [InlineData("HasPermission", "Determines whether it has permission.")]
    [InlineData("CanDeleteUser", "Determines whether it can delete the user.")]
    [InlineData("TryGetUser", "Tries to get the user.")]
    [InlineData("OnClick", "Handles the click event.")]
    [InlineData("ToDto", "Converts to dto.")]
    [InlineData("SaveAsync", "Saves.")]
    [InlineData("ExportPDFReport", "Exports the PDF report.")]
    [InlineData("Frobnicate", "Performs the frobnicate operation.")]
    public void English_summaries(string name, string expected) =>
        Assert.Equal(expected, DocumentationText.ForMethod(name, DocumentationText.English));

    [Theory]
    [InlineData("HTTPClientFactory", new[] { "HTTP", "Client", "Factory" })]
    [InlineData("getUserById", new[] { "get", "User", "By", "Id" })]
    [InlineData("Load2FACodes", new[] { "Load2", "FA", "Codes" })]
    [InlineData("save_all_changes", new[] { "save", "all", "changes" })]
    public void Identifiers_are_split_into_words(string name, string[] expected) =>
        Assert.Equal(expected, DocumentationText.Split(name));

    [Fact]
    public void Constructor_summaries_reference_the_type()
    {
        Assert.Equal("<see cref=\"Repository{T}\"/> sınıfının yeni bir örneğini oluşturur.", DocumentationText.ForConstructor("Repository{T}", "class", "tr"));
        Assert.Equal("Initializes a new instance of the <see cref=\"Point\"/> struct.", DocumentationText.ForConstructor("Point", "struct", "en"));
    }

    [Fact]
    public void Xml_special_characters_are_escaped() =>
        Assert.DoesNotContain("<", DocumentationText.ForMethod("Get<T>", DocumentationText.English)!.Replace("&lt;", string.Empty));
}
