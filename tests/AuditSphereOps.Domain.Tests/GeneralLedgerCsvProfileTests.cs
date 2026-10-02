using System.Text;
using AuditSphereOps.Application.Accounting;
namespace AuditSphereOps.Domain.Tests;

public sealed class GeneralLedgerCsvProfileTests
{
    [Fact] public void ExactBoundedProfilePreservesJournalIdentityAndSixDecimalAmounts() { var p = GeneralLedgerCsvProfile.Parse("source.csv", Encoding.UTF8.GetBytes(GeneralLedgerUploadSeed.Csv)); Assert.Equal("FY26", p.PeriodCode); Assert.Equal("STAT", p.BookCode); var j = Assert.Single(p.Transactions); Assert.Equal(2, j.Lines.Count); Assert.Equal(100.123456m, j.Lines[0].FunctionalAmount); Assert.Equal(-100.123456m, j.Lines[1].OriginalAmount); }
    [Theory]
    [InlineData("precision")]
    [InlineData("date")]
    [InlineData("quotes")]
    [InlineData("currency")]
    [InlineData("metadata")]
    [InlineData("column")]
    public void MalformedAndAmbiguousRowsAreNotGuessed(string kind) { var text = kind switch { "precision" => GeneralLedgerUploadSeed.Csv.Replace("100.123456", "100.1234567"), "date" => GeneralLedgerUploadSeed.Csv.Replace("2026-06-30", "2026-02-30"), "quotes" => GeneralLedgerUploadSeed.Csv + "\"unfinished", "currency" => GeneralLedgerUploadSeed.Csv.Replace("L-2,D-1", "L-2,D-2").Replace("FY26,STAT,SYN-GL-CSV,QAR,J-1,L-2", "FY26,STAT,SYN-GL-CSV,USD,J-1,L-2"), "metadata" => GeneralLedgerUploadSeed.Csv.Replace("L-2,D-1", "L-2,D-2"), _ => GeneralLedgerUploadSeed.Csv.Replace("PeriodCode", "UnsupportedColumn") }; Assert.Throws<FormatException>(() => GeneralLedgerCsvProfile.Parse("source.csv", Encoding.UTF8.GetBytes(text))); }
    [Fact] public void TransportAndRowCapsAreExplicit() { Assert.Throws<FormatException>(() => GeneralLedgerCsvProfile.Parse("source.xlsx", [1])); Assert.Throws<FormatException>(() => GeneralLedgerCsvProfile.Parse("source.csv", new byte[GeneralLedgerCsvProfile.MaxBytes + 1])); var row = GeneralLedgerUploadSeed.Csv.Split('\n')[1]; var text = GeneralLedgerUploadSeed.Csv.Split('\n')[0] + "\n" + string.Join('\n', Enumerable.Repeat(row, 5001)); Assert.Throws<FormatException>(() => GeneralLedgerCsvProfile.Parse("source.csv", Encoding.UTF8.GetBytes(text))); }
}
