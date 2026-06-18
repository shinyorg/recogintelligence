using Xunit;
using Shiny.DocumentIntelligence;

namespace Shiny.DocumentIntelligence.Tests;

public class ReceiptParserTests
{
    const string Receipt =
        "WHOLE FOODS MARKET\n" +
        "123 Main Street\n" +
        "01/15/2024\n" +
        "Bananas        1.99\n" +
        "Almond Milk    3.49\n" +
        "Subtotal       5.48\n" +
        "Tax            0.55\n" +
        "Total          6.03\n";

    [Fact]
    public void ParsesMerchantTotalsAndDate()
    {
        var r = ReceiptParser.Parse(Receipt);

        Assert.Equal("WHOLE FOODS MARKET", r.Merchant);
        Assert.Equal(5.48m, r.Subtotal);
        Assert.Equal(0.55m, r.Tax);
        Assert.Equal(6.03m, r.Total);
        Assert.Equal(new DateOnly(2024, 1, 15), r.Date);
    }

    [Fact]
    public void Total_DoesNotPickUpSubtotalLine()
    {
        // "Subtotal" contains "total"; the parser must exclude it so Total is 6.03, not 5.48.
        var r = ReceiptParser.Parse(Receipt);
        Assert.Equal(6.03m, r.Total);
    }

    [Fact]
    public void ParsesLineItems()
    {
        var r = ReceiptParser.Parse(Receipt);
        Assert.Contains(r.Items, i => i.Description == "Bananas" && i.Amount == 1.99m);
        Assert.Contains(r.Items, i => i.Description == "Almond Milk" && i.Amount == 3.49m);
    }

    [Fact]
    public void DetectsCurrencySymbol()
    {
        var r = ReceiptParser.Parse("CAFE\nLatte  $4.50\nTotal  $4.50\n");
        Assert.Equal("USD", r.Currency);
    }

    [Fact]
    public void NoTotalKeyword_FallsBackToLargestAmount()
    {
        var r = ReceiptParser.Parse("SHOP\nThing A  2.00\nThing B  9.99\n");
        Assert.Equal(9.99m, r.Total);
    }

    // A messier, more realistic receipt: uppercase labels, $ symbols, an "HST 13%" tax line,
    // a "Total items 2" decoy above the real total, and a VISA tender line after it.
    const string MessyReceipt =
        "TARGET\n" +
        "Jan 15, 2024\n" +
        "Milk $3.99\n" +
        "Bread $2.50\n" +
        "Total items 2\n" +
        "SUBTOTAL $6.49\n" +
        "HST 13% $0.84\n" +
        "TOTAL $7.33\n" +
        "VISA $7.33\n";

    [Fact]
    public void Messy_TotalSkipsDecoyAndTender()
    {
        var r = ReceiptParser.Parse(MessyReceipt);
        Assert.Equal(7.33m, r.Total);   // not "Total items 2", not the VISA tender, not subtotal
        Assert.Equal(6.49m, r.Subtotal);
        Assert.Equal(0.84m, r.Tax);
        Assert.Equal(new DateOnly(2024, 1, 15), r.Date); // "Jan 15, 2024" month-name format
    }

    [Fact]
    public void Messy_PaymentLineIsNotALineItem()
    {
        var r = ReceiptParser.Parse(MessyReceipt);
        Assert.DoesNotContain(r.Items, i => i.Description.Contains("VISA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Items, i => i.Description == "Milk");
        Assert.Contains(r.Items, i => i.Description == "Bread");
    }

    [Fact]
    public void ParsesThousandsSeparatorAndTrailingIsoCurrency()
    {
        var r = ReceiptParser.Parse("ELECTRONICS CO\n2024-03-01\nLaptop 1,299.00\nTotal 1,402.92 USD\n");
        Assert.Equal(1402.92m, r.Total);
        Assert.Equal("USD", r.Currency); // no symbol present — detected from the trailing ISO code
    }

    [Fact]
    public void Total_TakesBottomMostWhenAQualifyingDecoyHasAnAmount()
    {
        // "Total savings $1.00" appears before the real total and carries an amount — must be skipped.
        var r = ReceiptParser.Parse("STORE\nWidget 10.00\nTotal savings $1.00\nTotal $9.00\n");
        Assert.Equal(9.00m, r.Total);
    }
}
