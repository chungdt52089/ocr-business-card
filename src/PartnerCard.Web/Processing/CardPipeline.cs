using System.Diagnostics;
using Microsoft.Extensions.Options;
using PartnerCard.Web.Audit;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Storage;

namespace PartnerCard.Web.Processing;

/// <summary>
/// Đường ống — SPEC mục 2. **Hai nhánh, không phải một chuỗi thẳng:**
///
/// <list type="bullet">
/// <item>Trích xuất: <c>Validate → Extract → Guard(JSON thô) → deserialize → Normalize → Confidence → Audit</c>.
/// Không persist — kết quả là bản nháp chờ người xác nhận (M-04).</item>
/// <item>Lưu: <c>Validate → Normalize → Confidence → Guard(DTO đã serialize) → Persist → Audit</c>.
/// Không extract — các trường đã do người sửa.</item>
/// </list>
///
/// Giao diện Blazor gọi đúng class này, không có đường đi riêng. Nếu giao diện có đường riêng
/// thì mọi bảo đảm ở đây đều vô nghĩa.
///
/// **Không exception nào được thoát ra ngoài** (SPEC mục 10.3): mọi lỗi thành kết quả có cấu trúc
/// kèm thông báo trung tính — không stack trace, không đường dẫn file, không tên khoá cấu hình.
/// </summary>
public sealed class CardPipeline(
    IExtractor extractor,
    ISchemaGuard guard,
    IPartnerStore store,
    IAuditLogger audit,
    IOptions<PartnerCardOptions> options,
    TimeProvider clock)
{
    private const string ExtractTool = "extract_business_card";
    private const string SaveTool = "save_partner";

    private PartnerCardOptions Options => options.Value;

    public async Task<ExtractOutcome> ExtractAsync(
        string imageBase64,
        string mimeType,
        string? languageHint,
        string? sourceName,
        CancellationToken ct,
        string sessionId = "web")
    {
        var started = Stopwatch.GetTimestamp();

        // ---- Validate ---------------------------------------------------------------
        var validation = ImageValidation.Validate(imageBase64, mimeType, Options.MaxImageBytes);
        if (!validation.Ok)
        {
            return Rejected(validation.ErrorCode!, validation.Message!);
        }

        try
        {
            // ---- Extract ------------------------------------------------------------
            var raw = await extractor.ExtractRawAsync(
                validation.Bytes, mimeType, languageHint, sourceName, ct);

            // ---- Guard trên JSON thô, TRƯỚC khi deserialize -------------------------
            var verdict = guard.Check(raw.Json, GuardBranch.Extraction);
            if (verdict.IsBlocked)
            {
                // Chặn là chặn. Không thử lại — thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế (G-13).
                LogGuardBlock(sessionId, ExtractTool, verdict, Elapsed(started));

                return new ExtractOutcome(
                    Ok: false,
                    Card: null,
                    ReviewFields: [],
                    ErrorCode: "guard_blocked",
                    Message: "Kết quả đọc được không hợp lệ nên đã bị chặn.",
                    Warnings: verdict.Warnings);
            }

            // ---- deserialize --------------------------------------------------------
            var card = CardJson.Deserialize(verdict.CleanedJson!);
            if (card is null)
            {
                return Rejected("guard_blocked", "Kết quả đọc được không hợp lệ nên đã bị chặn.");
            }

            // ---- Normalize → Confidence ---------------------------------------------
            var normalized = Normalizer.Normalize(card with { Warnings = [] });
            var scored = Confidence.Evaluate(normalized.Card, Options.ConfidenceReviewThreshold);

            var result = scored.Card with { FieldConfidence = scored.FieldConfidence };

            // ---- Audit ---------------------------------------------------------------
            audit.Log(new AuditEntry(
                Timestamp: clock.GetUtcNow(),
                SessionId: sessionId,
                Tool: ExtractTool,
                Level: AuditLevel.Info,
                IsBusinessCard: result.IsBusinessCard,
                FieldsFilled: CountFilled(scored.FieldConfidence),
                AvgConfidence: Average(scored.FieldConfidence),
                Warnings: verdict.Warnings.Count,
                LatencyMs: Elapsed(started),
                TokensIn: raw.Usage.TokensIn,
                TokensOut: raw.Usage.TokensOut,
                Model: raw.Usage.Model,
                PromptVersion: raw.Usage.PromptVersion));

            return new ExtractOutcome(
                Ok: true,
                Card: result,
                ReviewFields: scored.ReviewFields,
                ErrorCode: null,
                Message: null,
                Warnings: verdict.Warnings)
            {
                // Đi tiếp tới màn hình xác nhận rồi quay lại ở PartnerDraft khi người dùng bấm Lưu.
                Usage = raw.Usage,
            };
        }
        catch (ExtractorException ex)
        {
            // Đứng trước CẢ mắt OperationCanceledException bên dưới, không chỉ trước
            // catch (Exception): timeout của HttpClient chính là TaskCanceledException, mà mắt
            // đó thì rethrow — để sau là timeout bay thẳng ra khỏi đường ống (SPEC mục 4.1).
            //
            // Không thử lại. Với 429 thì thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế.
            LogExtractError(sessionId, ex.Code, Elapsed(started));

            return Rejected(ex.Code, ex.Message);
        }
        catch (OperationCanceledException)
        {
            // Người dùng huỷ là việc của người gọi, không phải lỗi để nuốt thành kết quả.
            throw;
        }
        catch (Exception)
        {
            // Thông báo trung tính, không lộ chi tiết nội bộ (SPEC mục 10.3).
            LogExtractError(sessionId, "extract_failed", Elapsed(started));

            return Rejected("extract_failed", "Không đọc được ảnh này. Thử chụp lại rõ hơn.");
        }
    }

    public async Task<SaveOutcome> SaveAsync(
        PartnerDraft draft,
        string? sessionId,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var session = string.IsNullOrWhiteSpace(sessionId) ? "web" : sessionId;

        try
        {
            // ---- Normalize → Confidence ---------------------------------------------
            var normalized = Normalizer.Normalize(draft.Card with { Warnings = [] });
            var scored = Confidence.Evaluate(normalized.Card, Options.ConfidenceReviewThreshold);
            var card = scored.Card with { FieldConfidence = scored.FieldConfidence };

            // ---- Guard trên DTO đã serialize -----------------------------------------
            var verdict = guard.Check(CardJson.Serialize(card), GuardBranch.Save);
            if (verdict.IsBlocked)
            {
                LogGuardBlock(session, SaveTool, verdict, Elapsed(started));

                return new SaveOutcome(
                    false, null, null, "guard_blocked",
                    "Hồ sơ không hợp lệ nên chưa được lưu.", verdict.Warnings);
            }

            var candidate = ToPartner(draft, card);

            // ---- Chống trùng (SPEC mục 8 — chỉ so email) -----------------------------
            if (!draft.AllowDuplicate)
            {
                var duplicates = await store.FindDuplicatesAsync(candidate, ct);
                if (duplicates.Count > 0)
                {
                    // Không tự gộp. Gộp là hành vi của người dùng (F-05).
                    return new SaveOutcome(
                        false, null, duplicates[0], "duplicate",
                        "Đã có hồ sơ dùng chung email này.", verdict.Warnings);
                }
            }

            // ---- Persist -------------------------------------------------------------
            var saved = await store.UpsertAsync(candidate, ct);

            // ---- Audit ---------------------------------------------------------------
            audit.Log(new AuditEntry(
                Timestamp: clock.GetUtcNow(),
                SessionId: session,
                Tool: SaveTool,
                Level: AuditLevel.Info,
                PartnerId: saved.PartnerId,
                ImageSha256: saved.ImageSha256,
                FieldsFilled: CountFilled(saved.FieldConfidence),
                AvgConfidence: Average(saved.FieldConfidence),
                Warnings: verdict.Warnings.Count,
                LatencyMs: Elapsed(started)));

            return new SaveOutcome(true, saved.PartnerId, null, null, null, verdict.Warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new SaveOutcome(
                false, null, null, "save_failed", "Không lưu được hồ sơ.", []);
        }
    }

    private Partner ToPartner(PartnerDraft draft, CardExtractionResult card)
    {
        var now = clock.GetUtcNow();

        return new Partner(
            PartnerId: draft.PartnerId ?? string.Empty,
            FullName: card.FullName,
            JobTitle: card.JobTitle,
            Company: card.Company,
            Phones: card.Phones,
            Emails: card.Emails,
            Website: card.Website,
            Address: card.Address,
            DetectedLanguage: card.DetectedLanguage,
            SearchAlias: card.SearchAlias,
            Industry: null,
            ShortDescription: null,
            MainProducts: [],
            EnrichmentSourceUrl: null,
            EnrichedAt: null,
            SourceImage: draft.SourceImage,
            ImageSha256: draft.ImageSha256,
            FieldConfidence: card.FieldConfidence,
            EditedFields: draft.EditedFields,
            // Số đo đi từ lần trích xuất, qua màn hình xác nhận, tới đây (SPEC mục 4.1).
            // Nhánh lưu không trích xuất nên tự nó không biết gì về lời gọi mô hình; đọc cấu
            // hình thay cho số thật là ghi "đã gọi gemini" cho cả hồ sơ người tự gõ.
            Extraction: new ExtractionMeta(
                Model: draft.Usage.Model,
                PromptVersion: draft.Usage.PromptVersion,
                ExtractedAt: now,
                LatencyMs: draft.Usage.LatencyMs),
            // Lưu là hành vi sau khi người đã xác nhận ở màn hình /review (US-03).
            Status: PartnerStatus.Confirmed,
            CreatedAt: now,
            UpdatedAt: now);
    }

    /// <summary>
    /// Trích xuất hỏng thì vẫn ghi một dòng — lời gọi này **đã chạm tới mô hình**, khác với việc
    /// Validate từ chối (SPEC mục 2). Chỉ ghi mã lỗi, không ghi gì của tấm thẻ.
    /// </summary>
    private void LogExtractError(string sessionId, string errorCode, int latencyMs) =>
        audit.Log(new AuditEntry(
            Timestamp: clock.GetUtcNow(),
            SessionId: sessionId,
            Tool: ExtractTool,
            Level: AuditLevel.Error,
            LatencyMs: latencyMs,
            ErrorCode: errorCode));

    private void LogGuardBlock(string sessionId, string tool, GuardResult verdict, int latencyMs) =>
        audit.Log(new AuditEntry(
            Timestamp: clock.GetUtcNow(),
            SessionId: sessionId,
            Tool: tool,
            Level: AuditLevel.GuardBlock,
            // Chỉ ghi MÃ lý do. Giá trị bị chặn không bao giờ đi vào nhật ký (G-12).
            BlockCode: verdict.BlockCode,
            Warnings: verdict.Warnings.Count,
            LatencyMs: latencyMs));

    private static ExtractOutcome Rejected(string code, string message) =>
        new(false, null, [], code, message, []);

    private static int CountFilled(IReadOnlyDictionary<string, double> confidence) =>
        confidence.Count(pair => pair.Value > 0);

    private static double Average(IReadOnlyDictionary<string, double> confidence) =>
        confidence.Count == 0 ? 0 : Math.Round(confidence.Values.Average(), 4);

    private static int Elapsed(long started) =>
        (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
