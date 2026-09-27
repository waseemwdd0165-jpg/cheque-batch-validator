# cheque-batch-validator

A .NET 8 library and command line tool that validates a cheque presentment batch
and refuses the whole file if it does not agree with its own control totals.

I spent nine years on the nationwide Cheque Truncation System rollout at Mumbai
Clearing House, and the same small number of things went wrong in inward files
over and over: a field in the wrong shape, the same cheque presented twice for
two different amounts, and, the one that actually costs money, a file whose
trailer no longer matches what the file contains. This is that logic, written
out properly, with the reasoning in the code rather than in my head.

## The file format

Pipe separated, one record per line, one trailer at the end.

```
D|<cheque no>|<account no>|<ifsc>|<amount>|<yyyy-MM-dd>
T|<record count>|<total amount>
```

## What it checks

Field rules, one record at a time:

| Rule | Reason code |
| --- | --- |
| Cheque number is exactly six digits | `BadChequeNumber` |
| Account number is 9 to 18 digits | `BadAccountNumber` |
| IFSC is four letters, a reserved zero, then six characters | `BadIfsc` |
| Amount is positive and no finer than a paisa | `BadAmount` |
| Amount is at or below the referral limit | `AmountTooLarge` |
| Issue date is not in the future | `PostDated` |
| Cheque is within its validity period | `StaleCheque` |

Across the batch:

| Rule | Reason code |
| --- | --- |
| The same cheque appears twice for the same amount | `DuplicateInBatch` |
| The same cheque appears twice for different amounts | `DuplicateWithDifferentAmount` |

A cheque is identified by IFSC plus account plus cheque number, not by cheque
number alone. Two banks issue cheque number 100045 every day.

## The part that matters

Control totals are compared against every record the file claimed to contain,
not against the records that survived validation.

That sounds like a detail and it is the whole point. If you total the accepted
records and compare that to the trailer, then a file that quietly loses a record
in transit still balances, because the lost record is missing from both sides of
the comparison. A control total exists to catch exactly that, so it has to be
checked before validation throws anything away.

Two consequences fall out of it:

- A rejected record still counts. A stale cheque is a record the file presented
  and the file is still whole, so the batch can settle with rejections in it.
- A line nobody can parse makes the file unreconcilable. Its amount is unknown,
  so no sum can prove the file is complete. If the trailer total happens to
  match the readable records, that is a coincidence, and the batch is still
  refused.

When controls fail, nothing in the batch settles. Not the good records, not
some of them. The file goes back to the presenting bank.

## Running it

```
dotnet run --project src/ChequeBatch.Cli -- sample/clean-batch.txt --date 2026-06-30
```

`--date` is the processing date. It is injected everywhere rather than read from
the clock, so a re-run of last Tuesday's file behaves the way it did last
Tuesday, and so the tests can sit on a fixed date.

Exit codes are meant to be read by a scheduler:

| Code | Meaning |
| --- | --- |
| 0 | the batch balances and settles |
| 1 | the batch is refused, the file does not agree with its trailer |
| 2 | the file could not be read at all |

`--out <dir>` writes `accepted.txt` and `rejected.txt` for the next step in the
chain.

## The sample files

All four are written for a processing date of 2026-06-30.

| File | What it is | Exit code |
| --- | --- | --- |
| `sample/clean-batch.txt` | eight good records, balances | 0 |
| `sample/mixed-batch.txt` | one of every rejection, and the file is still whole | 0 |
| `sample/short-batch.txt` | a record lost in transit, trailer unchanged | 1 |
| `sample/torn-batch.txt` | a line truncated on transfer | 1 |

`mixed-batch.txt` is the interesting one: eight of its eleven records are
rejected, three go through, and the batch still settles, because the file
contains exactly what it says it contains. `short-batch.txt` is the opposite, and the one that would
otherwise go through unnoticed.

## Tests

`tests/ChequeBatch.Tests` covers the field rules at their boundaries, the two
kinds of duplicate, a malformed line becoming a rejection instead of an
exception, and each way the control check can fail, including the unreadable
line whose total happens to match.

I wrote these on a machine without the .NET SDK on it, so run them yourself
before you trust the count:

```
dotnet test
```

## Layout

```
src/ChequeBatch/          the library: parser, validator, processor
src/ChequeBatch.Cli/      the command line tool
tests/ChequeBatch.Tests/  xUnit tests
sample/                   four batch files
```

No third party dependencies in the library.
