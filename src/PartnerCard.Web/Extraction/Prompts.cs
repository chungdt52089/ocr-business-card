namespace PartnerCard.Web.Extraction;

/// <summary>
/// Prompt gửi cho mô hình — SPEC mục 4.5.
///
/// <see cref="Version"/> là **nguồn sự thật duy nhất** cho <c>promptVersion</c>: nó nằm ngay
/// cạnh chuỗi thật sự được gửi đi. Để số này trong cấu hình thì T-09 tăng ở một file mà quên
/// file kia là hồ sơ ghi một phiên bản chưa từng được gửi — và con số đối chiếu của bộ đo trở
/// thành vô nghĩa đúng lúc nó cần có nghĩa nhất.
///
/// **Prompt viết bằng tiếng Việt, cố ý.** Nếu T-09 muốn thử bản tiếng Anh thì nó phải là **bản
/// dịch sát nghĩa, không đổi thêm một chữ nào khác**. Vừa dịch vừa sửa câu thì số đo trước–sau
/// không quy được cho nguyên nhân nào — mà cả T-09 tồn tại chỉ để làm đúng phép quy đó.
/// </summary>
public static class Prompts
{
    /// <summary>
    /// Tăng **mỗi lần sửa <see cref="ExtractCard"/>**, rồi chạy lại bộ đo và ghi cả số trước lẫn
    /// số sau vào <c>EVAL.md</c> (T-09). Một thay đổi prompt không kèm số đo trước–sau là một
    /// thay đổi không ai biết tốt hay xấu.
    ///
    /// <c>v1.1</c> — thêm phần định nghĩa <c>fieldConfidence</c>. Không có nó thì mô hình gần
    /// như chắc chắn trả 1.0 cho mọi trường, <c>min(mô hình, định dạng)</c> luôn ≥ 0,9, và màn
    /// hình xác nhận không bao giờ tô vàng chỗ nào — mất lưới cảnh báo mà mất im lặng.
    /// </summary>
    public const string Version = "v1.1";

