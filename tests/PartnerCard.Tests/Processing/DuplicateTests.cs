using Microsoft.Extensions.Options;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Processing;

/// <summary>
/// TEST-SPEC mục 6 — chống trùng, chỉ so email (SPEC mục 8). Luật đã có từ T-02 (<c>FindDuplicatesAsync</c>)
/// và T-06b (nhánh <c>duplicate</c> của đường ống); T-14 viết hai ca mà BACKLOG giao cho nó.
///
/// Đi qua <see cref="CardPipeline.SaveAsync"/> chứ không gọi thẳng kho: "cảnh báo trùng" là thứ đường ống trả
/// về cho màn hình xác nhận và cho <c>save_partner</c>, không phải thứ kho tự nói.
/// </summary>
[Trait("Category", "Duplicate")]
public sealed class DuplicateTests : IDisposable
{
    private readonly TempDataDirectory _data = new();
    private readonly JsonPartnerStore _store;
    private readonly CardPipeline _pipeline;

    public DuplicateTests()
    {
        _store = JsonPartnerStore.LoadFrom(_data.Path, new SteppingClock());

        // Nhánh lưu không trích xuất (SPEC mục 2) — gọi tới mô hình là sai, nên cho nó nổ.
        _pipeline = new CardPipeline(
            new ThrowingExtractor(new InvalidOperationException("nhánh lưu không được gọi mô hình")),
            new SchemaGuard(), _store, new RecordingAuditLogger(),
            Options.Create(new PartnerCardOptions()), new SteppingClock());
    }

    public void Dispose()
    {
        _store.Dispose();
        _data.Dispose();
    }

    private Task<SaveOutcome> SaveAsync(CardExtractionResult card) =>
        _pipeline.SaveAsync(new PartnerDraft(null, card, string.Empty, string.Empty, []), "s1", CancellationToken.None);

    [Fact]
    public async Task D01_trung_email_chinh_xac_thi_canh_bao_trung()
    {
        var existing = await _store.UpsertAsync(
            PartnerFactory.New(email: "m.feld@halbrook-logistics.example"), CancellationToken.None);

        var outcome = await SaveAsync(CardBuilder.Valid() with { FullName = "M. Feld" });

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be("duplicate");
        outcome.DuplicateOf!.PartnerId.Should().Be(existing.PartnerId);

        var stored = await _store.SearchAsync(new PartnerQuery(), CancellationToken.None);
        stored.Should().HaveCount(1, "không tự gộp, cũng không lưu thêm khi người dùng chưa chọn (SPEC mục 8)");
    }

    [Fact]
    public async Task D02_trung_email_khac_hoa_thuong_van_canh_bao_trung()
    {
        // Ghi thẳng vào kho, không qua Normalizer — đúng dạng của dữ liệu mồi hay dữ liệu cũ. Đường ống hạ chữ
        // thường email của bản MỚI, nhưng không đụng tới bản đã nằm trong kho: phép so phải tự bỏ qua hoa thường.
        var existing = await _store.UpsertAsync(
            PartnerFactory.New(email: "M.Feld@Halbrook-Logistics.EXAMPLE"), CancellationToken.None);

        var outcome = await SaveAsync(CardBuilder.Valid());

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be("duplicate");
        outcome.DuplicateOf!.PartnerId.Should().Be(existing.PartnerId);
    }
}
