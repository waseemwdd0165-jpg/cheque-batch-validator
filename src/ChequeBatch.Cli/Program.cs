using System.Globalization;
using ChequeBatch;

// chequebatch <file> [--date yyyy-MM-dd] [--out <dir>]
//
// Exit codes are what a scheduler reads, so they mean something:
//   0  the batch balances and settles
//   1  the batch is refused: the file does not agree with its own trailer
//   2  the file could not be read

var path = args.FirstOrDefault(a => !a.StartsWith("--"));
if (path is null)
{
    Console.Error.WriteLine("usage: chequebatch <file> [--date yyyy-MM-dd] [--out <dir>]");
    return 2;
}

var processingDate = DateOnly.FromDateTime(DateTime.Today);
var dateArg = ValueOf("--date");
if (dateArg is not null &&
    !DateOnly.TryParseExact(dateArg, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out processingDate))
{
    Console.Error.WriteLine($"--date '{dateArg}' is not yyyy-MM-dd");
    return 2;
}

string[] lines;
try
{
    lines = File.ReadAllLines(path);
}
catch (IOException e)
{
    Console.Error.WriteLine($"could not read {path}: {e.Message}");
    return 2;
}

var rules = new ValidationRules { ProcessingDate = processingDate };
var result = new BatchProcessor(rules).Process(lines);

Console.WriteLine($"{Path.GetFileName(path)}  processed as at {processingDate:yyyy-MM-dd}");
Console.WriteLine($"  accepted  {result.Accepted.Count,6}   {Money(result.AcceptedTotal),16}");
Console.WriteLine($"  rejected  {result.Rejected.Count,6}");

if (result.Rejected.Count > 0)
{
    Console.WriteLine();
    foreach (var group in result.Rejected.GroupBy(r => r.Reason).OrderByDescending(g => g.Count()))
    {
        Console.WriteLine($"  {group.Key,-28} {group.Count(),4}");
        foreach (var r in group.Take(3))
            Console.WriteLine($"      line {r.LineNumber,-5} {r.Detail}");
        if (group.Count() > 3)
            Console.WriteLine($"      ... and {group.Count() - 3} more");
    }
}

Console.WriteLine();
if (result.Settles)
{
    Console.WriteLine("  controls  ok, the file agrees with its trailer");
}
else
{
    Console.WriteLine("  controls  FAILED, nothing in this batch settles");
    foreach (var f in result.ControlFailures) Console.WriteLine($"      {f}");
}

var outDir = ValueOf("--out");
if (outDir is not null)
{
    Directory.CreateDirectory(outDir);
    File.WriteAllLines(Path.Combine(outDir, "accepted.txt"),
        result.Accepted.Select(c =>
            $"D|{c.ChequeNumber}|{c.AccountNumber}|{c.Ifsc}|{c.Amount}|{c.IssueDate:yyyy-MM-dd}"));
    File.WriteAllLines(Path.Combine(outDir, "rejected.txt"),
        result.Rejected.Select(r => $"{r.LineNumber}|{r.Reason}|{r.Detail}"));
    Console.WriteLine($"\n  written to {outDir}");
}

return result.Settles ? 0 : 1;

string? ValueOf(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
