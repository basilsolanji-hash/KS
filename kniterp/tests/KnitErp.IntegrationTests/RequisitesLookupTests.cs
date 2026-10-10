using System.Net;
using System.Text;
using KnitErp.Application.Workspace;
using KnitErp.Domain.Common;
using KnitErp.Infrastructure.External;

namespace KnitErp.IntegrationTests;

/// <summary>Заполнение реквизитов по ИНН (D78): разбор ответа DaData на подставных данных — без сети и без ключа.</summary>
public sealed class RequisitesLookupTests
{
    private sealed class StubHandler(HttpStatusCode code, string json) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Company_and_sole_proprietor_are_parsed()
    {
        const string company = """
            {"suggestions":[{"value":"ООО «ТЕСТ»","data":{"inn":"7707083893","kpp":"773601001","ogrn":"1027700132195","type":"LEGAL",
              "name":{"full_with_opf":"ОБЩЕСТВО С ОГРАНИЧЕННОЙ ОТВЕТСТВЕННОСТЬЮ «ТЕСТ»","short_with_opf":"ООО «ТЕСТ»"},
              "management":{"name":"Тестов Тест Тестович","post":"ГЕНЕРАЛЬНЫЙ ДИРЕКТОР"},
              "address":{"value":"г Москва, ул Тестовая, д 1","unrestricted_value":"117997, г Москва, ул Тестовая, д 1"},
              "state":{"status":"ACTIVE"}}}]}
            """;
        var handler = new StubHandler(HttpStatusCode.OK, company);
        var lookup = new DaDataRequisitesLookup(new HttpClient(handler), new RequisitesLookupOptions("test-key"));
        var r = (await lookup.FindByInnAsync("7707083893"))!;
        Assert.Equal((false, "ООО «ТЕСТ»", "773601001", "1027700132195", "117997, г Москва, ул Тестовая, д 1", "Тестов Тест Тестович", true),
            (r.SoleProprietor, r.ShortName, r.Kpp, r.Ogrn, r.Address, r.DirectorName, r.Active));
        Assert.Equal("Token test-key", handler.Request!.Headers.Authorization!.ToString());

        const string sole = """
            {"suggestions":[{"data":{"inn":"500100732259","kpp":null,"ogrn":"304500116000157","type":"INDIVIDUAL",
              "name":{"full_with_opf":"Индивидуальный предприниматель Петров Пётр Петрович","short_with_opf":"ИП Петров Пётр Петрович"},
              "fio":{"surname":"Петров","name":"Пётр","patronymic":"Петрович"},"state":{"status":"LIQUIDATED"}}}]}
            """;
        r = (await new DaDataRequisitesLookup(new HttpClient(new StubHandler(HttpStatusCode.OK, sole)), new RequisitesLookupOptions("k"))
            .FindByInnAsync("500100732259"))!;
        Assert.Equal((true, "Петров Пётр Петрович", null, false, "LIQUIDATED"), (r.SoleProprietor, r.DirectorName, r.DirectorPosition, r.Active, r.Status));
    }

    [Fact]
    public async Task Empty_answer_and_errors()
    {
        var empty = new DaDataRequisitesLookup(new HttpClient(new StubHandler(HttpStatusCode.OK, """{"suggestions":[]}""")), new RequisitesLookupOptions("k"));
        Assert.Null(await empty.FindByInnAsync("7707083893"));
        var down = new DaDataRequisitesLookup(new HttpClient(new StubHandler(HttpStatusCode.Forbidden, "{}")), new RequisitesLookupOptions("k"));
        await Assert.ThrowsAsync<ExternalServiceUnavailableException>(() => down.FindByInnAsync("7707083893"));
        Assert.False(new DaDataRequisitesLookup(new HttpClient(), new RequisitesLookupOptions(" ")).IsConfigured);
    }
}
