using Xunit;
using Shiny.DocumentIntelligence;

namespace Shiny.DocumentIntelligence.Tests;

public class MrzParserTests
{
    // The canonical ICAO 9303 TD3 specimen (Anna Maria Eriksson). All check digits are valid.
    const string Td3Line1 = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
    const string Td3Line2 = "L898902C36UTO7408122F1204159ZE184226B<<<<<10";

    [Fact]
    public void Td3_ParsesAllFields()
    {
        var result = MrzParser.TryParse(Td3Line1 + "\n" + Td3Line2);

        Assert.NotNull(result);
        Assert.Equal("P", result!.DocumentCode);
        Assert.Equal("UTO", result.IssuingCountry);
        Assert.Equal("ERIKSSON", result.Surname);
        Assert.Equal("ANNA MARIA", result.GivenNames);
        Assert.Equal("L898902C3", result.PassportNumber);
        Assert.Equal("UTO", result.Nationality);
        Assert.Equal(new DateOnly(1974, 8, 12), result.DateOfBirth);
        Assert.Equal("F", result.Sex);
        Assert.Equal(new DateOnly(2012, 4, 15), result.ExpiryDate);
    }

    [Fact]
    public void Td3_ValidCheckDigits_AreReportedValid()
    {
        var result = MrzParser.TryParse(Td3Line1 + "\n" + Td3Line2);
        Assert.True(result!.IsValid);
    }

    [Fact]
    public void Td3_CorruptedCheckDigit_IsReportedInvalid()
    {
        // Flip the DOB check digit (…7408122F… -> …7408123F…) — fields still parse, validity fails.
        var corrupted = Td3Line2.Replace("7408122F", "7408123F");
        var result = MrzParser.TryParse(Td3Line1 + "\n" + corrupted);

        Assert.NotNull(result);
        Assert.False(result!.IsValid);
    }

    [Fact]
    public void TryParse_FindsMrzAmongVisualZoneText()
    {
        // The OCR also picks up the human-readable zone above the MRZ; the parser must still locate it.
        var noisy = "PASSPORT\nType P  Code UTO\nSurname ERIKSSON\n" + Td3Line1 + "\n" + Td3Line2 + "\n";
        var result = MrzParser.TryParse(noisy);

        Assert.NotNull(result);
        Assert.Equal("L898902C3", result!.PassportNumber);
    }

    [Fact]
    public void TryParse_NoMrz_ReturnsNull() =>
        Assert.Null(MrzParser.TryParse("just some\nordinary text\nno machine zone here"));

    [Fact]
    public void TryParse_Empty_ReturnsNull() =>
        Assert.Null(MrzParser.TryParse(""));
}