    /// <summary>
    /// Sáu điều theo đúng thứ tự ưu tiên của SPEC mục 4.5, cộng hai ví dụ. Hai ví dụ dùng nhân
    /// vật **không có trong bộ mẫu** — nhét một tấm thẻ của bộ đo vào prompt là dạy mô hình đáp
    /// án rồi tự đo lại chính mình.
    /// </summary>
    public const string ExtractCard = """
        Bạn đọc ảnh một tấm danh thiếp và trả về đúng cấu trúc JSON đã được chỉ định.
        Không viết lời dẫn, không dùng markdown, không bọc trong dấu nháy ba.

        BỐN LUẬT, THEO ĐÚNG THỨ TỰ ƯU TIÊN

        1. CHỈ CHÉP LẠI NHỮNG GÌ NHÌN THẤY TRÊN ẢNH.
           Không suy luận, không bổ sung kiến thức bên ngoài.
           - Không đoán email từ tên miền website. Thẻ không in email thì emails là mảng rỗng.
           - Không đoán tên công ty từ tên miền của email.
           - Không thêm mã quốc gia cho số điện thoại không in mã quốc gia.
             "03-5550-1284" giữ nguyên "03-5550-1284", không thành "+81-3-5550-1284".
           - Số điện thoại chép lại ĐÚNG NHƯ IN, giữ nguyên dấu cách, dấu chấm, gạch nối và ngoặc.

        2. KHÔNG ĐỌC ĐƯỢC THÌ ĐỂ CHUỖI RỖNG và đặt confidence của trường đó bằng 0.
           Thà thiếu còn hơn sai. Không bao giờ điền giá trị suy đoán cho đủ cấu trúc.

        3. GIỮ NGUYÊN CHỮ GỐC.
           Thẻ Nhật ghi 株式会社 thì giữ nguyên 株式会社: không phiên âm, không dịch, không đổi sang chữ Latin.
           Tên người, tên công ty, chức danh, địa chỉ đều giữ nguyên chữ như in trên thẻ.

        4. KHÔNG PHẢI DANH THIẾP thì đặt isBusinessCard = false, viết rejectReason một câu,
           và ĐỂ TRỐNG TOÀN BỘ các trường còn lại. Đã nói không đọc được thì không được
           đồng thời đưa ra dữ liệu.

        searchAlias — CHỈ dùng cho thẻ có chữ Nhật
           Ghi phiên âm Latin của TÊN NGƯỜI, TÊN CÔNG TY, và TỈNH/THÀNH + QUẬN
           vào MỘT chuỗi duy nhất, cách nhau bằng khoảng trắng.
           - KHÔNG DỊCH CHỨC DANH. 営業部長 không được thành "Sales Manager".
             Chức danh đã có trường jobTitle riêng, tìm thẳng trên đó.
           - Địa chỉ chỉ lấy tỉnh/thành và quận. Bỏ số nhà, tên toà nhà, mã bưu chính.
           - Thẻ không có chữ Nhật thì để chuỗi rỗng.

        fieldConfidence — CHẤM ĐIỂM TIN CẬY CHO TÁM TRƯỜNG
           fullName · jobTitle · company · phones · emails · website · address · searchAlias

           Thang 0–1. Chấm theo mức ĐỌC ĐƯỢC RÕ TỚI ĐÂU, không phải mức bạn tin nội dung là đúng:
           - 1.0       chữ sắc nét, đọc rõ ràng, không phải đoán ký tự nào
           - 0.5–0.8   chữ mờ, nhoè, bị che, loá, tương phản thấp, hoặc không chắc vài ký tự
           - 0         trường rỗng

           ĐỪNG ĐẶT 1.0 CHO MỌI TRƯỜNG THEO PHẢN XẠ.
           Điểm này quyết định trường nào được tô lên cho người kiểm lại trước khi lưu.
           Chấm 1.0 tất tay nghĩa là người dùng không còn được cảnh báo chỗ nào — kể cả chỗ
           bạn thật sự đã phải đoán.

        detectedLanguage: "en" · "ja" · "mixed" khi thẻ in cả hai thứ tiếng.

        VÍ DỤ 1 — thẻ tiếng Anh, hai số điện thoại

        Trên thẻ:
           Helena Ward — Regional Sales Lead — Brightmoor Textiles
           +1 555 0163 220 / (415) 555 0188
           h.ward@brightmoor-textiles.example — brightmoor-textiles.example
           220 Larkin Street, San Francisco CA 94102

        Trả về:
           fullName "Helena Ward" · jobTitle "Regional Sales Lead" · company "Brightmoor Textiles"
           phones ["+1 555 0163 220", "(415) 555 0188"]   (hai phần tử, chép đúng như in)
           emails ["h.ward@brightmoor-textiles.example"]
           website "brightmoor-textiles.example"
           address "220 Larkin Street, San Francisco CA 94102"
           detectedLanguage "en" · searchAlias ""   (thẻ không có chữ Nhật)
           fieldConfidence: fullName 1.0 · jobTitle 1.0 · company 1.0 · phones 1.0 ·
                            emails 1.0 · website 1.0 · address 1.0 · searchAlias 0
                            (searchAlias bằng 0 vì nó rỗng, không phải vì đọc không ra)

        VÍ DỤ 2 — thẻ tiếng Nhật đầy đủ

        Trên thẻ:
           有限会社葛西電装 — 技術部 主任 — 小林 誠
           〒103-0027 東京都中央区日本橋2-4-9   (dòng này in chữ nhỏ, hơi mờ)
           03-5550-7741 — m.kobayashi@kasai-densou.example

        Trả về:
           fullName "小林 誠" · jobTitle "技術部 主任" · company "有限会社葛西電装"
              (cả ba giữ nguyên chữ Nhật)
           phones ["03-5550-7741"]   (không thêm +81)
           emails ["m.kobayashi@kasai-densou.example"] · website ""
           address "〒103-0027 東京都中央区日本橋2-4-9"
           detectedLanguage "ja"
           searchAlias "Kobayashi Makoto Kasai Densou Tokyo Chuo"
              (tên người + tên công ty + tỉnh/thành và quận.
               KHÔNG có "Engineering Supervisor" — chức danh không được dịch.
               KHÔNG có "Nihonbashi 2-4-9" — số nhà không thuộc searchAlias.)
           fieldConfidence: fullName 1.0 · jobTitle 1.0 · company 1.0 · phones 1.0 ·
                            emails 1.0 · website 0 · address 0.7 · searchAlias 1.0
                            (address 0.7 vì dòng 〒103-0027 in nhỏ và mờ — đọc được nhưng
                             phải căng mắt. website 0 vì thẻ không in website.)
        """;
}
