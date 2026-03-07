using System.Net;

namespace CommunityManagement.Infrastructure.Services;

public static class EmailTemplates
{
    private static string E(string s) => WebUtility.HtmlEncode(s);
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

    public static string AnnouncementPublished(
        string orgName, string fullName, string title, string body, string category, string authorName)
    {
        var categoryLabel = category switch
        {
            "general" => "Genel",
            "urgent" => "Acil",
            "maintenance" => "Bakım",
            "meeting" => "Toplantı",
            "financial" => "Mali",
            "other" => "Diğer",
            _ => category
        };

        var truncatedBody = body.Length > 500 ? body[..500] + "..." : body;

        return Wrap(orgName, $"Sayın {fullName},",
            $"""
            <p>Yeni bir duyuru yayınlandı:</p>
            <p><strong>{title}</strong></p>
            <p style="color:#6b7280;font-size:13px;">Kategori: {categoryLabel} &middot; Yazan: {authorName}</p>
            <p style="white-space:pre-wrap;">{truncatedBody}</p>
            <p>Daha fazla bilgi ve tüm eklentiler için KomşuNet uygulamasını ziyaret edin.</p>
            """);
    }

    // --- Arıza Bildirimi: Yeni arıza (admin'lere) ---
    public static string MaintenanceRequestCreated(
        string orgName, string fullName, string title, string category, string priority,
        string reportedByName, string locationInfo)
    {
        var categoryLabel = MapCategory(category);
        var priorityLabel = MapPriority(priority);
        var priorityColor = priority switch { "acil" => "#dc2626", "yuksek" => "#f59e0b", _ => "#6b7280" };

        return Wrap(orgName, $"Sayın {E(fullName)},", $"""
            <p>Yeni bir arıza bildirimi yapıldı:</p>
            <p><strong>{E(title)}</strong></p>
            <p style="color:#6b7280;font-size:13px;">
                Kategori: {categoryLabel} &middot;
                Öncelik: <span style="color:{priorityColor};font-weight:bold;">{priorityLabel}</span> &middot;
                Bildiren: {E(reportedByName)}
            </p>
            <p>Konum: {E(locationInfo)}</p>
            <p>Detaylar için KomşuNet uygulamasını ziyaret edin.</p>
            """);
    }

    // --- Arıza Bildirimi: Durum değişikliği (sakine) ---
    public static string MaintenanceRequestStatusChanged(
        string orgName, string fullName, string title, string newStatus, string? note)
    {
        var statusLabel = MapStatus(newStatus);
        var extraMessage = newStatus == "resolved"
            ? "<p style=\"margin-top:12px;color:#16a34a;\"><strong>Arızanız çözüldü! Memnuniyet puanı vermek ister misiniz?</strong></p>"
            : "";

        return Wrap(orgName, $"Sayın {E(fullName)},", $"""
            <p>Arıza bildiriminizin durumu güncellendi:</p>
            <p><strong>{E(title)}</strong></p>
            <p>Yeni durum: <strong>{statusLabel}</strong></p>
            {(note is not null ? $"<p style=\"color:#6b7280;\">Not: {E(note)}</p>" : "")}
            {extraMessage}
            <p>Detaylar için KomşuNet uygulamasını ziyaret edin.</p>
            """);
    }

    // --- Arıza Bildirimi: Yeni yorum ---
    public static string MaintenanceRequestComment(
        string orgName, string fullName, string title, string commentByName, string commentContent)
    {
        var truncated = commentContent.Length > 300 ? commentContent[..300] + "..." : commentContent;

        return Wrap(orgName, $"Sayın {E(fullName)},", $"""
            <p>Arıza bildiriminize yeni bir yorum eklendi:</p>
            <p><strong>{E(title)}</strong></p>
            <p style="color:#6b7280;font-size:13px;">Yazan: {E(commentByName)}</p>
            <p style="white-space:pre-wrap;background:#f9fafb;padding:12px;border-radius:8px;">{E(truncated)}</p>
            <p>Detaylar için KomşuNet uygulamasını ziyaret edin.</p>
            """);
    }

    // --- Arıza Bildirimi: SLA aşıldı (admin'lere) ---
    public static string MaintenanceRequestSlaBreached(
        string orgName, string fullName, string title, string category, string priority,
        string reportedByName, DateTimeOffset slaDeadline)
    {
        var categoryLabel = MapCategory(category);
        return Wrap(orgName, $"Sayın {E(fullName)},", $"""
            <p style="color:#dc2626;font-weight:bold;">SLA süresi aşıldı!</p>
            <p><strong>{E(title)}</strong></p>
            <p style="color:#6b7280;font-size:13px;">
                Kategori: {categoryLabel} &middot; Bildiren: {E(reportedByName)}
            </p>
            <p>Hedef çözüm zamanı: <strong>{slaDeadline:dd.MM.yyyy HH:mm}</strong></p>
            <p>Lütfen en kısa sürede ilgilenin.</p>
            """);
    }

    // --- Helper'lar ---
    private static string MapCategory(string cat) => cat switch
    {
        "elektrik" => "Elektrik", "su_tesisati" => "Su Tesisatı",
        "asansor" => "Asansör", "ortak_alan" => "Ortak Alan",
        "boya_badana" => "Boya/Badana", "isitma_sogutma" => "Isıtma/Soğutma",
        "guvenlik" => "Güvenlik", "diger" => "Diğer", _ => cat
    };

    private static string MapPriority(string p) => p switch
    {
        "dusuk" => "Düşük", "normal" => "Normal",
        "yuksek" => "Yüksek", "acil" => "Acil", _ => p
    };

    private static string MapStatus(string s) => s switch
    {
        "reported" => "Bildirildi", "in_review" => "İnceleniyor",
        "assigned" => "Atandı", "in_progress" => "İşlemde",
        "resolved" => "Çözüldü", "closed" => "Kapatıldı",
        "cancelled" => "İptal Edildi", _ => s
    };
}
