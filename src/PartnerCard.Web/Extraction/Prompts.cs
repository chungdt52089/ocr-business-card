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
    ///
    /// <c>v1.2</c> — thêm luật thẻ song ngữ. **Đây là vá lỗ đặc tả, không phải vòng tinh chỉnh**
    /// (xem <see cref="VersionNote"/>).
    ///
    /// <c>v1.3</c> — địa danh ba cấp trong <c>searchAlias</c>. Cũng là vá lỗ đặc tả: luật cũ viết
    /// "tỉnh/thành và quận", đúng với địa chỉ **hai** cấp (<c>東京都渋谷区</c>) nhưng không nói gì
    /// về địa chỉ **ba** cấp (<c>兵庫県神戸市中央区</c>), nên mô hình lấy hai cấp đầu rồi dừng và
    /// đánh rơi tên quận. Hai lượt đo độc lập cùng sai đúng một kiểu ở đúng một tấm — dấu hiệu của
    /// một luật thiếu, không phải của một lần đọc trượt.
    ///
    /// <c>v1.4</c> — hậu tố loại hình công ty trong <c>searchAlias</c> của thẻ song ngữ. **Vòng
    /// tinh chỉnh đầu tiên của T-09**, mốc so sánh là số của <c>v1.3</c>. Đặc tả vốn đã đúng ("chép
    /// đúng như in"), chỉ prompt diễn đạt chưa tới: <c>bi-01</c>, <c>bi-02</c> giữ <c>K.K.</c> dưới
    /// <c>v1.2</c> nhưng rơi mất ở 5/5 lượt dưới <c>v1.3</c>, khi khối luật địa danh chèn vào lấy
    /// mất chỗ dựa của phần tên công ty. Sửa bằng một câu gắn vào luật song ngữ sẵn có, không thêm
    /// khối nhấn mạnh — chính khối nhấn mạnh của <c>v1.3</c> gây ra lỗi này.
    /// </summary>
    public const string Version = "v1.4";

    /// <summary>
    /// Vì sao phiên bản này khác phiên bản trước — in cạnh <c>promptVersion</c> trong
    /// <c>EVAL.md</c> (SPEC mục 14).
    ///
    /// **Phân biệt hai loại thay đổi prompt, và đó là mục đích duy nhất của chuỗi này.** T-09 là
    /// vòng *tinh chỉnh*: thử một cách diễn đạt khác để xem điểm có lên không. Còn v1.1 → v1.2 là
    /// *vá lỗ đặc tả*: prompt trước **không hề nói** thẻ in hai hệ chữ thì lấy bản nào, nên mô
    /// hình ghép cả hai — nó làm đúng thứ nó được bảo. Điểm `bi-01` và `bi-02` tăng lên là vì cái
    /// lỗ được vá, không phải vì mô hình đọc tốt hơn. Trộn hai loại vào một bảng số thì T-09 sẽ
    /// tưởng mình vừa tìm ra một cách diễn đạt hiệu quả, và đi tối ưu tiếp theo hướng đó.
    /// </summary>
    public const string VersionNote =
        "VÒNG TINH CHỈNH đầu tiên của T-09 (giữ hậu tố loại hình công ty trong searchAlias thẻ song ngữ), "
        + "mốc so sánh v1.3 — đặc tả vốn đúng, KHÔNG phải vá lỗ đặc tả";

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

        THẺ IN CẢ HAI HỆ CHỮ — CHỌN MỘT, KHÔNG GHÉP
           Nhiều thẻ Nhật in cùng một thông tin hai lần: một bản chữ Nhật, một bản Latin.
           - fullName, company, jobTitle: lấy BẢN CHỮ NHẬT, bỏ bản Latin.
             Thẻ in "森下 涼子 / Ryoko Morishita" → fullName = "森下 涼子"
             KHÔNG ghép thành "森下 涼子 Ryoko Morishita".
           - Bản Latin in trên thẻ đi vào searchAlias, CHÉP ĐÚNG NHƯ IN:
             "Ryoko Morishita", "Minatoya Instruments K.K."
             Không đảo thứ tự tên, không tự phiên âm lại thành "Morishita Ryoko"
             hay "Minatoya Keiki".
             CHÉP NGUYÊN CHUỖI, kể cả hậu tố loại hình công ty (K.K., Co., Ltd., Inc.).
           - Chỉ tự phiên âm khi thẻ KHÔNG in sẵn bản Latin.
             Tự phiên âm thì chỉ phiên âm danh từ riêng: hậu tố loại hình là danh từ chung,
             không đưa vào searchAlias (ví dụ 2 bỏ 有限会社). Chép bản in sẵn thì chép cả chuỗi.

        searchAlias — CHỈ dùng cho thẻ có chữ Nhật
           Ghi phiên âm Latin của TÊN NGƯỜI, TÊN CÔNG TY, và ĐỊA DANH HÀNH CHÍNH
           vào MỘT chuỗi duy nhất, cách nhau bằng khoảng trắng.
           - KHÔNG DỊCH CHỨC DANH. 営業部長 không được thành "Sales Manager".
             Chức danh đã có trường jobTitle riêng, tìm thẳng trên đó.
           - ĐỊA DANH: lấy ĐỦ MỌI CẤP HÀNH CHÍNH từ tỉnh/thành xuống TỚI QUẬN.
             Địa chỉ Nhật có khi hai cấp, có khi ba. Có ba thì lấy CẢ BA —
             đừng dừng lại ở cấp thứ hai.
                東京都渋谷区神南1-2-3       → "Tokyo Shibuya"
                                             (2 cấp: 都 + 区)
                静岡県浜松市中区板屋町4-5-6 → "Shizuoka Hamamatsu Naka"
                                             (3 cấp: 県 + 市 + 区 — thiếu "Naka" là SAI)
             Bỏ từ cấp phường (町, 丁目) trở xuống, cùng số nhà, tên toà nhà, mã bưu chính.
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
           〒111-0051 東京都台東区蔵前3-8-1   (dòng này in chữ nhỏ, hơi mờ)
           03-5550-7741 — m.kobayashi@kasai-densou.example

        Trả về:
           fullName "小林 誠" · jobTitle "技術部 主任" · company "有限会社葛西電装"
              (cả ba giữ nguyên chữ Nhật)
           phones ["03-5550-7741"]   (không thêm +81)
           emails ["m.kobayashi@kasai-densou.example"] · website ""
           address "〒111-0051 東京都台東区蔵前3-8-1"
           detectedLanguage "ja"
           searchAlias "Kobayashi Makoto Kasai Densou Tokyo Taito"
              (tên người + tên công ty + địa danh. Địa chỉ này có 2 cấp: 都 + 区.
               KHÔNG có "Engineering Supervisor" — chức danh không được dịch.
               KHÔNG có "Kuramae 3-8-1" — cấp phường và số nhà không thuộc searchAlias.)
           fieldConfidence: fullName 1.0 · jobTitle 1.0 · company 1.0 · phones 1.0 ·
                            emails 1.0 · website 0 · address 0.7 · searchAlias 1.0
                            (address 0.7 vì dòng 〒111-0051 in nhỏ và mờ — đọc được nhưng
                             phải căng mắt. website 0 vì thẻ không in website.)
        """;
}
