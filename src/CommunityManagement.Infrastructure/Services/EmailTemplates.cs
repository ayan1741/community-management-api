namespace CommunityManagement.Infrastructure.Services;

public static class EmailTemplates
{
    private static string Wrap(string orgName, string title, string body) => $$"""
        <!DOCTYPE html>
        <html lang="tr">
        <head><meta charset="UTF-8"><style>
          body { font-family: Inter, sans-serif; background: #f4f4f5; margin: 0; padding: 24px; }
          .card { background: #fff; border-radius: 12px; max-width: 520px; margin: 0 auto; padding: 32px; }
          .header { color: #1e3a5f; font-size: 20px; font-weight: bold; margin-bottom: 8px; }
          .subtext { color: #6b7280; font-size: 14px; margin-bottom: 24px; }
          .amount { font-size: 28px; font-weight: bold; color: #2563eb; }
          .footer { margin-top: 32px; font-size: 12px; color: #9ca3af; border-top: 1px solid #f3f4f6; padding-top: 16px; }
        </style></head>
        <body><div class="card">
          <div class="header">KomşuNet &mdash; {{orgName}}</div>
          <div class="subtext">{{title}}</div>
          {{body}}
          <div class="footer">Bu e-postayı KomşuNet üzerinden alıyorsunuz.</div>
        </div></body></html>
        """;

    public static string DueReminder(string orgName, string fullName, string periodName, decimal totalOwed)
        => Wrap(orgName, $"Sayın {fullName},",
            $"""
            <p>Aidat ödeme hatırlatması:</p>
            <p><strong>Dönem:</strong> {periodName}</p>
            <p class="amount">{totalOwed:N2} TL</p>
            <p>Ödemenizi gerçekleştirmek için site yönetiminizle iletişime geçin.</p>
            """);

    public static string LateNotice(string orgName, string fullName, string periodName, decimal amount, int lateDays)
        => Wrap(orgName, $"Sayın {fullName},",
            $"""
            <p>Aidat ödemeniz <strong>{lateDays} gün</strong> gecikmiştir.</p>
            <p><strong>Dönem:</strong> {periodName}</p>
            <p class="amount">{amount:N2} TL</p>
            <p>Lütfen en kısa sürede ödemenizi gerçekleştirin.</p>
            """);

    public static string PaymentConfirmation(string orgName, string fullName, string receiptNumber, decimal amount, string periodName)
        => Wrap(orgName, $"Sayın {fullName},",
            $"""
            <p>Aidat ödemeniz başarıyla kaydedildi.</p>
            <p><strong>Dönem:</strong> {periodName}</p>
            <p><strong>Fiş No:</strong> {receiptNumber}</p>
            <p class="amount">{amount:N2} TL</p>
            <p>Teşekkür ederiz!</p>
            """);

    public static string PaymentCancellation(string orgName, string fullName, string receiptNumber, decimal amount)
        => Wrap(orgName, $"Sayın {fullName},",
            $"""
            <p>Bir ödeme kaydı iptal edildi.</p>
            <p><strong>Fiş No:</strong> {receiptNumber}</p>
            <p class="amount">{amount:N2} TL</p>
            <p>Sorularınız için site yönetiminizle iletişime geçin.</p>
            """);

    public static string MonthlyFinanceSummary(
        string orgName, string fullName, string monthYear,
        decimal totalIncome, decimal totalExpense, decimal netBalance,
        IReadOnlyList<(string Name, decimal Amount)> topCategories)
    {
        var catRows = string.Join("",
            topCategories.Select(c =>
                $"<tr><td style=\"padding:6px 12px;border-bottom:1px solid #f3f4f6;\">{c.Name}</td>" +
                $"<td style=\"padding:6px 12px;border-bottom:1px solid #f3f4f6;text-align:right;\">{c.Amount:N2} TL</td></tr>"));

        var netColor = netBalance >= 0 ? "#16a34a" : "#dc2626";

        return Wrap(orgName, $"Sayın {fullName},", $"""
            <p>{monthYear} dönemi gelir-gider özeti:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0;">
              <tr>
                <td style="padding:8px 12px;"><strong>Toplam Gelir</strong></td>
                <td style="padding:8px 12px;text-align:right;color:#16a34a;font-weight:bold;">{totalIncome:N2} TL</td>
              </tr>
              <tr>
                <td style="padding:8px 12px;"><strong>Toplam Gider</strong></td>
                <td style="padding:8px 12px;text-align:right;color:#dc2626;font-weight:bold;">{totalExpense:N2} TL</td>
              </tr>
              <tr style="border-top:2px solid #e5e7eb;">
                <td style="padding:8px 12px;"><strong>Net Bakiye</strong></td>
                <td style="padding:8px 12px;text-align:right;color:{netColor};font-weight:bold;font-size:18px;">{netBalance:N2} TL</td>
              </tr>
            </table>
            {(topCategories.Count > 0 ? $"""
            <p style="margin-top:16px;"><strong>En Büyük Gider Kalemleri:</strong></p>
            <table style="width:100%;border-collapse:collapse;">
              {catRows}
            </table>
            """ : "")}
            <p style="margin-top:16px;">Detaylı bilgi için KomşuNet uygulamasını ziyaret edin.</p>
            """);
    }
}
