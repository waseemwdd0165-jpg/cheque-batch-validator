using ChequeBatch;
using Xunit;

namespace ChequeBatch.Tests;

public class BatchParserTests
{
    [Fact]
    public void A_detail_line_becomes_a_record()
    {
        var (records, rejections, trailer) = BatchParser.Parse(new[]
        {
            "D|100045|123456789012|HDFC0001234|12500.00|2026-06-20"
        });

        Assert.Empty(rejections);
        Assert.Null(trailer);
        var r = Assert.Single(records);
        Assert.Equal("100045", r.ChequeNumber);
        Assert.Equal(12_500.00m, r.Amount);
        Assert.Equal(new DateOnly(2026, 6, 20), r.IssueDate);
        Assert.Equal(1, r.LineNumber);
    }

    [Fact]
    public void Blank_lines_are_skipped_but_still_count_for_numbering()
    {
        var (records, _, _) = BatchParser.Parse(new[]
        {
            "",
            "   ",
            "D|100045|123456789012|HDFC0001234|1.00|2026-06-20"
        });

        Assert.Equal(3, Assert.Single(records).LineNumber);
    }

    [Fact]
    public void A_line_with_too_few_fields_is_a_rejection_not_an_exception()
    {
        var (records, rejections, _) = BatchParser.Parse(new[] { "D|100045|123456789012" });

        Assert.Empty(records);
        Assert.Equal(RejectReason.MalformedLine, Assert.Single(rejections).Reason);
    }

    [Fact]
    public void An_unknown_record_type_is_a_rejection()
    {
        var (_, rejections, _) = BatchParser.Parse(new[] { "X|who|knows|what|this|is" });
        Assert.Equal(RejectReason.MalformedLine, Assert.Single(rejections).Reason);
    }

    [Fact]
    public void An_amount_that_is_not_a_number_is_reported_as_a_bad_amount()
    {
        var (_, rejections, _) = BatchParser.Parse(new[]
        {
            "D|100045|123456789012|HDFC0001234|twelve thousand|2026-06-20"
        });

        Assert.Equal(RejectReason.BadAmount, Assert.Single(rejections).Reason);
    }

    [Fact]
    public void A_date_in_the_wrong_shape_does_not_get_guessed_at()
    {
        var (_, rejections, _) = BatchParser.Parse(new[]
        {
            "D|100045|123456789012|HDFC0001234|1.00|20-06-2026"
        });

        Assert.Equal(RejectReason.MalformedLine, Assert.Single(rejections).Reason);
    }

    [Fact]
    public void The_trailer_is_read_out_of_the_file()
    {
        var (_, rejections, trailer) = BatchParser.Parse(new[] { "T|2|3500.50" });

        Assert.Empty(rejections);
        Assert.NotNull(trailer);
        Assert.Equal(2, trailer!.Count);
        Assert.Equal(3500.50m, trailer.Total);
    }

    [Fact]
    public void A_trailer_in_the_wrong_shape_is_rejected_and_leaves_no_trailer()
    {
        var (_, rejections, trailer) = BatchParser.Parse(new[] { "T|two|3500.50" });

        Assert.Null(trailer);
        Assert.Equal(RejectReason.MalformedLine, Assert.Single(rejections).Reason);
    }
}

public class BatchProcessorTests
{
    private static readonly ValidationRules Rules = new()
    {
        ProcessingDate = new DateOnly(2026, 6, 30)
    };

    private static BatchResult Run(params string[] lines) =>
        new BatchProcessor(Rules).Process(lines);

