using DoubleMark.Crpt;
using FluentAssertions;

namespace DoubleMark.Core.Tests.Crpt;

public class CrptSuzRequestBuilderTests
{
    [Fact]
    public void BuildOrderBody_IncludesRequiredSuzFields()
    {
        var body = CrptSuzRequestBuilder.BuildOrderBody(
            productGroup: "chemistry",
            gtin: "00000000000000",
            quantity: 10,
            contactPerson: "Test Contact",
            templateId: 46);

        body["productGroup"].Should().Be("chemistry");

        var attributes = body["attributes"].Should().BeOfType<Dictionary<string, object>>().Subject;
        attributes["releaseMethodType"].Should().Be("PRODUCTION");
        attributes["createMethodType"].Should().Be("SELF_MADE");
        attributes["contactPerson"].Should().Be("Test Contact");

        var products = body["products"].Should().BeAssignableTo<object[]>().Subject;
        products.Should().HaveCount(1);

        var product = products[0].Should().BeOfType<Dictionary<string, object>>().Subject;
        product["gtin"].Should().Be("00000000000000");
        product["quantity"].Should().Be(10);
        product["serialNumberType"].Should().Be("OPERATOR");
        product["cisType"].Should().Be("UNIT");
        product["templateId"].Should().Be(46);
    }

    [Fact]
    public void BuildOrderBody_OmitsTemplateIdWhenNull()
    {
        var body = CrptSuzRequestBuilder.BuildOrderBody(
            "chemistry",
            "00000000000000",
            1,
            "Test Contact");

        var products = body["products"].Should().BeAssignableTo<object[]>().Subject;
        var product = products[0].Should().BeOfType<Dictionary<string, object>>().Subject;
        product.Should().NotContainKey("templateId");
    }

    [Fact]
    public void BuildOrderBody_SerializesToCompactJson()
    {
        var body = CrptSuzRequestBuilder.BuildOrderBody(
            "chemistry",
            "00000000000000",
            5,
            "Test Contact",
            templateId: 46);

        var json = CrptJson.ToCompact(body);

        json.Should().Contain("\"productGroup\":\"chemistry\"");
        json.Should().Contain("\"gtin\":\"00000000000000\"");
        json.Should().NotContain("\n");
    }
}
