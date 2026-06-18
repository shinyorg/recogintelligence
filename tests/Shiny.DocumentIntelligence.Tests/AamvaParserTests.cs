using Xunit;
using Shiny.DocumentIntelligence;

namespace Shiny.DocumentIntelligence.Tests;

public class AamvaParserTests
{
    // A representative AAMVA PDF417 payload (US license). Element IDs per the AAMVA DL/ID standard.
    const string UsLicense =
        "@\n\rANSI 636026080002DL00410288\n" +
        "DLDAQD12345678\n" +
        "DCSPUBLIC\n" +
        "DACJOHN\n" +
        "DADQUINCY\n" +
        "DBB01151985\n" +
        "DBA01152027\n" +
        "DBD01152021\n" +
        "DBC1\n" +
        "DAG123 MAIN ST\n" +
        "DAILOS ANGELES\n" +
        "DAJCA\n" +
        "DAK902100000\n" +
        "DCGUSA\r";

    [Fact]
    public void ParsesNameAndLicenseNumber()
    {
        var result = AamvaParser.TryParse(UsLicense);

        Assert.NotNull(result);
        Assert.Equal("JOHN", result!.FirstName);
        Assert.Equal("QUINCY", result.MiddleName);
        Assert.Equal("PUBLIC", result.LastName);
        Assert.Equal("D12345678", result.LicenseNumber);
    }

    [Fact]
    public void ParsesUsDates_MmDdCcYy()
    {
        var result = AamvaParser.TryParse(UsLicense);

        Assert.Equal(new DateOnly(1985, 1, 15), result!.DateOfBirth);
        Assert.Equal(new DateOnly(2027, 1, 15), result.ExpiryDate);
        Assert.Equal(new DateOnly(2021, 1, 15), result.IssueDate);
    }

    [Fact]
    public void ParsesAddressAndSex()
    {
        var result = AamvaParser.TryParse(UsLicense);

        Assert.Equal("123 MAIN ST", result!.Address);
        Assert.Equal("LOS ANGELES", result.City);
        Assert.Equal("CA", result.State);
        Assert.Equal("90210", result.PostalCode); // +4 was zero-filler, so dropped — and not mangled to "9021"
        Assert.Equal("M", result.Sex);
    }

    [Fact]
    public void ParsesCanadianDates_CcYyMmDd()
    {
        // Canada encodes dates CCYYMMDD; the DCG=CAN hint (and year-first layout) must flip parsing.
        var caLicense = UsLicense.Replace("DBB01151985", "DBB19850115").Replace("DCGUSA", "DCGCAN");
        var result = AamvaParser.TryParse(caLicense);

        Assert.Equal(new DateOnly(1985, 1, 15), result!.DateOfBirth);
    }

    [Fact]
    public void KeepsFullElementDictionary()
    {
        var result = AamvaParser.TryParse(UsLicense);
        Assert.Equal("D12345678", result!.Elements["DAQ"]);
        Assert.Equal("CA", result.Elements["DAJ"]);
    }

    [Fact]
    public void NonAamvaPayload_ReturnsNull() =>
        Assert.Null(AamvaParser.TryParse("https://example.com/not-a-license"));
}
