using System.Globalization;

namespace ChequeBatch;

/// <summary>
/// The whole run: parse, validate each record, find duplicates, then check the
/// file against its own trailer.
///
/// The ordering matters. Control totals are compared against every record the
/// file claimed to contain, not against the ones that survived validation.
/// Otherwise a file could drop a record on the floor and still balance, which
/// is exactly the failure a control total exists to catch.
/// </summary>
public sealed class BatchProcessor(ValidationRules rules)
{
    public BatchResult Process(IEnumerable<string> lines)
    {
        var (records, rejections, trailer) = BatchParser.Parse(lines);

        // Everything the parser rejected is a line that never became a record, so
        // its amount is unknown. Count them now, before validation adds its own
        // rejections for lines that did parse.
        var unreadable = rejections.Count;

        var accepted = new List<ChequeRecord>();
        var seen = new Dictionary<string, ChequeRecord>();

        foreach (var record in records)
        {
            var problems = RecordValidator.Validate(record, rules).ToList();

            if (seen.TryGetValue(record.Identity, out var first))
            {
                problems.Add(first.Amount == record.Amount
                    ? new Rejection(record.LineNumber, RejectReason.DuplicateInBatch,
                        $"same cheque as line {first.LineNumber}")
                    : new Rejection(record.LineNumber, RejectReason.DuplicateWithDifferentAmount,
                        $"same cheque as line {first.LineNumber} but for " +
                        $"{Money(record.Amount)} instead of {Money(first.Amount)}"));
            }
            else
            {
                seen[record.Identity] = record;
            }

            if (problems.Count > 0) rejections.AddRange(problems);
            else accepted.Add(record);
        }

        return new BatchResult(accepted, rejections, trailer,
                               CheckControls(records, unreadable, trailer));
    }

    private static List<string> CheckControls(
        List<ChequeRecord> parsed, int unreadable, BatchTrailer? trailer)
    {
        var failures = new List<string>();

        if (trailer is null)
        {
            failures.Add("the file has no trailer, so there is nothing to balance against");
            return failures;
        }

        if (parsed.Count + unreadable != trailer.Count)
            failures.Add($"trailer says {trailer.Count} record(s), the file holds " +
                         $"{parsed.Count + unreadable}");

        if (unreadable > 0)
        {
            // An unreadable line has no amount, so no sum can prove the file is
            // whole. A trailer total that happens to match the readable records
            // is a coincidence, not a reconciliation.
            failures.Add($"{unreadable} line(s) could not be read, so the file's total " +
                         "cannot be reconciled");
        }
        else if (parsed.Sum(r => r.Amount) != trailer.Total)
        {
            failures.Add($"trailer says {Money(trailer.Total)}, the records add up to " +
                         $"{Money(parsed.Sum(r => r.Amount))}");
        }

        return failures;
    }

    private static string Money(decimal amount) =>
        amount.ToString("N2", CultureInfo.InvariantCulture);
}
