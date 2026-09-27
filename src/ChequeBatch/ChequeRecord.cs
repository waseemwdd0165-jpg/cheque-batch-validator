namespace ChequeBatch;

/// <summary>
/// One line of a presentment file: a single cheque being sent for clearing.
/// </summary>
/// <param name="LineNumber">Where it sat in the file, so a rejection can be traced back.</param>
public sealed record ChequeRecord(
    int LineNumber,
    string ChequeNumber,
    string AccountNumber,
    string Ifsc,
    decimal Amount,
    DateOnly IssueDate)
{
    /// <summary>
    /// What makes two presentments the same cheque. The amount is deliberately
    /// not part of this: the same cheque presented twice for different amounts
    /// is a worse problem than a duplicate, not a different cheque.
    /// </summary>
    public string Identity => $"{Ifsc}|{AccountNumber}|{ChequeNumber}";
}

/// <summary>
/// The last line of the file. The sender states how many records they put in
/// and what they add up to, and the batch is refused unless the file agrees
/// with its own trailer.
/// </summary>
public sealed record BatchTrailer(int Count, decimal Total);

public enum RejectReason
{
    MalformedLine,
    BadChequeNumber,
    BadAccountNumber,
    BadIfsc,
    BadAmount,
    AmountTooLarge,
    StaleCheque,
    PostDated,
    DuplicateInBatch,
    DuplicateWithDifferentAmount
}

public sealed record Rejection(int LineNumber, RejectReason Reason, string Detail);

public sealed record BatchResult(
    IReadOnlyList<ChequeRecord> Accepted,
    IReadOnlyList<Rejection> Rejected,
    BatchTrailer? Trailer,
    IReadOnlyList<string> ControlFailures)
{
    /// <summary>
    /// A batch with a control failure is not partly good. Nothing in it settles.
    /// </summary>
    public bool Settles => ControlFailures.Count == 0;

    public decimal AcceptedTotal => Accepted.Sum(c => c.Amount);
}
