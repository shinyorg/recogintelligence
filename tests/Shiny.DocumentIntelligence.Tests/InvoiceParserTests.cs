using Xunit;
using Shiny.DocumentIntelligence;

namespace Shiny.DocumentIntelligence.Tests;

public class InvoiceParserTests
{
    const string Invoice =
        "ACME CORP\n" +
        "Invoice Number: INV-2024-0042\n" +
        "Invoice Date: 02/03/2024\n" +
        "Due Date: 03/04/2024\n" +
        "Widget A      100.00\n" +
        "Widget B       50.00\n" +
        "Subtotal      150.00\n" +
        "Tax            15.00\n" +
        "Total Due     165.00\n";

    [Fact]
    public void ParsesVendorAndInvoiceNumber()
    {
        var inv = InvoiceParser.Parse(Invoice);
        Assert.Equal("ACME CORP", inv.Vendor);
        Assert.Equal("INV-2024-0042", inv.InvoiceNumber);
    }

    [Fact]
    public void ParsesInvoiceAndDueDates()
    {
        var inv = InvoiceParser.Parse(Invoice);
        Assert.Equal(new DateOnly(2024, 2, 3), inv.InvoiceDate);
        Assert.Equal(new DateOnly(2024, 3, 4), inv.DueDate);
    }

    [Fact]
    public void ParsesTotals()
    {
        var inv = InvoiceParser.Parse(Invoice);
        Assert.Equal(150.00m, inv.Subtotal);
        Assert.Equal(15.00m, inv.Tax);
        Assert.Equal(165.00m, inv.Total);
    }

    [Fact]
    public void ParsesLineItems()
    {
        var inv = InvoiceParser.Parse(Invoice);
        Assert.Contains(inv.Items, i => i.Description == "Widget A" && i.Amount == 100.00m);
        Assert.Contains(inv.Items, i => i.Description == "Widget B" && i.Amount == 50.00m);
    }
}
