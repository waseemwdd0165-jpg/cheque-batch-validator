using System.Globalization;
using System.Text.RegularExpressions;

namespace ChequeBatch;

public sealed class ValidationRules
{
    /// <summary>A cheque goes stale three months after its issue date.</summary>
    public int ValidForDays { get; init; } = 90;

    /// <summary>Anything above this needs a human, so the batch does not carry it.</summary>
    public decimal ReferAbove { get; init; } = 1_000_000m;

    /// <summary>The day the batch is being processed. Injected, never DateTime.Now,
    /// so a test can sit on any date and a re-run of yesterday's file behaves
    /// the way it did yesterday.</summary>
    public required DateOnly ProcessingDate { get; init; }
}

/// <summary>
/// Field level checks on one record. Nothing here looks at the rest of the
/// batch; duplicates and control totals are somebody else's job.
/// </summary>
public static partial class RecordValidator
{
    [GeneratedRegex(@"^\d{6}$")]
    private static partial Regex ChequeNumberPattern();

    [GeneratedRegex(@"^\d{9,18}$")]
    private static partial Regex AccountPattern();

    // Four letters for the bank, a reserved zero, then the branch code.
    [GeneratedRegex(@"^[A-Z]{4}0[A-Z0-9]{6}$")]
    private static partial Regex IfscPattern();

    public static IEnumerable<Rejection> Validate(ChequeRecord r, ValidationRules rules)
    {
        if (!ChequeNumberPattern().IsMatch(r.ChequeNumber))
            yield return new Rejection(r.LineNumber, RejectReason.BadChequeNumber,
                $"cheque number '{r.ChequeNumber}' is not six digits");

        if (!AccountPattern().IsMatch(r.AccountNumber))
            yield return new Rejection(r.LineNumber, RejectReason.BadAccountNumber,
                $"account '{r.AccountNumber}' is not 9 to 18 digits");

        if (!IfscPattern().IsMatch(r.Ifsc))
            yield return new Rejection(r.LineNumber, RejectReason.BadIfsc,
                $"IFSC '{r.Ifsc}' does not match AAAA0BBBBBB");

        if (r.Amount <= 0)
            yield return new Rejection(r.LineNumber, RejectReason.BadAmount,
                $"amount {Money(r.Amount)} is not positive");
        else if (decimal.Round(r.Amount, 2) != r.Amount)
            yield return new Rejection(r.LineNumber, RejectReason.BadAmount,
                $"amount {r.Amount} has more than two decimal places");
        else if (r.Amount > rules.ReferAbove)
            yield return new Rejection(r.LineNumber, RejectReason.AmountTooLarge,
                $"amount {Money(r.Amount)} is above the {Money(rules.ReferAbove)} limit");

        if (r.IssueDate > rules.ProcessingDate)
            yield return new Rejection(r.LineNumber, RejectReason.PostDated,
                $"issued {r.IssueDate:yyyy-MM-dd}, which is after {rules.ProcessingDate:yyyy-MM-dd}");
        else if (r.IssueDate.AddDays(rules.ValidForDays) < rules.ProcessingDate)
            yield return new Rejection(r.LineNumber, RejectReason.StaleCheque,
                $"issued {r.IssueDate:yyyy-MM-dd}, more than {rules.ValidForDays} days before " +
                $"{rules.ProcessingDate:yyyy-MM-dd}");
    }

    private static string Money(decimal amount) =>
        amount.ToString("N2", CultureInfo.InvariantCulture);
}
