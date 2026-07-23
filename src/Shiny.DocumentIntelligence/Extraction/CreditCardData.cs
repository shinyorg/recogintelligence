namespace Shiny.DocumentIntelligence;

/// <summary>The card network, inferred from the issuer identification number (the leading digits of the PAN).</summary>
public enum CardNetwork
{
    /// <summary>The prefix didn't match a known network.</summary>
    Unknown,
    Visa,
    Mastercard,
    AmericanExpress,
    Discover,
    JCB,
    DinersClub,
    UnionPay,
    Maestro
}

/// <summary>
/// Fields read off the front of a payment card.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is cardholder data. Handle it accordingly.</b> Two choices here are deliberate and should not be
/// "improved" away:
/// </para>
/// <list type="number">
/// <item>
/// <b>There is no CVV/CVC property, and the parser never looks for one.</b> PCI-DSS forbids storing the
/// card verification value after authorization, and a scanning API that surfaces it invites exactly that.
/// It also isn't on the front of most cards. If you need it, take it from a text field the user types and
/// pass it straight to your payment processor.
/// </item>
/// <item>
/// <b><see cref="ToString"/> is overridden to mask the number.</b> A positional record's generated
/// <c>ToString</c> prints every property, so <c>logger.LogInformation("{Card}", card)</c> — or a debugger
/// watch, or an exception message — would put a full PAN in your logs. The override makes the safe thing
/// the default; <see cref="Number"/> is still there when you genuinely need it.
/// </item>
/// </list>
/// <para>
/// Scanning a PAN brings the app into PCI-DSS scope. Keep it in memory, hand it to your processor, and
/// don't persist it.
/// </para>
/// </remarks>
/// <param name="Number">The full PAN with separators stripped. Sensitive — see the remarks.</param>
/// <param name="Network">Network inferred from the leading digits.</param>
/// <param name="ExpiryMonth">Expiry month 1–12, when one was found.</param>
/// <param name="ExpiryYear">Four-digit expiry year, when one was found.</param>
/// <param name="CardholderName">Embossed name, when a plausible one was found. Often absent on modern cards.</param>
/// <param name="IsValid">Whether <paramref name="Number"/> passes the Luhn check digit.</param>
public record CreditCardData(
    string Number,
    CardNetwork Network,
    int? ExpiryMonth,
    int? ExpiryYear,
    string? CardholderName,
    bool IsValid
)
{
    /// <summary>The PAN with everything but the last four digits masked — safe for display and logs.</summary>
    public string MaskedNumber =>
        this.Number.Length <= 4
            ? new string('•', this.Number.Length)
            : new string('•', this.Number.Length - 4) + this.Number[^4..];

    /// <summary>The last four digits, the conventional way to reference a card without exposing it.</summary>
    public string Last4 => this.Number.Length <= 4 ? this.Number : this.Number[^4..];

    /// <summary>
    /// The last day the card is valid — cards are good through the <i>end</i> of the printed month, so
    /// "08/27" yields 2027-08-31. Null when no expiry was read.
    /// </summary>
    public DateOnly? ExpiresOn =>
        this.ExpiryMonth is { } m && this.ExpiryYear is { } y
            ? new DateOnly(y, m, DateTime.DaysInMonth(y, m))
            : null;

    /// <summary>True when the card had expired as of <paramref name="asOf"/>. Null expiry is treated as not expired.</summary>
    public bool IsExpired(DateOnly asOf) => this.ExpiresOn is { } d && asOf > d;

    /// <summary>Masked on purpose — see the remarks on this type.</summary>
    public override string ToString()
    {
        var expiry = this.ExpiryMonth is { } m && this.ExpiryYear is { } y ? $" {m:00}/{y % 100:00}" : String.Empty;
        return $"{this.Network} {this.MaskedNumber}{expiry}";
    }
}
