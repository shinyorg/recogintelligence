using Xunit;

namespace Shiny.DocumentIntelligence.Tests;

/// <summary>
/// All card numbers here are the published <b>test</b> PANs the networks provide for exactly this purpose
/// (they satisfy Luhn and route nowhere). Never put a real PAN in a test.
/// </summary>
public class CreditCardParserTests
{
    const string Visa = "4111111111111111";
    const string Mastercard = "5555555555554444";
    const string Amex = "378282246310005";
    const string Discover = "6011111111111117";

    [Fact]
    public void ParsesTypicalCardFront()
    {
        var text = """
            BIG BANK
            4111 1111 1111 1111
            VALID THRU  08/27
            JANE Q CARDHOLDER
            DEBIT
            """;

        var card = CreditCardParser.TryParse(text);

        Assert.NotNull(card);
        Assert.Equal(Visa, card!.Number);
        Assert.Equal(CardNetwork.Visa, card.Network);
        Assert.Equal(8, card.ExpiryMonth);
        Assert.Equal(2027, card.ExpiryYear);
        Assert.Equal("JANE Q CARDHOLDER", card.CardholderName);
        Assert.True(card.IsValid);
    }

    [Theory]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("4111111111111111")]
    public void AcceptsAnyGroupingSeparator(string printed)
        => Assert.Equal(Visa, CreditCardParser.TryParse(printed)!.Number);

    [Theory]
    [InlineData(Visa, CardNetwork.Visa)]
    [InlineData(Mastercard, CardNetwork.Mastercard)]
    [InlineData(Amex, CardNetwork.AmericanExpress)]
    [InlineData(Discover, CardNetwork.Discover)]
    [InlineData("2223003122003222", CardNetwork.Mastercard)]   // the 2221-2720 range
    [InlineData("3530111333300000", CardNetwork.JCB)]
    [InlineData("30569309025904", CardNetwork.DinersClub)]
    [InlineData("6200000000000005", CardNetwork.UnionPay)]
    public void DetectsNetworkFromPrefix(string pan, CardNetwork expected)
        => Assert.Equal(expected, CreditCardParser.DetectNetwork(pan));

    [Fact]
    public void ReadsFifteenDigitAmex()
    {
        var card = CreditCardParser.TryParse("3782 822463 10005\nVALID THRU 12/26");

        Assert.Equal(Amex, card!.Number);
        Assert.Equal(CardNetwork.AmericanExpress, card.Network);
        Assert.True(card.IsValid);
    }

    [Fact]
    public void IgnoresNumbersThatAreNotCards()
    {
        // A phone number and a date: right shape, no Luhn, wrong length.
        Assert.Null(CreditCardParser.TryParse("CALL 1-800-555-0199\nMEMBER SINCE 01/09"));
    }

    [Fact]
    public void PrefersLabelledExpiryOverMemberSince()
    {
        var text = $"""
            MEMBER SINCE 03/15
            {Visa}
            VALID THRU 11/28
            """;

        var card = CreditCardParser.TryParse(text);

        Assert.Equal(11, card!.ExpiryMonth);
        Assert.Equal(2028, card.ExpiryYear);
    }

    [Fact]
    public void UnlabelledExpiry_TakesTheLaterDate()
    {
        // No "VALID THRU" label at all — the expiry is always later than the issue date.
        var card = CreditCardParser.TryParse($"{Visa}\n03/15\n09/29");

        Assert.Equal(9, card!.ExpiryMonth);
        Assert.Equal(2029, card.ExpiryYear);
    }

    [Fact]
    public void ExpiryLabelOnPreviousLine_IsStillFound()
    {
        var card = CreditCardParser.TryParse($"{Visa}\nVALID\nTHRU\n05/30");
        Assert.Equal(5, card!.ExpiryMonth);
    }

    [Fact]
    public void RejectsCardFurnitureAsCardholderName()
    {
        var card = CreditCardParser.TryParse($"WORLD ELITE MASTERCARD\n{Mastercard}\nVALID THRU 04/29");
        Assert.Null(card!.CardholderName);
    }

    [Fact]
    public void FailedLuhn_StillReturnsTheReadNumber()
    {
        // One digit off. The caller should be able to show what was read rather than "nothing found".
        var card = CreditCardParser.TryParse("4111 1111 1111 1112");

        Assert.NotNull(card);
        Assert.False(card!.IsValid);
    }

    [Theory]
    [InlineData(Visa, true)]
    [InlineData(Mastercard, true)]
    [InlineData(Amex, true)]
    [InlineData("4111111111111112", false)]
    [InlineData("1234567890123456", false)]
    public void LuhnCheck(string digits, bool valid)
        => Assert.Equal(valid, CreditCardParser.IsLuhnValid(digits));

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        Assert.Null(CreditCardParser.TryParse(""));
        Assert.Null(CreditCardParser.TryParse("   "));
    }

    // --- the security-relevant behaviour ------------------------------------------------------------

    [Fact]
    public void ToString_MasksThePan()
    {
        // A positional record's generated ToString would print the full number, so any log line
        // interpolating the card would leak it. This override is the guard — if it regresses, so does PCI.
        var card = CreditCardParser.TryParse($"{Visa}\nVALID THRU 08/27")!;

        var text = card.ToString();

        Assert.DoesNotContain(Visa, text);
        Assert.Contains("1111", text);          // last 4 is fine to show
        Assert.Contains("Visa", text);
    }

    [Fact]
    public void MaskedNumber_ShowsOnlyLastFour()
    {
        var card = CreditCardParser.TryParse(Visa)!;

        Assert.Equal("••••••••••••1111", card.MaskedNumber);
        Assert.Equal("1111", card.Last4);
    }

    [Fact]
    public void NoCvvIsEverExposed()
    {
        // The type has no CVV member and the parser never looks for one — asserted structurally so nobody
        // "helpfully" adds it later.
        var members = typeof(CreditCardData)
            .GetProperties()
            .Select(p => p.Name.ToLowerInvariant())
            .ToList();

        Assert.DoesNotContain(members, m => m.Contains("cvv") || m.Contains("cvc") || m.Contains("securitycode"));
    }

    // --- expiry helpers -----------------------------------------------------------------------------

    [Fact]
    public void ExpiresOn_IsTheLastDayOfThePrintedMonth()
    {
        var card = CreditCardParser.TryParse($"{Visa}\nVALID THRU 02/28")!;

        // Cards are valid *through* the end of the printed month, and 2028 is a leap year.
        Assert.Equal(new DateOnly(2028, 2, 29), card.ExpiresOn);
    }

    [Fact]
    public void IsExpired_ComparesAgainstEndOfMonth()
    {
        var card = CreditCardParser.TryParse($"{Visa}\nVALID THRU 06/27")!;

        Assert.False(card.IsExpired(new DateOnly(2027, 6, 30)));
        Assert.True(card.IsExpired(new DateOnly(2027, 7, 1)));
    }

    [Fact]
    public void NoExpiry_IsNotTreatedAsExpired()
    {
        var card = CreditCardParser.TryParse(Visa)!;

        Assert.Null(card.ExpiresOn);
        Assert.False(card.IsExpired(new DateOnly(2030, 1, 1)));
    }
}
