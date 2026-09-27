using System.Globalization;

namespace ChequeBatch;

/// <summary>
/// Turns the file into records. A line it cannot read becomes a rejection
/// rather than an exception, because one mangled line in a file of ten
/// thousand should cost you that line, not the run.
/// </summary>
public static class BatchParser
{
    public const string TrailerPrefix = "T";
    public const string DetailPrefix = "D";

    public static (List<ChequeRecord> Records, List<Rejection> Rejections, BatchTrailer? Trailer)
        Parse(IEnumerable<string> lines)
    {
        var records = new List<ChequeRecord>();
        var rejections = new List<Rejection>();
        BatchTrailer? trailer = null;

        var lineNumber = 0;
        foreach (var raw in lines)
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split('|');

            if (parts[0] == TrailerPrefix)
            {
                if (parts.Length != 3
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                    || !decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var total))
                {
                    rejections.Add(new Rejection(lineNumber, RejectReason.MalformedLine,
                        "trailer should be T|<count>|<total>"));
                    continue;
                }
                trailer = new BatchTrailer(count, total);
                continue;
            }

            if (parts[0] != DetailPrefix || parts.Length != 6)
            {
                rejections.Add(new Rejection(lineNumber, RejectReason.MalformedLine,
                    $"expected D|cheque|account|ifsc|amount|date, found {parts.Length} field(s)"));
                continue;
            }

            if (!decimal.TryParse(parts[4], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                rejections.Add(new Rejection(lineNumber, RejectReason.BadAmount,
                    $"amount '{parts[4]}' is not a number"));
                continue;
            }

            if (!DateOnly.TryParseExact(parts[5], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var issued))
            {
                rejections.Add(new Rejection(lineNumber, RejectReason.MalformedLine,
                    $"date '{parts[5]}' is not yyyy-MM-dd"));
                continue;
            }

            records.Add(new ChequeRecord(lineNumber, parts[1], parts[2], parts[3], amount, issued));
        }

        return (records, rejections, trailer);
    }
}
