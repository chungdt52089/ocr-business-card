using System.Text.Json;
using PartnerCard.Web.Processing;

namespace PartnerCard.Tests.Processing;

/// <summary>
/// TEST-SPEC mục 5 — các ca thuộc T-06.
///
/// Guard nhận **chuỗi JSON**, không nhận record (SPEC mục 7). Đó là điều làm SG-1 có thật:
/// deserialize vào record C# là khoá lạ bị nuốt im lặng, và ca G-02 không thể đỏ được nữa.
///
/// G-11, G-12, G-13 không ở đây — chúng kiểm "guard chặn thì audit ghi gì", cần đường ống
/// và <c>IAuditLogger</c>, tức là T-06b.
/// </summary>
[Trait("Category", "Guard")]
public sealed class SchemaGuardTests
{
    private static readonly SchemaGuard Guard = new();

    // =====================================================================================
    // G-14 — ca quan trọng nhất, viết trước tiên.
    // =====================================================================================

    /// <summary>
    /// <c>HostileExtractor</c> cố tình trả rác: mọi trường điền đầy, <c>isBusinessCard = false</c>,
    /// email không có <c>@</c>, <c>company</c> là ```` ```json ````.
    ///
    /// Guard phải **chặn**. Nếu ca này không đỏ khi gỡ guard ra thì lưới an toàn không tồn tại.
    /// Đây cũng là beat demo mạnh nhất của dự án: đưa ảnh phong cảnh vào, nó từ chối.
    /// </summary>
    [Fact]
    public void G14_ket_qua_rac_bi_chan()
    {
        var verdict = Guard.Check(HostilePayload.Json, GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeTrue();
        verdict.IsClean.Should().BeFalse();
        verdict.BlockCode.Should().Be("SG-7",
            "nói không đọc được thì không được đồng thời đưa ra dữ liệu — đó là US-02");
    }

    [Fact]
    public void G14_ket_qua_rac_khong_bao_gio_lot_ra_duoi_dang_du_lieu()
    {
        var verdict = Guard.Check(HostilePayload.Json, GuardBranch.Extraction);

        // Bị chặn nghĩa là không có gì đi tiếp. Không phải "đi tiếp nhưng đã dọn".
        verdict.IsBlocked.Should().BeTrue();
        verdict.CleanedJson.Should().BeNull();
    }

    // =====================================================================================
    // G-01 → G-10
    // =====================================================================================

    [Fact]
    public void G01_ket_qua_hop_le_day_du_thi_sach_va_khong_canh_bao()
    {
        var verdict = Guard.Check(CardJson.Valid(), GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeFalse();
        verdict.Warnings.Should().BeEmpty();
        verdict.IsClean.Should().BeTrue();
    }

    [Fact]
    public void G02_khoa_la_bi_bo_ghi_canh_bao_nhung_van_hop_le()
    {
        var verdict = Guard.Check(CardJson.Valid(extra: "\"note\": \"mô hình nói thêm\","), GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeFalse();
        verdict.Warnings.Should().ContainSingle().Which.Code.Should().Be("SG-1");
        verdict.CleanedJson.Should().NotContain("note");
    }

    [Fact]
    public void G03_thieu_khoa_bat_buoc_thi_tra_loi_khong_luu()
    {
        var verdict = Guard.Check(CardJson.Valid(omit: "company"), GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeTrue();
        verdict.BlockCode.Should().Be("SG-2");
    }

    [Fact]
    public void G04_email_thieu_a_coi_bi_xoa_ve_rong_va_ghi_canh_bao()
    {
        var verdict = Guard.Check(CardJson.Valid(emails: "[\"an.nguyen\"]"), GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeFalse();
        verdict.Warnings.Should().Contain(w => w.Code == "SG-4" && w.Field == "emails");
        verdict.CleanedJson.Should().NotContain("an.nguyen");
    }

    [Fact]
    public void G05_dien_thoai_lan_chu_bi_xoa_ve_rong_va_ghi_canh_bao()
    {
        var verdict = Guard.Check(
            CardJson.Valid(phones: "[\"gọi số 0912345678\"]"), GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeFalse();
        verdict.Warnings.Should().Contain(w => w.Code == "SG-5" && w.Field == "phones");
        verdict.CleanedJson.Should().NotContain("gọi số");
    }

    [Theory]
    [InlineData("N/A", "company")]          // G-06
    [InlineData("null", "company")]
    [InlineData("unknown", "company")]
    [InlineData("không rõ", "company")]
    public void G06_gia_tri_la_dau_vet_mo_hinh_noi_chuyen_thi_ve_rong(string value, string field)
    {
        var verdict = Guard.Check(CardJson.Valid(company: value), GuardBranch.Extraction);

        verdict.Warnings.Should().Contain(w => w.Code == "SG-6" && w.Field == field);
        verdict.CleanedJson.Should().NotContain(value);
    }

    [Fact]
    public void G07_cau_tu_choi_cua_mo_hinh_trong_jobTitle_bi_ve_rong()
    {
        var verdict = Guard.Check(
            CardJson.Valid(jobTitle: "Tôi không thể đọc được"), GuardBranch.Extraction);

        verdict.Warnings.Should().Contain(w => w.Code == "SG-6" && w.Field == "jobTitle");
        verdict.CleanedJson.Should().NotContain("Tôi không thể");
    }

    [Fact]
    public void G08_gia_tri_boc_trong_rao_json_bi_ve_rong()
    {
        var verdict = Guard.Check(
            CardJson.Valid(company: "```json Halbrook Logistics"), GuardBranch.Extraction);

        verdict.Warnings.Should().Contain(w => w.Code == "SG-6" && w.Field == "company");
        verdict.CleanedJson.Should().NotContain("```json");
    }

    [Fact]
    public void G09_truong_dai_bat_thuong_bi_ve_rong_va_ghi_canh_bao()
    {
        var verdict = Guard.Check(
            CardJson.Valid(fullName: new string('a', 150)), GuardBranch.Extraction);

        verdict.Warnings.Should().Contain(w => w.Code == "SG-8" && w.Field == "fullName");
        verdict.CleanedJson.Should().NotContain(new string('a', 150));
    }

    [Fact]
    public void G10_noi_khong_phai_danh_thiep_ma_van_co_company_thi_bi_chan()
    {
        var verdict = Guard.Check(
            CardJson.Valid(isBusinessCard: "false", rejectReason: "Ảnh phong cảnh"),
            GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeTrue();
        verdict.BlockCode.Should().Be("SG-7");
    }

    [Fact]
    public void The_am_tinh_dung_cach_thi_khong_bi_chan()
    {
        // isBusinessCard = false VÀ mọi trường rỗng — đây mới là hình dạng hợp lệ của ca âm tính.
        var verdict = Guard.Check(CardJson.NegativeCard(), GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeFalse();
    }

    // ---- SG-3 và danh sách miễn trừ (SPEC mục 7) ----------------------------------

    [Fact]
    public void SG3_truong_co_gia_tri_ma_thieu_confidence_thi_bi_chan()
    {
        var verdict = Guard.Check(
            CardJson.Valid(confidence: "{ \"fullName\": 0.9, \"phones\": 0.9, \"emails\": 0.9 }"),
            GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeTrue();
        verdict.BlockCode.Should().Be("SG-3");
    }

    [Fact]
    public void SG3_mien_tru_detectedLanguage_isBusinessCard_va_rejectReason()
    {
        // Thiếu danh sách miễn trừ thì SG-3 chặn MỌI tấm thẻ hợp lệ, vì detectedLanguage
        // luôn có giá trị mà không bao giờ có mục trong fieldConfidence.
        var verdict = Guard.Check(CardJson.Valid(), GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeFalse();
    }

    // ---- Hai nhánh, hai bộ luật (SPEC mục 2) --------------------------------------

    [Fact]
    public void Nhanh_luu_bo_qua_SG1_vi_JSON_do_chinh_ta_serialize()
    {
        var json = CardJson.Valid(extra: "\"note\": \"khoá lạ\",");

        Guard.Check(json, GuardBranch.Extraction).Warnings.Should().Contain(w => w.Code == "SG-1");
        Guard.Check(json, GuardBranch.Save).Warnings.Should().NotContain(w => w.Code == "SG-1");
    }

    [Fact]
    public void Nhanh_luu_bo_qua_SG2_vi_khoa_thieu_khong_the_xay_ra()
    {
        var json = CardJson.Valid(omit: "company");

        Guard.Check(json, GuardBranch.Extraction).BlockCode.Should().Be("SG-2");
        Guard.Check(json, GuardBranch.Save).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void Nhanh_luu_van_ap_dung_SG3_den_SG8()
    {
        var verdict = Guard.Check(
            CardJson.Valid(isBusinessCard: "false", rejectReason: "gì đó"), GuardBranch.Save);

        verdict.IsBlocked.Should().BeTrue();
        verdict.BlockCode.Should().Be("SG-7");
    }

    // ---- Đầu vào không phải JSON ---------------------------------------------------

    [Theory]
    [InlineData("đây không phải JSON")]
    [InlineData("")]
    [InlineData("[1, 2, 3]")]
    public void JSON_hong_hoac_khong_phai_object_thi_bi_chan_chu_khong_nem_exception(string json)
    {
        var verdict = Guard.Check(json, GuardBranch.Extraction);

        verdict.IsBlocked.Should().BeTrue();
        verdict.BlockCode.Should().Be("SG-0");
    }

    // =====================================================================================

    /// <summary>Đúng thứ rác mà <c>HostileExtractor</c> trả về (TEST-SPEC G-14).</summary>
    private static class HostilePayload
    {
        public const string Json = """
            {
              "isBusinessCard": false,
              "rejectReason": "",
              "fullName": "Nguyễn Văn An",
              "jobTitle": "Giám đốc",
              "company": "```json",
              "phones": ["0912345678"],
              "emails": ["an.nguyen"],
              "website": "abc.example",
              "address": "12 Nguyễn Huệ",
              "detectedLanguage": "vi",
              "searchAlias": "",
              "fieldConfidence": {
                "fullName": 0.99, "jobTitle": 0.99, "company": 0.99, "phones": 0.99,
                "emails": 0.99, "website": 0.99, "address": 0.99, "searchAlias": 0.0
              }
            }
            """;
    }

    private static class CardJson
    {
        public static string Valid(
            string isBusinessCard = "true",
            string rejectReason = "",
            string fullName = "Marcus Feld",
            string jobTitle = "Operations Director",
            string company = "Halbrook Logistics",
            string phones = "[\"+15550142887\"]",
            string emails = "[\"m.feld@halbrook-logistics.example\"]",
            string website = "https://halbrook-logistics.example",
            string address = "418 Kestrel Avenue",
            string? confidence = null,
            string extra = "",
            string? omit = null)
        {
            confidence ??= """
                {
                  "fullName": 0.97, "jobTitle": 0.95, "company": 0.93, "phones": 0.99,
                  "emails": 0.99, "website": 0.9, "address": 0.9, "searchAlias": 0.0
                }
                """;

            var fields = new List<(string Key, string Value)>
            {
                ("isBusinessCard", isBusinessCard),
                ("rejectReason", Quote(rejectReason)),
                ("fullName", Quote(fullName)),
                ("jobTitle", Quote(jobTitle)),
                ("company", Quote(company)),
                ("phones", phones),
                ("emails", emails),
                ("website", Quote(website)),
                ("address", Quote(address)),
                ("detectedLanguage", Quote("en")),
                ("searchAlias", Quote("")),
                ("fieldConfidence", confidence),
            };

            var body = string.Join(",\n  ", fields
                .Where(f => f.Key != omit)
                .Select(f => $"\"{f.Key}\": {f.Value}"));

            return "{\n  " + extra + body + "\n}";
        }

        public static string NegativeCard() => Valid(
            isBusinessCard: "false",
            rejectReason: "Ảnh phong cảnh, không phải danh thiếp.",
            fullName: "",
            jobTitle: "",
            company: "",
            phones: "[]",
            emails: "[]",
            website: "",
            address: "",
            confidence: """
                {
                  "fullName": 0.0, "jobTitle": 0.0, "company": 0.0, "phones": 0.0,
                  "emails": 0.0, "website": 0.0, "address": 0.0, "searchAlias": 0.0
                }
                """);

        /// <summary>
        /// Bọc chuỗi mà **không** escape ký tự ngoài ASCII. Nếu để mặc định thì `không rõ`
        /// thành `không rõ` trong JSON, và các khẳng định `NotContain("không rõ")`
        /// sẽ xanh ngay cả khi guard chẳng dọn gì — tức là xanh giả.
        /// </summary>
        private static string Quote(string value) => JsonSerializer.Serialize(value, RelaxedOptions);

        private static readonly JsonSerializerOptions RelaxedOptions = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