    private static string Detail(string cheque, decimal amount,
                                 string account = "123456789012",
                                 string ifsc = "HDFC0001234",
                                 string date = "2026-06-20") =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"D|{cheque}|{account}|{ifsc}|{amount:0.00}|{date}");

    [Fact]
    public void A_batch_that_agrees_with_its_trailer_settles()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            Detail("100046", 2500.50m),
            "T|2|3500.50");

        Assert.True(result.Settles);
        Assert.Empty(result.ControlFailures);
        Assert.Equal(2, result.Accepted.Count);
        Assert.Equal(3500.50m, result.AcceptedTotal);
    }

    [Fact]
    public void A_trailer_count_that_does_not_match_refuses_the_whole_batch()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            "T|2|1000.00");

        Assert.False(result.Settles);
        Assert.Contains(result.ControlFailures, f => f.Contains("record"));
    }

    [Fact]
    public void A_trailer_total_that_does_not_match_refuses_the_whole_batch()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            "T|1|999.00");

        Assert.False(result.Settles);
        Assert.Contains(result.ControlFailures, f => f.Contains("999.00"));
    }

    [Fact]
    public void A_file_with_no_trailer_cannot_settle()
    {
        var result = Run(Detail("100045", 1000.00m));

        Assert.False(result.Settles);
        Assert.Contains(result.ControlFailures, f => f.Contains("no trailer"));
    }

    /// <summary>
    /// The point of the whole exercise. A record that failed validation is still a
    /// record the file presented, so it counts towards the trailer. If controls
    /// were checked against the accepted list instead, this batch would balance
    /// and one cheque would have quietly vanished.
    /// </summary>
    [Fact]
    public void A_rejected_record_still_counts_towards_the_control_totals()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            Detail("bad!!!", 2500.50m),   // fails the cheque number rule
            "T|2|3500.50");

        Assert.Single(result.Accepted);
        Assert.Single(result.Rejected);
        Assert.True(result.Settles);
        Assert.Equal(1000.00m, result.AcceptedTotal);
    }

    [Fact]
    public void A_malformed_line_counts_towards_the_trailer_count()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            "D|100046|123456789012",     // unreadable, but the file claimed it
            "T|2|1000.00");

        Assert.DoesNotContain(result.ControlFailures, f => f.Contains("record(s), the file holds"));
    }

    /// <summary>
    /// A line nobody can read has no amount, so no sum proves the file is whole.
    /// The trailer total here matches the one readable record exactly, and the
    /// batch is still refused, because that match is a coincidence.
    /// </summary>
    [Fact]
    public void A_file_with_an_unreadable_line_cannot_be_reconciled_even_if_the_total_matches()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            "D|100046|123456789012",
            "T|2|1000.00");

        Assert.False(result.Settles);
        Assert.Contains(result.ControlFailures, f => f.Contains("could not be read"));
    }

    [Fact]
    public void An_unreadable_amount_is_an_unreadable_line_for_control_purposes()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            "D|100046|123456789012|HDFC0001234|twelve thousand|2026-06-20",
            "T|2|13000.00");

        Assert.False(result.Settles);
        Assert.Contains(result.ControlFailures, f => f.Contains("could not be read"));
        // The count is right: the file did hold two records, one of them unreadable.
        Assert.DoesNotContain(result.ControlFailures, f => f.Contains("record(s), the file holds"));
    }

    [Fact]
    public void Dropping_a_record_does_not_balance()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            "T|2|3500.50");

        Assert.False(result.Settles);
        Assert.Equal(2, result.ControlFailures.Count);   // count wrong and total wrong
    }

    [Fact]
    public void The_same_cheque_twice_for_the_same_amount_is_a_plain_duplicate()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            Detail("100045", 1000.00m),
            "T|2|2000.00");

        Assert.Single(result.Accepted);
        Assert.Equal(RejectReason.DuplicateInBatch, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void The_same_cheque_twice_for_different_amounts_gets_its_own_reason()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            Detail("100045", 9000.00m),
            "T|2|10000.00");

        var rejection = Assert.Single(result.Rejected);
        Assert.Equal(RejectReason.DuplicateWithDifferentAmount, rejection.Reason);
        Assert.Contains("1,000.00", rejection.Detail);
        Assert.Contains("9,000.00", rejection.Detail);
    }

    [Fact]
    public void The_first_of_a_duplicate_pair_is_the_one_that_is_kept()
    {
        var result = Run(
            Detail("100045", 1000.00m),
            Detail("100045", 9000.00m),
            "T|2|10000.00");

        Assert.Equal(1000.00m, Assert.Single(result.Accepted).Amount);
        Assert.Equal(2, Assert.Single(result.Rejected).LineNumber);
    }

    [Fact]
    public void The_same_cheque_number_at_a_different_bank_is_not_a_duplicate()
    {
        var result = Run(
            Detail("100045", 1000.00m, ifsc: "HDFC0001234"),
            Detail("100045", 1000.00m, ifsc: "SBIN0001234"),
            "T|2|2000.00");

        Assert.Equal(2, result.Accepted.Count);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public void The_same_cheque_number_on_a_different_account_is_not_a_duplicate()
    {
        var result = Run(
            Detail("100045", 1000.00m, account: "123456789012"),
            Detail("100045", 1000.00m, account: "999456789012"),
            "T|2|2000.00");

        Assert.Equal(2, result.Accepted.Count);
    }

    [Fact]
    public void A_duplicate_that_is_also_invalid_reports_both_problems()
    {
        var result = Run(
            Detail("bad!!!", 1000.00m),
            Detail("bad!!!", 1000.00m),
            "T|2|2000.00");

        Assert.Empty(result.Accepted);
        Assert.Equal(3, result.Rejected.Count);   // two bad numbers, one duplicate
        Assert.Contains(result.Rejected, r => r.Reason == RejectReason.DuplicateInBatch);
    }

    [Fact]
    public void An_empty_file_with_a_zero_trailer_settles()
    {
        var result = Run("T|0|0.00");

        Assert.True(result.Settles);
        Assert.Empty(result.Accepted);
    }

    [Fact]
    public void An_entirely_empty_file_does_not_settle()
    {
        var result = Run();

        Assert.False(result.Settles);
    }

    [Fact]
    public void Processing_the_same_file_on_a_later_date_can_change_the_answer()
    {
        var lines = new[] { Detail("100045", 1000.00m, date: "2026-01-01"), "T|1|1000.00" };

        var onTime = new BatchProcessor(new ValidationRules
        {
            ProcessingDate = new DateOnly(2026, 2, 1)
        }).Process(lines);

        var tooLate = new BatchProcessor(new ValidationRules
        {
            ProcessingDate = new DateOnly(2026, 6, 1)
        }).Process(lines);

        Assert.Single(onTime.Accepted);
        Assert.Empty(tooLate.Accepted);
        Assert.Equal(RejectReason.StaleCheque, Assert.Single(tooLate.Rejected).Reason);

        // Either way the file still balances against its own trailer: a stale
        // cheque is a rejected record, not a missing one.
        Assert.True(onTime.Settles);
        Assert.True(tooLate.Settles);
    }
}
