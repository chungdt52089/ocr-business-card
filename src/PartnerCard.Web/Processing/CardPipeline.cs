using System.Diagnostics;
using Microsoft.Extensions.Options;
using PartnerCard.Web.Audit;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Observability;
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
        using var span = Telemetry.StartPipelineSpan("pipeline.extract");

        // Nhãn model cho metric. Chưa tới bước Extract thì lấy theo cấu hình; tới rồi thì lấy số thật.
        var model = Options.Extractor == ExtractorNames.Fake ? ExtractorNames.Fake : Options.Model;

        // ---- Validate ---------------------------------------------------------------
        ImageValidation.Result validation;
        using (Telemetry.Source.StartActivity("validate"))
        {
            validation = ImageValidation.Validate(imageBase64, mimeType, Options.MaxImageBytes);
        }

        if (!validation.Ok)
        {
            EndExtract(span, started, model, "rejected", validation.ErrorCode);
            return Rejected(validation.ErrorCode!, validation.Message!);
        }

        try
        {
            // ---- Extract ------------------------------------------------------------
            var raw = await extractor.ExtractRawAsync(
                validation.Bytes, mimeType, languageHint, sourceName, ct);

            // ---- Guard trên JSON thô, TRƯỚC khi deserialize -------------------------
            GuardResult verdict;
            CardExtractionResult? card;
            using (Telemetry.Source.StartActivity("guard"))
            {
                verdict = guard.Check(raw.Json, GuardBranch.Extraction);
                card = verdict.IsBlocked ? null : CardJson.Deserialize(verdict.CleanedJson!);
            }

            model = raw.Usage.Model;
            span?.SetTag("partnercard.guard.warnings", verdict.Warnings.Count);
            Telemetry.GuardWarnings.Add(verdict.Warnings.Count, new KeyValuePair<string, object?>("partnercard.branch", "extract"));

            if (verdict.IsBlocked)
            {
                // Chặn là chặn. Không thử lại — thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế (G-13).
                LogGuardBlock(sessionId, ExtractTool, verdict, Elapsed(started));
                EndExtract(span, started, model, "rejected", "guard_blocked");

                return new ExtractOutcome(
                    Ok: false,
                    Card: null,
                    ReviewFields: [],
                    ErrorCode: "guard_blocked",
                    Message: "Kết quả đọc được không hợp lệ nên đã bị chặn.",
                    Warnings: verdict.Warnings);
            }

            // ---- deserialize (đã chạy trong span guard ở trên) -----------------------
            if (card is null)
            {
                EndExtract(span, started, model, "rejected", "guard_blocked");
                return Rejected("guard_blocked", "Kết quả đọc được không hợp lệ nên đã bị chặn.");
            }

            // ---- Normalize → Confidence ---------------------------------------------
            var normalized = Normalize(card);
            var scored = Score(normalized.Card);

            var result = scored.Card with { FieldConfidence = scored.FieldConfidence };

            span?.SetTag("partnercard.is_business_card", result.IsBusinessCard);
            span?.SetTag("partnercard.fields_below_threshold", scored.ReviewFields.Count);
            // Ảnh không phải danh thiếp vẫn là Ok=true ở tầng đường ống — màn chụp mới là chỗ từ chối.
            EndExtract(span, started, model, result.IsBusinessCard ? "extracted" : "rejected", null);

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
            EndExtract(span, started, model, "error", ex.Code);

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
            EndExtract(span, started, model, "error", "extract_failed");

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
        using var span = Telemetry.StartPipelineSpan("pipeline.save");

        try
        {
            // ---- Normalize → Confidence ---------------------------------------------
            var normalized = Normalize(draft.Card);
            var scored = Score(normalized.Card);
            var card = scored.Card with { FieldConfidence = scored.FieldConfidence };

            span?.SetTag("partnercard.is_business_card", card.IsBusinessCard);
            span?.SetTag("partnercard.fields_below_threshold", scored.ReviewFields.Count);

            // ---- Guard trên DTO đã serialize -----------------------------------------
            GuardResult verdict;
            CardExtractionResult? cleaned;
            using (Telemetry.Source.StartActivity("guard"))
            {
                verdict = guard.Check(CardJson.Serialize(card), GuardBranch.Save);
                cleaned = verdict.IsBlocked ? null : CardJson.Deserialize(verdict.CleanedJson!);
            }

            span?.SetTag("partnercard.guard.warnings", verdict.Warnings.Count);
            Telemetry.GuardWarnings.Add(verdict.Warnings.Count, new KeyValuePair<string, object?>("partnercard.branch", "save"));

            if (verdict.IsBlocked)
            {
                LogGuardBlock(session, SaveTool, verdict, Elapsed(started));
                TagOutcome(span, "rejected", "guard_blocked");

                return new SaveOutcome(
                    false, null, null, "guard_blocked",
                    "Hồ sơ không hợp lệ nên chưa được lưu.", verdict.Warnings);
            }

            // Lưu bản ĐÃ DỌN, không phải bản trước guard. SG-4…SG-8 xử lý bằng cách xoá về rỗng và ghi
            // warnings (SPEC mục 7), áp cả ở nhánh này — lưu `card` thì email rác vẫn vào kho, chỉ kèm
            // một cảnh báo không ai đọc. save_partner không có form nào kiểm trước, nên đây là chốt duy nhất.
            // (deserialize đã chạy trong span guard ở trên)
            if (cleaned is null)
            {
                TagOutcome(span, "rejected", "guard_blocked");
                return new SaveOutcome(
                    false, null, null, "guard_blocked",
                    "Hồ sơ không hợp lệ nên chưa được lưu.", verdict.Warnings);
            }

            var candidate = ToPartner(draft, cleaned);

            // Span persist bọc cả chống trùng lẫn ghi — cả hai đều là lượt đọc/ghi kho.
            Partner saved;
            using (Telemetry.Source.StartActivity("persist"))
            {
                // ---- Chống trùng (SPEC mục 8 — chỉ so email) -------------------------
                if (!draft.AllowDuplicate)
                {
                    var duplicates = await store.FindDuplicatesAsync(candidate, ct);
                    if (duplicates.Count > 0)
                    {
                        // Không tự gộp. Gộp là hành vi của người dùng (F-05).
                        TagOutcome(span, "rejected", "duplicate");
                        return new SaveOutcome(
                            false, null, duplicates[0], "duplicate",
                            "Đã có hồ sơ dùng chung email này.", verdict.Warnings);
                    }
                }

                // ---- Persist ---------------------------------------------------------
                saved = await store.UpsertAsync(candidate, ct);
            }

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

            TagOutcome(span, "saved", null);
            // Human in the loop: bao nhiêu trường người đã sửa trước khi bấm Lưu. Chỉ đếm (luật cứng 9).
            Telemetry.HumanEdits.Add(draft.EditedFields.Count);
            return new SaveOutcome(true, saved.PartnerId, null, null, null, verdict.Warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            TagOutcome(span, "error", "save_failed");
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

    /// <summary>Normalize trong span riêng — đúng lời gọi như trước H-02, chỉ thêm span.</summary>
    private static NormalizedCard Normalize(CardExtractionResult card)
    {
        using var _ = Telemetry.Source.StartActivity("normalize");
        return Normalizer.Normalize(card with { Warnings = [] });
    }

    /// <summary>Confidence trong span riêng — đúng lời gọi như trước H-02, chỉ thêm span.</summary>
    private ScoredCard Score(CardExtractionResult card)
    {
        using var _ = Telemetry.Source.StartActivity("confidence");
        return Confidence.Evaluate(card, Options.ConfidenceReviewThreshold);
    }

    /// <summary>
    /// Kết cục của một lượt đường ống, gắn lên span gốc. Chỉ mã — không thông điệp, không nội dung
    /// thẻ (luật cứng 9). Chỉ <c>error</c> mới đặt trạng thái Error; từ chối là kết cục bình thường.
    /// </summary>
    private static void TagOutcome(Activity? span, string outcome, string? errorCode)
    {
        if (span is null)
        {
            return;
        }

        span.SetTag("partnercard.outcome", outcome);

        if (errorCode is not null)
        {
            span.SetTag("partnercard.error_code", errorCode);
        }

        if (outcome == "error")
        {
            span.SetStatus(ActivityStatusCode.Error, errorCode);
        }
    }

    /// <summary>Kết cục nhánh trích xuất: gắn lên span gốc và ghi histogram độ trễ.</summary>
    private static void EndExtract(Activity? span, long started, string model, string outcome, string? errorCode)
    {
        TagOutcome(span, outcome, errorCode);

        Telemetry.ExtractDuration.Record(
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("gen_ai.request.model", model),
            new KeyValuePair<string, object?>("partnercard.outcome", outcome));
    }

    private static ExtractOutcome Rejected(string code, string message) =>
        new(false, null, [], code, message, []);

    private static int CountFilled(IReadOnlyDictionary<string, double> confidence) =>
        confidence.Count(pair => pair.Value > 0);

    private static double Average(IReadOnlyDictionary<string, double> confidence) =>
        confidence.Count == 0 ? 0 : Math.Round(confidence.Values.Average(), 4);

    private static int Elapsed(long started) =>
        (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
