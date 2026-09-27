using ChequeBatch;
using Xunit;

namespace ChequeBatch.Tests;

public class RecordValidatorTests
{
    private static readonly ValidationRules Rules = new()
    {
        ProcessingDate = new DateOnly(2026, 6, 30)
    };

    private static ChequeRecord Good(
        string cheque = "100045",
        string account = "123456789012",
        string ifsc = "HDFC0001234",
        decimal amount = 12_500.00m,
        int daysAgo = 10) =>
        new(1, cheque, account, ifsc, amount, Rules.ProcessingDate.AddDays(-daysAgo));

    private static RejectReason[] Reasons(ChequeRecord r) =>
        RecordValidator.Validate(r, Rules).Select(x => x.Reason).ToArray();

    [Fact]
    public void A_clean_record_has_nothing_wrong_with_it()
        => Assert.Empty(Reasons(Good()));

    [Theory]
    [InlineData("12345")]        // too short
    [InlineData("1234567")]      // too long
    [InlineData("12A456")]       // not digits
    [InlineData("")]
    public void Cheque_number_must_be_six_digits(string cheque)
        => Assert.Contains(RejectReason.BadChequeNumber, Reasons(Good(cheque: cheque)));

    [Theory]
    [InlineData("12345678")]                 // 8, one short
    [InlineData("1234567890123456789")]      // 19, one long
    [InlineData("12345678901a")]
    public void Account_must_be_nine_to_eighteen_digits(string account)
        => Assert.Contains(RejectReason.BadAccountNumber, Reasons(Good(account: account)));

    [Theory]
    [InlineData("123456789")]                // exactly 9
    [InlineData("123456789012345678")]       // exactly 18
    public void The_ends_of_the_account_range_are_allowed(string account)
        => Assert.DoesNotContain(RejectReason.BadAccountNumber, Reasons(Good(account: account)));

    [Theory]
    [InlineData("HDFC1001234")]   // fifth character must be zero
    [InlineData("HDF00001234")]   // three letters
    [InlineData("hdfc0001234")]   // lower case
    [InlineData("HDFC000123")]    // branch code too short
    public void Ifsc_must_be_four_letters_a_zero_and_six_more(string ifsc)
        => Assert.Contains(RejectReason.BadIfsc, Reasons(Good(ifsc: ifsc)));

    [Fact]
    public void A_numeric_branch_code_is_fine()
        => Assert.DoesNotContain(RejectReason.BadIfsc, Reasons(Good(ifsc: "SBIN0012345")));

    [Fact]
    public void A_zero_amount_is_refused()
        => Assert.Contains(RejectReason.BadAmount, Reasons(Good(amount: 0m)));

    [Fact]
    public void A_negative_amount_is_refused()
        => Assert.Contains(RejectReason.BadAmount, Reasons(Good(amount: -1m)));

    [Fact]
    public void Amount_cannot_carry_sub_paisa_precision()
        => Assert.Contains(RejectReason.BadAmount, Reasons(Good(amount: 100.005m)));

    [Fact]
    public void Exactly_the_referral_limit_still_goes_through()
        => Assert.DoesNotContain(RejectReason.AmountTooLarge,
                                 Reasons(Good(amount: Rules.ReferAbove)));

    [Fact]
    public void One_paisa_over_the_limit_is_referred()
        => Assert.Contains(RejectReason.AmountTooLarge,
                           Reasons(Good(amount: Rules.ReferAbove + 0.01m)));

    [Fact]
    public void A_cheque_dated_tomorrow_is_post_dated()
        => Assert.Contains(RejectReason.PostDated, Reasons(Good(daysAgo: -1)));

    [Fact]
    public void A_cheque_dated_today_is_fine()
        => Assert.Empty(Reasons(Good(daysAgo: 0)));

    [Fact]
    public void The_last_valid_day_is_still_valid()
        => Assert.DoesNotContain(RejectReason.StaleCheque,
                                 Reasons(Good(daysAgo: Rules.ValidForDays)));

    [Fact]
    public void The_day_after_that_is_stale()
        => Assert.Contains(RejectReason.StaleCheque,
                           Reasons(Good(daysAgo: Rules.ValidForDays + 1)));

    [Fact]
    public void Every_problem_with_a_record_is_reported_not_just_the_first()
    {
        var reasons = Reasons(Good(cheque: "12", account: "x", amount: -5));
        Assert.Contains(RejectReason.BadChequeNumber, reasons);
        Assert.Contains(RejectReason.BadAccountNumber, reasons);
        Assert.Contains(RejectReason.BadAmount, reasons);
    }
}
