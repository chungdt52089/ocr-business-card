using System.Text.Json.Nodes;
using PartnerCard.Web.Extraction;

namespace PartnerCard.Tests.Extraction;

/// <summary>
/// Prompt là thứ quyết định phần lớn chất lượng đọc, nhưng chất lượng đó chỉ đo được ở bộ đo
/// T-08 với mô hình thật. Nhóm ca này chỉ giữ hai thứ mà `dotnet test` giữ được:
/// prompt **có nói đủ** các luật SPEC mục 4.5, và nó **không dạy đáp án** cho bộ đo.
/// </summary>
[Trait("Category", "Extract")]
public sealed class PromptsTests
{
    private static string ExpectedJsonPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "cards", "expected.json");

    [Fact]
    public void Co_phien_ban_prompt_va_no_khong_rong()
    {
        Prompts.Version.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    // Luật 1 — không suy luận. Đây là dạng bịa phổ biến nhất (ca X-07).
    [InlineData("Không đoán email từ tên miền website")]
    [InlineData("Không thêm mã quốc gia")]
    // Luật 2 — thà thiếu còn hơn sai.
    [InlineData("ĐỂ CHUỖI RỖNG")]
    // Luật 3 — giữ nguyên chữ gốc.
    [InlineData("株式会社")]
    // Luật 4 — không phải danh thiếp.
    [InlineData("isBusinessCard = false")]
    // searchAlias — ranh giới quan trọng nhất của nó.
    [InlineData("KHÔNG DỊCH CHỨC DANH")]
    // Thẻ song ngữ — luật thêm ở v1.2. Thiếu nó thì mô hình ghép hai hệ chữ vào một trường,
    // và đó là lỗ đặc tả chứ không phải lỗi đọc (SPEC mục 4.5).
    [InlineData("CHỌN MỘT, KHÔNG GHÉP")]
    [InlineData("Chỉ tự phiên âm khi thẻ KHÔNG in sẵn bản Latin")]
    // fieldConfidence — schema ép trả tám số, prompt phải nói chấm chúng thế nào.
    [InlineData("ĐỪNG ĐẶT 1.0 CHO MỌI TRƯỜNG THEO PHẢN XẠ")]
    [InlineData("0.5–0.8")]
    public void Prompt_noi_du_cac_luat_cua_SPEC_4_5(string fragment)
    {
        Prompts.ExtractCard.Should().Contain(fragment);
    }

    [Fact]
    public void Prompt_khong_chua_bat_ky_dap_an_nao_cua_bo_mau()
    {
        // Nhét một tấm thẻ của bộ đo vào prompt là dạy mô hình đáp án rồi tự đo lại chính mình:
        // con số nghiệm thu sẽ đẹp lên mà không có gì thật sự tốt hơn. Đúng loại "sai mà trông
        // như đúng" mà cả dự án được dựng để chặn.
        var expected = JsonNode.Parse(File.ReadAllText(ExpectedJsonPath))!.AsObject();

        string[] fields = ["fullName", "company", "website"];

        var answers = expected
            .SelectMany(card => fields.Select(field => card.Value![field]?.GetValue<string>()))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct()
            .ToList();

        answers.Should().NotBeEmpty("phải có đáp án để đối chiếu thì ca này mới có nghĩa");
        answers.Should().OnlyContain(answer => !Prompts.ExtractCard.Contains(answer!));
    }

    [Fact]
    public void Vi_du_trong_prompt_co_fieldConfidence_va_khong_toan_1_0()
    {
        // Hai ví dụ có tác dụng hơn mọi câu mô tả — nhưng ví dụ nào cũng 1.0 tất tay thì nó dạy
        // đúng cái thói quen ta vừa cấm ở trên. Ví dụ 2 phải có ít nhất một trường dưới 1.0.
        var examples = Prompts.ExtractCard[Prompts.ExtractCard.IndexOf("VÍ DỤ 1", StringComparison.Ordinal)..];

        examples.Should().Contain("fieldConfidence");
        examples.Should().Contain("0.7", "phải có một trường đọc được nhưng không sắc nét");
    }

    [Fact]
    public void Tam_truong_trong_phan_fieldConfidence_khop_dung_CardSchema()
    {
        // Prompt liệt kê thiếu một trường thì mô hình bỏ qua trường đó, và SG-3 chặn cả tấm thẻ.
        var scale = Prompts.ExtractCard[Prompts.ExtractCard.IndexOf("fieldConfidence — CHẤM", StringComparison.Ordinal)..];

        scale.Should().ContainAll(CardSchema.ConfidenceRequiredFields);
    }

    [Fact]
    public void Prompt_khong_bao_mo_hinh_boc_ket_qua_trong_markdown()
    {
        // Guard xoá mọi giá trị mang dấu vết ```json (SG-6), nên prompt phải nói ngược lại.
        Prompts.ExtractCard.Should().Contain("Không viết lời dẫn, không dùng markdown");
    }
}
