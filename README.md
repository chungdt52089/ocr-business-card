# PartnerCard (Scanie)

**Chụp danh thiếp → Gemini trích xuất → người xác nhận → hồ sơ đối tác chuẩn hoá.**

Cán bộ đi sự kiện mở trang web trên điện thoại, bấm **Chụp**, và vài giây sau có một bản nháp hồ sơ
với 8 trường: họ tên, chức danh, công ty, điện thoại, email, website, địa chỉ, gợi ý tìm kiếm bằng chữ
Latin. Ảnh được **thu nhỏ ngay trong trình duyệt**, gửi tới **Gemini** qua một đường ống cố định, rồi
đi qua **`SchemaGuard`** — lưới chặn mọi giá trị mô hình bịa ra — trước khi hiện lên màn hình xác nhận.

Hệ thống **không tự lưu**: bản nháp chỉ thành hồ sơ khi người bấm **Lưu**, sau khi đã xem lại và sửa
những trường được tô vàng. Trùng email thì cảnh báo và để người chọn, không tự gộp.

Cùng đường ống đó được mở cho AI agent qua **4 tool MCP** tại `/mcp`. Giao diện Blazor và tool MCP gọi
**đúng một class `CardPipeline`** — không có đường đi riêng cho giao diện, nên mọi bảo đảm của đường ống
áp cho cả hai.

**Thư mục này chỉ chứa những gì cần để chạy.** Đặc tả, backlog, kịch bản demo và tài liệu thiết kế nằm
ngoài repo, tại `..\PartnerCard\docs\` trên máy phát triển (xem §14). Deploy lên Google Cloud Run: xem
[`DEPLOY.md`](DEPLOY.md).

---

## 1. Bối cảnh — danh thiếp nằm trong ngăn kéo

Mỗi hội thảo, triển lãm hay sự kiện kết nối mang về **hàng chục đến hàng trăm tấm danh thiếp**. Sau sự
kiện, phần lớn số đó nằm yên trong ngăn kéo:

| Mã | Vấn đề | Ảnh hưởng |
| --- | --- | --- |
| P-1 | Nhập tay tốn thời gian, dễ sai, thường bị trì hoãn vô thời hạn | Liên hệ thu được không biến thành tài sản dùng được |
| P-2 | Danh thiếp chỉ có thông tin cơ bản; muốn biết lĩnh vực, sản phẩm phải tự tra | Mỗi liên hệ tốn thêm 10–15 phút |
| P-3 | Dữ liệu rải rác ở bảng tính cá nhân, ảnh trong điện thoại, card giấy | Không chia sẻ, không tái sử dụng, trùng lặp |
| P-4 | Danh thiếp đa ngôn ngữ (Anh, Hàn, Nhật, Trung) | Người nhập không đọc được, bỏ qua hoặc nhập sai |

Gốc rễ không nằm ở chỗ thiếu nơi lưu — mà ở chỗ **khâu nhập liệu quá đắt** so với giá trị tức thời của
một tấm thẻ, nên nó luôn bị đẩy sang "để sau". Hướng giải quyết là làm khâu đó rẻ tới mức làm được
**ngay tại sự kiện**: chụp một tấm, sửa vài chỗ, bấm Lưu — không gõ lại từ đầu.

Mô hình ngôn ngữ đọc danh thiếp khá tốt, nhưng có một thói quen nguy hiểm: **điền bừa cho đủ schema**.
Một email đoán từ tên công ty trông y hệt email thật. Vì vậy bài toán thật của dự án không phải "gọi
Gemini", mà là **làm sao tin được kết quả Gemini trả về** — và để con người quyết định ở chỗ máy không
chắc.

---

## 2. Dự án này demo phần nào của bài toán

Đây là MVP cho đề bài #2 của AI-OJT, **một lát cắt được thu hẹp có chủ đích**: mục tiêu tối thượng là
demo được việc quét danh thiếp trong buổi họp với mentor, không phải sản phẩm vận hành thật.

Trọng tâm là **đường ống trích xuất có kiểm soát**: ảnh đi qua một chuỗi mắt xích cố định
(`Validate → Extract → Guard → Normalize → Confidence → Audit`), mỗi mắt xích có test riêng, và độ chính
xác được **đo bằng số** trên ảnh chụp thật chứ không bằng cảm giác. MCP là lối vào thứ hai của cùng
đường ống đó, cho AI agent.

Cụ thể, với người dùng, bản demo làm được năm việc:

1. **Chụp danh thiếp bằng camera điện thoại** (hoặc chọn ảnh) và nhận bản nháp trong vài giây.
2. **Xem lại và sửa** trên màn hình xác nhận — trường điểm tin cậy thấp được tô lên.
3. **Lưu có chống trùng** theo email.
4. **Tìm lại** theo tên, công ty, email, chức danh, địa chỉ — thẻ tiếng Nhật tìm được bằng chữ Latin.
5. **Xuất CSV** mở thẳng bằng Excel, không vỡ chữ Nhật, không mất số 0 đầu.

Ranh giới in-scope/out-of-scope ở §3; những gì cố tình chưa làm ở §13.

---

## 3. Dự án này chứng minh điều gì

| # | Điều được chứng minh | Cách chứng minh trong code |
| --- | --- | --- |
| 1 | **Trích xuất đủ chính xác để chỉ phải sửa, không phải gõ lại** | Bộ đo `tools/eval` chạy trên **ảnh chụp lại từ bản in**: **72/72** trường bắt buộc đúng ở các lượt đo độc lập với `gemini-3.5-flash-lite`, p95 ≈ 3,6 s (ngưỡng BRD: ≥ 90% tiếng Anh, ≥ 80% tiếng Nhật, p95 ≤ 10 s). Kết quả thô trong [`EVAL.md`](EVAL.md) |
| 2 | **Không bịa** | Prompt "không chắc thì để rỗng" + `response_schema` + `SchemaGuard` 9 luật trên JSON thô. Ca riêng `ja-01-partial` (thẻ bị cắt mất khối liên hệ) phải trả `emails` và `website` **rỗng** |
| 3 | **Một đường ống cho cả giao diện lẫn MCP** | Trang Blazor và 4 tool MCP cùng gọi `CardPipeline`. Tool không nhận `IExtractor` hay `IPartnerStore` nên không có cách đi tắt qua Validate, Guard hay chống trùng |
| 4 | **Human in the loop thật** | Không tự lưu; trường < 0,9 tô vàng; ghi `editedFields` vào hồ sơ; metric `partnercard.human.edits`; trùng email thì hỏi, không tự gộp |
| 5 | **Thay mô hình là việc của một class** | Mọi lời gọi Gemini nằm trong `GeminiExtractor` sau interface `IExtractor`; `FakeExtractor` cho toàn bộ test chạy **khi đã ngắt mạng** |
| 6 | **Quan sát được** | Nhật ký audit JSONL mỗi lượt (không nội dung thẻ); trace + metric OpenTelemetry đổ về Aspire Dashboard; CI chạy build, test và quét khoá API trên mỗi PR |

### Phạm vi

| In-scope | Out-of-scope |
| --- | --- |
| Chụp bằng camera điện thoại qua trình duyệt (`input capture`), hoặc chọn ảnh | Ứng dụng di động riêng; chụp nhiều thẻ trong một khung hình |
| Trích xuất 8 trường — **đo và cam kết trên tiếng Anh và tiếng Nhật** | Danh thiếp tiếng Hàn, tiếng Trung (mô hình không giới hạn ngôn ngữ, nhưng **chưa có bộ mẫu và số đo**) |
| Màn hình xác nhận, sửa trước khi lưu | Tự động hoàn toàn không cần người xem |
| Lưu tập trung, tìm kiếm, chống trùng theo email, xuất CSV | Xoá hồ sơ, gộp hồ sơ trùng |
| 4 tool MCP: `extract_business_card`, `save_partner`, `search_partners`, `enrich_partner` | Tool MCP xoá, sửa trực tiếp, xuất file |
| `enrich_partner` giữ chỗ, trả `not_implemented` | Bổ sung thông tin công ty từ website/Internet (**cắt 08/09**) |
| Lưu bằng **file JSON** | Database |
| Deploy công khai lên **Google Cloud Run** (HTTPS), 1 instance | Lưu trữ bền trên môi trường deploy — instance mới là kho về 3 hồ sơ mồi |
| Chạy một người dùng, không đăng nhập | Đăng nhập, phân quyền, nhiều người dùng |
| Trace + metric OpenTelemetry → Aspire Dashboard **cục bộ** | Telemetry từ bản deploy trên Cloud Run |

Toàn bộ danh thiếp trong dự án là **hư cấu 100%** — nhân vật và công ty tự dựng, tên miền đuôi
`.example`. Mọi trang đều mang dải cảnh báo *"Bản thử nghiệm — chỉ dùng danh thiếp mẫu, không tải danh
thiếp thật lên"*.

---

## 4. Công nghệ sử dụng

| Lớp | Lựa chọn | Ghi chú |
| --- | --- | --- |
| Runtime / backend | **.NET 10** (`net10.0`), ASP.NET Core | SDK ghim `10.0.400` qua `global.json`, `rollForward: latestPatch` |
| Giao diện | **Blazor Server** (Interactive Server) + **vanilla JavaScript** + CSS thuần | `capture.js` thu nhỏ ảnh, `reconnect.js` hộp nối lại tiếng Việt. Không React/Vue, không bundler, không npm |
| Mô hình trích xuất | **`gemini-3.5-flash-lite`**, `thinkingLevel = low` | Đổi từ `gemini-3.8-flash` ngày 11/09 sau khi đo: cùng 72/72, hạn mức rộng hơn hẳn (§11) |
| Gọi Gemini | **`HttpClient` → REST `v1beta/models/{model}:generateContent`** | Không dùng SDK. `response_schema` **sinh từ `CardSchema`** — cùng nguồn với guard |
| Prompt | Hằng `Prompts.ExtractCard`, phiên bản **`v1.4`** | `promptVersion` ghi vào từng hồ sơ và từng lượt đo |
| Giao thức tool | **MCP** — `ModelContextProtocol.AspNetCore` **2.2.0** | Streamable HTTP tại `/mcp`, Stateless; header `X-Session-Id` để nối nhật ký |
| Lưu trữ | **File JSON** — `data/partners.json`, `data/counter.json` | Ảnh gốc ở `data/images/<sha256>.jpg`. Không RDBMS/NoSQL |
| Nhật ký | **JSONL** — `logs/audit-yyyyMMdd.jsonl` | Một dòng mỗi lượt; không tên, không email, không số điện thoại |
| Quan sát | **OpenTelemetry** 1.19.x, OTLP → **Aspire Dashboard** (Docker) | Chỉ bật khi có `OTEL_EXPORTER_OTLP_ENDPOINT` |
| Kiểm thử | **xUnit 2.9.3** + FluentAssertions 7.2.0 | Bộ mặc định chạy **offline hoàn toàn**; ca `Live` gọi Gemini thật là opt-in |
| CI | **GitHub Actions** (`windows-latest`) | Quét khoá `AIza…`, build Release, test trừ `Category=Live` |
| Deploy | **Google Cloud Run** — image `aspnet:10.0` chép bản publish dựng sẵn | Build bằng Cloud Build, khoá lấy từ Secret Manager. Chi tiết: `DEPLOY.md` |

---

## 5. Kiến trúc

**Một tiến trình duy nhất.** Giao diện, MCP Server và đường ống cùng nằm trong `PartnerCard.Web`; bên
ngoài chỉ có Gemini API và (tuỳ chọn) Aspire Dashboard.

```text
   Điện thoại / trình duyệt                          AI agent · MCP Inspector
   http://<host>:5080                                POST /mcp   (+ X-Session-Id)
              │ SignalR (Blazor Server)                    │ Streamable HTTP, Stateless
              ▼                                            ▼
 ┌──────────────────────────────────────────────────────────────────────────────────────┐
 │  PartnerCard.Web  — ASP.NET Core .NET 10, MỘT tiến trình                             │
 │                                                                                      │
 │   Trang Blazor                                  4 tool MCP                           │
 │   /  ·  /partners  ·  /review/{id}              CardTools · PartnerTools             │
 │            │                                          │                              │
 │            └────────────────────┬─────────────────────┘                              │
 │                                 ▼                                                    │
 │   CardPipeline — đường DUY NHẤT để trích xuất và ghi, hai nhánh                      │
 │   Trích xuất: Validate → Extract → Guard → Normalize → Confidence → Audit            │
 │   Lưu:        Normalize → Confidence → Guard → chống trùng → Persist → Audit         │
 │                 │                                            │                       │
 │         ┌───────┴────────┐                    ┌──────────────┴──────────────┐        │
 │   FakeExtractor   GeminiExtractor       IPartnerStore   ImageStore    IAuditLogger   │
 └──────────────────────────┬────────────────────┬──────────────┬──────────────┬────────┘
                            │ HTTPS              ▼              ▼              ▼
                     ┌──────────────┐      data/            data/images/    logs/
                     │  Gemini API  │      partners.json    <sha256>.jpg    audit-*.jsonl
                     └──────────────┘      counter.json

   Tuỳ chọn: OTLP ──▶ Aspire Dashboard :18888 (chỉ khi có OTEL_EXPORTER_OTLP_ENDPOINT)
```

### Ai sở hữu cái gì

| Thành phần | Sở hữu | Không bao giờ làm |
| --- | --- | --- |
| `Components/Pages` (Blazor) | Chụp, màn xác nhận, lịch sử, ghi ảnh gốc vào `ImageStore` | Gọi `IExtractor` trực tiếp, ghi audit, ghi kho không qua đường ống |
| `Tools/` (MCP) | 4 tool, bọc kết quả thành envelope có `errorCode` | Nhận `IExtractor`/`IPartnerStore` để đi tắt; ghi audit lần hai |
| `Processing/CardPipeline` | Thứ tự mắt xích, chống trùng, bắt mọi exception thành kết quả có cấu trúc | Để exception thoát ra ngoài; lưu bản trước guard |
| `Extraction/GeminiExtractor` | **Mọi** lời gọi Gemini, timeout, thử lại 5xx, phân loại 429 | Nằm rải `HttpClient` ở chỗ khác |
| `Processing/SchemaGuard` | 9 luật SG-0…SG-8 trên chuỗi JSON | Deserialize trước khi soi (khoá lạ sẽ bị nuốt im lặng) |
| `Storage/` | Kho JSON, cấp mã `PTN0001` tăng dần, ảnh theo SHA-256, endpoint ảnh và CSV | Phục vụ `data/` như file tĩnh |
| `Audit/` | Nhật ký JSONL, mã phiên nối nhật ký | Ghi nội dung danh thiếp |

### Ranh giới tin cậy

- **Output của mô hình là dữ liệu không tin cậy.** Nó đi qua `SchemaGuard` dưới dạng **chuỗi JSON thô**,
  trước khi deserialize — deserialize vào record C# là khoá lạ bị nuốt im lặng.
- **Chữ trên danh thiếp là dữ liệu người ngoài đưa vào.** CSV thêm `'` trước ô bắt đầu bằng ký tự công
  thức (OWASP CSV injection): một tấm thẻ in `=HYPERLINK(…)` không được thành công thức sống.
- **Endpoint ảnh chỉ nhận đúng dạng tên nó ghi ra** (64 ký tự hex + `.jpg`). Sai dạng hay không có file
  đều `404` như nhau — không nói cho người dò đường dẫn biết mình trượt ở bước nào.
- Mọi lỗi trả về đều có cấu trúc; không stack trace, đường dẫn file hay tên khoá cấu hình nào lộ ra
  giao diện hoặc kết quả tool.
- `X-Session-Id` là **mã tương quan, không phải danh tính** — không dùng để phân quyền.

---

## 6. Luồng làm việc của ứng dụng

### 6.1 Một lần quét đi qua đâu

Đường ống có **hai nhánh**, không phải một chuỗi thẳng:

```text
NHÁNH TRÍCH XUẤT  —  CardPipeline.ExtractAsync   (không lưu gì)
   │
   0. Trình duyệt: capture.js thu nhỏ cạnh dài ≤ 1600 px, JPEG q0.8 — TRƯỚC khi gửi
   │     (ảnh 12 MP không bao giờ đi qua SignalR; trần tin nhắn hub là 2 MB base64)
   │
   1. Validate — chạy TRƯỚC khi tốn một request hạn mức
   │     • mime ∉ {jpeg, png, webp, heic, heif}          → unsupported_mime
   │     • base64 rỗng / sai / có tiền tố data URI       → invalid_base64
   │     • > 8 MB                                        → image_too_large
   │
   2. Extract — IExtractor
   │     fake   : trả đáp án trong expected.json, tra theo mã thẻ lấy từ TÊN FILE
   │     gemini : response_schema, thinkingLevel low, timeout 20 s
   │              5xx → thử lại đúng 1 lần sau 2 s · 429 → KHÔNG thử lại
   │
   3. SchemaGuard trên JSON THÔ, trước deserialize (§6.5)
   │     chặn → guard_blocked, không thử lại
   │     dọn  → xoá giá trị hỏng về rỗng + ghi warnings
   │
   4. Normalize — điện thoại bỏ khoảng trắng . - ( ); email chữ thường;
   │     website thêm https://, bỏ www. và / cuối; địa chỉ gộp khoảng trắng
   │     số không có mã quốc gia → ghi chú unnormalizedPhone (KHÔNG trừ điểm)
   │
   5. Confidence — min(điểm mô hình tự báo, điểm kiểm định dạng)
   │     trường < 0,9 → reviewFields → tô vàng trên màn xác nhận
   │
   6. Audit — một dòng JSONL: số trường, điểm TB, số cảnh báo, độ trễ, token, model, promptVersion
   │
   ▼  Bản nháp → màn hình xác nhận. Chưa có gì được lưu.

NHÁNH LƯU  —  CardPipeline.SaveAsync   (không gọi mô hình)
   │
   1. Normalize → Confidence   (các trường giờ do người sửa)
   2. SchemaGuard trên DTO đã serialize — bỏ SG-1, SG-2; vẫn dọn SG-4…SG-8
   │     → LƯU BẢN ĐÃ DỌN, không phải bản trước guard
   3. Chống trùng: giao nhau ≥ 1 email (không phân biệt hoa thường) → duplicate + duplicateOf
   │     người chọn "vẫn tạo mới" → allowDuplicate = true. KHÔNG tự gộp.
   4. Persist — mã mới PTN0001, PTN0002…; mã đã cấp không bao giờ cấp lại
   5. Audit — thêm partnerId, imageSha256
   ▼  Hồ sơ status = confirmed
```

**Vì sao không tự lưu sau khi trích xuất:** người dùng luôn xem lại trước khi lưu là giả định nền của
sản phẩm. Hệ thống trợ giúp, không tự động hoàn toàn — và `editedFields` của mỗi hồ sơ là nguyên liệu để
biết prompt sai ở đâu.

### 6.2 Kịch bản người dùng

| Người dùng làm | Đi qua | Kết quả trên giao diện |
| --- | --- | --- |
| Chụp một danh thiếp tiếng Nhật (`ja-01`) | Nhánh trích xuất | Bản nháp: tên, công ty giữ nguyên chữ Nhật; `searchAlias` phiên âm Latin |
| Chụp tấm bị cắt mất khối liên hệ (`ja-01-partial`) | Nhánh trích xuất | `emails`, `website` **rỗng** — mô hình không được suy ra thứ không nhìn thấy |
| Sửa một trường rồi bấm **Lưu** | Nhánh lưu | Thông báo đã lưu `PTN000x`; trường đã sửa ghi vào `editedFields` |
| Lưu thẻ có email trùng hồ sơ cũ | Nhánh lưu → chống trùng | Cảnh báo *"Email này đã có trong hồ sơ PTN000x"* + nút xem hồ sơ đó / vẫn tạo mới |
| Chụp một ảnh không phải danh thiếp (`neg-01`) | Nhánh trích xuất | `isBusinessCard = false` kèm lý do; không có trường nào mang giá trị (SG-7) |
| Gõ `tanaka` ở trang **Lịch sử** | Đọc kho | Ra thẻ tiếng Nhật của 田中 nhờ `searchAlias`; gõ không dấu vẫn khớp chữ có dấu |
| Bấm **Xuất CSV** | `GET /export/partners.csv` | File chỉ gồm hồ sơ `confirmed`, mở thẳng bằng Excel |
| Gọi `enrich_partner` qua MCP Inspector | Tool giữ chỗ | `errorCode = not_implemented` |

### 6.3 Bốn MCP tool

| Tool | Tham số | Đi qua đường ống | Ghi kho |
| --- | --- | --- | --- |
| `extract_business_card` | `imageBase64`, `mimeType`, `languageHint?` (`vi`/`en`/`ko`/`ja`/`zh`), `sourceName?` | Nhánh trích xuất | Không |
| `save_partner` | `fullName`, `jobTitle`, `company`, `phones[]`, `emails[]`, `website`, `address`, `detectedLanguage`, `searchAlias`, `partnerId?`, `fieldConfidence?`, `editedFields?`, `allowDuplicate?` | Nhánh lưu | Có |
| `search_partners` | `keyword?`, `company?`, `take` (mặc định 20, tối đa 100) | Không — chỉ đọc | Không |
| `enrich_partner` | `website?`, `companyName?` — không dùng | Không — `static`, không phụ thuộc gì | Không |

> Các tham số chuỗi của `save_partner` **không có mặc định**: trường không có thì gửi `""` hoặc `[]`
> tường minh — nhớ điều này khi gọi qua MCP Inspector.

Mọi tool trả về `ok`, `errorCode`, `message` — thông báo trung tính nhưng **không giấu nguyên nhân**:
`quota_exhausted` khác `extract_timeout`, và cả hai khác một ảnh không phải danh thiếp (cái đó là
`ok = true` với `isBusinessCard = false`).

```jsonc
// extract_business_card
{
  "ok": true,
  "card": { "isBusinessCard": true, "fullName": "…", "phones": ["…"], "fieldConfidence": { … } },
  "reviewFields": ["jobTitle"],              // trường dưới ngưỡng 0,9
  "warnings": [{ "code": "SG-4", "field": "emails", "message": "…" }],
  "normalizationWarnings": ["unnormalizedPhone"],
  "errorCode": null,
  "message": null
}
```

Tầng tool **không ghi audit** — đường ống đã ghi, ghi thêm ở đây là mỗi lời gọi ra hai dòng.
`search_partners` cũng không ghi: từ khoá thường chính là tên người.

Không có tool xoá, sửa trực tiếp hay xuất file — theo thiết kế, không phải thiếu sót.

### 6.4 Mã lỗi

| Mã lỗi | Từ đâu | Nghĩa |
| --- | --- | --- |
| `unsupported_mime` | Validate | Định dạng ảnh không được nhận |
| `invalid_base64` | Validate | Nội dung ảnh rỗng hoặc không phải base64 thuần |
| `image_too_large` | Validate | Ảnh vượt trần 8 MB |
| `quota_exhausted` | Gemini `429` — trần **ngày** | Hết hạn mức hôm nay. Không thử lại |
| `rate_limited` | Gemini `429` — trần **phút** | Gọi nhanh quá, thử lại sau ít giây |
| `extract_timeout` | Gemini | Quá `ExtractTimeoutSeconds` (mặc định 20 s) |
| `extract_unavailable` | Gemini `5xx` sau lần thử lại | Dịch vụ quá tải |
| `extractor_auth` | Gemini `401`/`403`/khoá sai | Khoá API không dùng được — chụp lại cũng vô ích |
| `extract_failed` | Mọi lỗi khác của nhánh trích xuất | Thông báo trung tính, chi tiết ở lại server |
| `guard_blocked` | SchemaGuard | Kết quả bị chặn (SG-0/2/3/7) |
| `duplicate` | Chống trùng | Đã có hồ sơ dùng chung email; kèm `duplicateOf` |
| `save_failed` | Nhánh lưu | Không lưu được hồ sơ |
| `search_failed` | `search_partners` | Không đọc được kho |
| `not_implemented` | `enrich_partner` | Luôn luôn |

`quota_exhausted` và `rate_limited` về **cùng mã HTTP 429** nhưng xử lý ngược nhau (đợi vài giây vs.
đợi tới hôm sau), nên `GeminiExtractor` đọc thân phản hồi để tách — chỗ duy nhất còn nhìn thấy nó.

### 6.5 Chống bịa — `SchemaGuard`

Ba lớp, từ ngoài vào trong:

1. **Prompt** — sáu điều theo thứ tự ưu tiên, điều cao nhất là *trường không chắc thì để rỗng*. Hai ví
   dụ dùng nhân vật **không có trong bộ mẫu** (nhét thẻ của bộ đo vào prompt là dạy đáp án rồi tự đo lại).
2. **`response_schema`** — sinh từ `CardSchema`, cùng nguồn sự thật với guard, nên hai bên không lệch nhau.
3. **`SchemaGuard`** — lưới cuối, soi chuỗi JSON:

| Luật | Kiểm | Xử lý |
| --- | --- | --- |
| SG-0 | Không phải một đối tượng JSON | **Chặn** |
| SG-1 | Khoá không có trong schema | Bỏ khoá, cảnh báo *(chỉ nhánh trích xuất)* |
| SG-2 | Thiếu khoá bắt buộc | **Chặn** *(chỉ nhánh trích xuất)* |
| SG-3 | Trường có giá trị nhưng thiếu `fieldConfidence` | **Chặn** |
| SG-4 | Email sai định dạng | Xoá phần tử, cảnh báo |
| SG-5 | Số điện thoại có ký tự lạ | Xoá phần tử, cảnh báo |
| SG-6 | Dấu vết mô hình nói chuyện: `n/a`, `null`, `unknown`, `không rõ` (khớp nguyên giá trị); ` ```json `, `I cannot`, `Tôi không thể` (khớp chuỗi con) | Xoá về rỗng, cảnh báo |
| SG-7 | `isBusinessCard = false` nhưng vẫn có trường mang giá trị | **Chặn** |
| SG-8 | Dài bất thường: `fullName` > 100, `company` > 200 ký tự | Xoá về rỗng, cảnh báo |

**SG-7 là luật quan trọng nhất**: hệ thống đã nói không đọc được thì không được đồng thời đưa ra dữ
liệu. Nó biến "không bịa" từ một lời hứa thành một bảo đảm.

Chặn là chặn: không thử lại — thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế. Nhật ký chỉ ghi **mã**
luật, không bao giờ ghi giá trị bị chặn.

### 6.6 Điểm tin cậy và chuẩn hoá

Điểm mỗi trường = **min(điểm mô hình tự báo, điểm kiểm định dạng)** — lấy nhỏ hơn vì mô hình có thể rất
tự tin về một email sai cú pháp.

| Trường | Điểm định dạng |
| --- | --- |
| Chữ (tên, chức danh, công ty, website, địa chỉ, `searchAlias`) | Có giá trị = 1,0 · rỗng = 0 |
| `emails` | Khớp `x@y.z` = 1,0 · không = 0; **phần tử tệ nhất** quyết định cả mảng |
| `phones` | Sau khi bỏ phân cách còn **8–15 chữ số** = 1,0 · ngoài khoảng = 0 |

Ngưỡng tô vàng: **0,9** (`PartnerCard:ConfidenceReviewThreshold`).

Số điện thoại **giữ nguyên như in**, không tự thêm mã quốc gia: phạm vi Anh–Nhật không có luật suy đoán
nào an toàn (người Nhật vẫn có danh thiếp tiếng Anh, người Anh vẫn có số bắt đầu bằng 0). `03-5550-1284`
được đọc đúng tuyệt đối — nó thiếu `+81` vì tấm thẻ vốn không in `+81` — nên chỉ được ghi chú
`unnormalizedPhone`, **không trừ điểm**.

### 6.7 Bí mật và dữ liệu

- **Khoá API không bao giờ vào repo hay image.** Cục bộ: `appsettings.Development.json` (bị `.gitignore`
  chặn **và** `CopyToPublishDirectory="Never"` trong csproj — `.gitignore` không chặn publish). Cloud
  Run: Secret Manager.
- **Chết sớm khi cấu hình sai.** `SecretLoader` từ chối khởi động khi `Extractor = gemini` mà thiếu khoá,
  khi khoá có khoảng trắng hoặc < 20 ký tự, hoặc khi `Extractor` gõ sai (`"gemni"` im lặng rơi về `fake`
  là cái bẫy tệ nhất). Thông báo không bao giờ nhắc lại giá trị khoá.
- `SecretOptions` cố ý là `class`, không phải `record` — `ToString()` tự sinh của record in mọi thành phần.
- **CI quét chuỗi `AIza…`** trên mỗi PR và chỉ in tên file, không in chuỗi khoá (repo công khai).
- **Audit và span không mang nội dung thẻ** — chỉ số đếm, mã lỗi, cờ, tên model.

### 6.8 Quan sát

| Tín hiệu | Nội dung |
| --- | --- |
| Trace `pipeline.extract` | `validate` → `gen_ai.generate_content` (model, token, số lần thử lại) → `guard` → `normalize` → `confidence` |
| Trace `pipeline.save` | `normalize` → `confidence` → `guard` → `persist` |
| Metric `partnercard.extract.duration` | Histogram ms, nhãn `gen_ai.request.model`, `partnercard.outcome` |
| Metric `partnercard.guard.warnings` | Số cảnh báo guard theo nhánh `extract` / `save` |
| Metric `partnercard.human.edits` | Số trường người sửa trước khi Lưu — **đo human in the loop** |
| Audit `logs/audit-yyyyMMdd.jsonl` | Một dòng mỗi lượt: `sessionId`, `tool`, `level`, `fieldsFilled`, `avgConfidence`, `warnings`, `blockCode`, `latencyMs`, `tokensIn/Out`, `model`, `promptVersion`, `errorCode` |

Mỗi lần quét là **một trace riêng** — span đường ống bỏ cha kiểu Server (kết nối `/_blazor` sống suốt
phiên) để làm gốc trace mới. Request `/_blazor`, `/_framework` và file tĩnh đã lọc bỏ.

---

## 7. Cấu trúc thư mục

```text
Code/
├── README.md                      # File này
├── DEPLOY.md                      # Runbook Google Cloud Run
├── EVAL.md                        # Kết quả bộ đo — mỗi lượt nối thêm một khối
├── PartnerCard.slnx               # Solution (định dạng slnx) — 3 project
├── global.json                    # Ghim .NET SDK 10.0.400
├── .github/workflows/ci.yml       # Quét khoá + build + test trên mỗi PR
│
├── data/
│   ├── partners.seed.json         # 3 hồ sơ mồi — git theo dõi, đi cùng bản publish
│   ├── partners.json              #   kho (sinh lúc chạy, .gitignore)
│   ├── counter.json               #   bộ đếm mã PTN (sinh lúc chạy, .gitignore)
│   └── images/                    #   ảnh gốc <sha256>.jpg (sinh lúc chạy, .gitignore)
│
├── deploy/
│   └── Dockerfile                 # aspnet:10.0 + chép deploy/publish/ — không build trong cloud
│
├── src/PartnerCard.Web/           # Ứng dụng duy nhất: Blazor + MCP + đường ống   (:5080)
│   ├── Program.cs                 #   Đăng ký DI, /health, /images, /export, /mcp, OTel
│   ├── appsettings.json           #   Cấu hình mặc định — Extractor = fake
│   ├── Audit/                     #   JsonlAuditLogger, SessionContext (X-Session-Id)
│   ├── Components/                #   Pages: Capture (/), Partners (/partners), Review (/review/{id})
│   │                              #   Review/: ReviewForm dùng chung cho màn xác nhận
│   ├── Configuration/             #   PartnerCardOptions, SecretLoader
│   ├── Extraction/                #   IExtractor, GeminiExtractor, FakeExtractor,
│   │                              #   CardSchema, Prompts (v1.4), mã lỗi extractor
│   ├── Models/                    #   Partner, CardExtractionResult, DTO
│   ├── Observability/             #   Telemetry — ActivitySource + Meter "PartnerCard"
│   ├── Processing/                #   CardPipeline, ImageValidation, SchemaGuard,
│   │                              #   Normalizer, Confidence
│   ├── Storage/                   #   JsonPartnerStore, ImageStore, PartnerIdGenerator,
│   │                              #   PartnerSeed, ImageEndpoint, CsvExportEndpoint
│   ├── Tools/                     #   CardTools, PartnerTools — 4 tool MCP
│   └── wwwroot/                   #   app.css, js/capture.js, js/reconnect.js
│
├── tests/PartnerCard.Tests/       # Một test project cho toàn solution
│   ├── Audit/ Components/ Configuration/ Eval/ Extraction/
│   ├── Observability/ Processing/ Storage/ Tools/
│   ├── Fakes/                     #   Hostile/Replay/Throwing extractor, logger ghi lại…
│   └── TestData/
│       ├── cards/                 #   20 ảnh render .png + expected.json (đáp án)
│       └── realcards/             #   19 ảnh chụp lại từ bản in — bộ đo thật
│
└── tools/eval/                    # Bộ đo độ chính xác → EVAL.md   (không PackageReference nào)
```

---

## 8. Yêu cầu hệ thống

| Thành phần | Yêu cầu |
| --- | --- |
| .NET SDK | **`10.0.400`** (band `10.0.4xx`, `rollForward: latestPatch`) — kiểm bằng `dotnet --version` trong `Code\` |
| Gemini API key | **Chỉ cần ở chế độ `gemini`.** Chế độ `fake` chạy không cần khoá, không cần mạng |
| Trình duyệt | Chrome/Edge/Safari hiện hành. Điện thoại cần cùng Wi-Fi với máy chạy app |
| Port trống | `5080` (app). Tuỳ chọn: `18888` + `4317` cho Aspire Dashboard |
| Docker | **Không bắt buộc.** Chỉ để chạy Aspire Dashboard (§9C) |
| gcloud CLI | Chỉ để deploy (§9B) |
| Hệ điều hành | Windows + PowerShell là môi trường đã kiểm chứng. Mã nguồn không phụ thuộc nền tảng |

---

## 9. Cài đặt và chạy

## 9A. Chạy cục bộ

### Bước 1 — Build

```bash
dotnet build
```

### Bước 2 — Chọn chế độ

| Cấu hình | Dùng khi |
| --- | --- |
| `PartnerCard:Extractor = fake` | Mặc định khi phát triển và chạy test. Không cần mạng, không cần khoá API |
| `PartnerCard:Extractor = gemini` | Khi đo thật hoặc demo. Cần `GEMINI_API_KEY` |

Khoá API đặt trong `src/PartnerCard.Web/appsettings.Development.json` — file này nằm trong
`.gitignore`, **không bao giờ commit**:

```json
{ "GEMINI_API_KEY": "<khoá>" }
```

Đổi chế độ bằng biến môi trường `PartnerCard__Extractor=gemini`, hoặc thêm
`"PartnerCard": { "Extractor": "gemini" }` vào cùng file đó.

> `fake` tra đáp án theo **tên file** (`ja-06.jpg` → `ja-06`). Điện thoại đặt tên mọi ảnh là
> `image.jpg`, nên ở chế độ `fake` ảnh chụp thật từ điện thoại sẽ ra *"không phải danh thiếp"* — đó là
> dấu hiệu nhận biết đang chạy nhầm chế độ.

Các khoá cấu hình khác (khối `PartnerCard` trong `appsettings.json`, đè được bằng biến môi trường
`PartnerCard__<Tên>`):

| Khoá | Mặc định | Ghi chú |
| --- | --- | --- |
| `Model` | `gemini-3.5-flash-lite` | Model đã nghiệm thu |
| `DataDirectory` | `../../data` | Tương đối theo content root → trỏ đúng `Code\data\` |
| `LogsDirectory` | `../../logs` | Cùng luật giải đường dẫn với `DataDirectory` |
| `MaxImageBytes` | `8388608` (8 MB) | Dưới trần 20 MB của Gemini |
| `ExtractTimeoutSeconds` | `20` | Cloud Run đặt `45` |
| `ConfidenceReviewThreshold` | `0.9` | Dưới ngưỡng thì tô vàng |

### Bước 3 — Chạy

```bash
dotnet run --project src/PartnerCard.Web --urls http://0.0.0.0:5080
```

Dòng log đầu tiên cho biết có nạp mồi hay không và hai thư mục dữ liệu/nhật ký nằm ở đâu sau khi giải
đường dẫn. Kho rỗng thì app tự chép `partners.seed.json` vào — **3 hồ sơ mồi**.

### Bước 4 — Kiểm tra

Mở `http://localhost:5080`, hoặc `http://<IP-LAN>:5080` từ điện thoại (cùng Wi-Fi, cổng 5080 đã mở trên
tường lửa). Cách rẻ nhất để biết bind, tường lửa và Wi-Fi đều đúng là mở `/health` từ điện thoại:

```text
http://<IP-LAN>:5080/health   →   {"status":"ok"}
```

`/health` chỉ là liveness: không gọi Gemini, không đọc kho.

> Kho cục bộ tích hồ sơ rác sau mỗi lượt thử. Muốn về 3 hồ sơ mồi: dừng app, xoá
> `data/partners.json` và `data/counter.json`, chạy lại.

---

## 9B. Deploy lên Google Cloud Run

Runbook đầy đủ ở [`DEPLOY.md`](DEPLOY.md). Tóm tắt những điều không được quên:

- **`PartnerCard__Extractor=gemini`** — `appsettings.json` ship `fake`. Quên dòng này thì app vẫn chạy,
  `/health` vẫn xanh, thẻ mẫu "đọc" hoàn hảo — bằng đáp án dựng sẵn.
- **`--max-instances 1`** — Blazor Server giữ trạng thái trong bộ nhớ một tiến trình, kho nằm trên đĩa
  riêng từng instance.
- **Kiểm `Test-Path deploy/publish/appsettings.Development.json` phải in `False`** trước khi build image.
- Instance mới (deploy, đổi cấu hình, ngủ ~15 phút) = **kho về 3 hồ sơ mồi**.
- **Không ghi URL service, số project hay khoá API vào README, DEPLOY hay bất kỳ đâu trong repo.** Trang
  không có đăng nhập — ai có URL cũng tiêu được hạn mức Gemini.

---

## 9C. Quan sát cục bộ bằng Aspire Dashboard

```powershell
docker run --rm -d --name aspire -p 18888:18888 -p 4317:18889 -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true mcr.microsoft.com/dotnet/aspire-dashboard:latest

$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
dotnet run --project src/PartnerCard.Web --urls http://0.0.0.0:5080
```

Mở `http://localhost:18888`. **Không có biến → không đăng ký exporter**, app chạy y như cũ. Muốn thấy
span `gen_ai.generate_content` thì chạy chế độ `gemini`. Dừng: `docker stop aspire`.

Cloud Run cố ý không đặt biến này — telemetry chỉ có từ bản chạy cục bộ. Chi tiết: `DEPLOY.md` mục 8.

---

## 10. Hướng dẫn sử dụng

### Giao diện

| Trang | Đường dẫn | Làm gì |
| --- | --- | --- |
| **Chụp** | `/` | Nút *Chụp hoặc chọn ảnh danh thiếp* — điện thoại mở app camera gốc, máy tính hiện hộp chọn file. Kết quả hiện **ngay trên trang này** thành màn xác nhận |
| **Lịch sử** | `/partners` | Danh sách hồ sơ, ô tìm kiếm, nút **Xuất CSV**; tự làm mới mỗi 2 giây |
| **Hồ sơ** | `/review/{PartnerId}` | Mở lại hồ sơ đã lưu để xem/sửa — cùng `ReviewForm`, nút Lưu vẫn đi qua đường ống |

Màn xác nhận có ảnh gốc bên trái (bấm để mở cỡ gốc), form bên phải; trường dưới ngưỡng tin cậy được tô
vàng. Sau khi lưu, **Chụp thẻ tiếp** mở camera ngay — nó là `<label>` của chính ô chọn file nên không
cần điều hướng hay JS, và iOS Safari không mất user activation.

Mất kết nối (điện thoại mở camera đẩy trình duyệt xuống nền) thì hiện hộp **nối lại tiếng Việt**; ảnh đã
chụp được `capture.js` giữ và gửi khi server nghe lại được.

### Endpoint HTTP

| Method | Path | Mô tả |
| --- | --- | --- |
| GET | `/health` | `{"status":"ok"}` — liveness |
| GET | `/images/{sha256}.jpg` | Ảnh gốc; tên sai dạng hoặc không có đều `404` |
| GET | `/export/partners.csv` | Hồ sơ `confirmed`, tối đa 100, mới nhất trước. BOM UTF-8, số điện thoại dạng `="…"` |
| POST | `/mcp` | MCP Streamable HTTP — 4 tool ở §6.3 |

### Gọi MCP

Dùng một MCP client thật (MCP Inspector, hoặc `McpClient` + `HttpClientTransport`) trỏ vào
`http://localhost:5080/mcp` — không dùng `curl` thô: đó là JSON-RPC cần đúng header `Accept`.

Gửi kèm header **`X-Session-Id`** để các lời gọi của cùng một lượt làm việc nối được với nhau trong
nhật ký audit. Thiếu thì server tự sinh một mã và ghi cảnh báo chẩn đoán — gói MCP 2.2.0 chạy Stateless
nên không có `Mcp-Session-Id`.

Thử nhanh: `search_partners` với `keyword = "tanaka"`, rồi `enrich_partner` → `not_implemented`.

### Quy tắc khi dùng

- **Chỉ dùng danh thiếp mẫu.** Khoá Gemini là gói miễn phí — điều khoản của Google ghi rõ không gửi dữ
  liệu cá nhân lên gói này.
- Thẻ tiếng Nhật tìm bằng chữ Latin (`tanaka`, `tokyo`); gõ không dấu vẫn khớp chữ có dấu.
- Trường không chắc thì **để trống**, đừng đoán cho đủ — đó cũng là luật mô hình phải theo.

---

## 11. Bộ đo độ chính xác

```bash
dotnet run --project tools/eval -- --out EVAL.md
```

| Cờ | |
| --- | --- |
| `--out <path>` | File kết quả. Mặc định `EVAL.md` ở gốc repo. Đường dẫn tường minh tính theo thư mục hiện tại, nên `--out ..\PartnerCard\docs\EVAL.md` chạy đúng khi đứng ở `Code\` |
| `--overwrite` | Ghi đè cả file thay vì nối thêm một khối mới |
| `--extractor fake\|gemini` | Đè cấu hình. Không truyền thì đọc `appsettings.json` |
| `--model <id>` | Đè `PartnerCard:Model` — để so hai model mà chỉ khác đúng một biến |
| `--cards a,b,c` | Chỉ chạy các mã thẻ này — thử bộ đo bằng 3 request thay vì 19 |
| `--delay <giây>` | Nghỉ giữa các lượt gọi. Mặc định 0 ở `fake`; ở `gemini` suy từ trần phút của model — 3.8 Flash 13s · 3.5 Flash Lite 5s |
| `--no-retry` | Không chờ–gọi lại thẻ dính trần phút (mặc định có, chờ 60s, đúng một lần) |
| `--dir <path>` | Đè thư mục ảnh |

Mặc định **nối thêm** một khối mới mỗi lượt chạy, không ghi đè: một thay đổi prompt không kèm số đo
trước–sau là một thay đổi không ai biết tốt hay xấu (SPEC mục 14).

### Cổng bắt buộc: chạy `fake` trước

```bash
dotnet run --project tools/eval -- --extractor fake --overwrite
```

Phải ra đúng **72/72 (100%)**. `FakeExtractor` đọc chính `expected.json`, tức đáp án đi vào rồi đi ra
không đổi — nên con số nào khác 100% là **lỗi của bộ đo**, không phải của mô hình. Chưa đạt thì đừng gọi
Gemini thật: mỗi lượt đo tiêu 19 request của hạn mức ngày.

Ca `B-11` trong `dotnet test` giữ cổng này mãi, không phải kiểm bằng tay từng lần.

### Dữ liệu đo

18 danh thiếp (8 tiếng Anh, 8 tiếng Nhật, 2 song ngữ) và 2 ca âm tính. Nhân vật và công ty **hư cấu
100%**, tên miền đuôi `.example`.

| Đường dẫn | |
| --- | --- |
| `tests/…/TestData/cards/*.png` | Bản render — test đơn vị và `FakeExtractor` |
| `tests/…/TestData/cards/expected.json` | Đáp án, tra theo mã thẻ lấy từ tên file |
| `tests/…/TestData/realcards/*.jpg` | Ảnh chụp lại từ bản in — **đây mới là bộ đo thật** |

`realcards/` có **19 file**: 18 tấm thẻ cộng `ja-01-partial.jpg`. **19 lời gọi, 18 thẻ tính điểm** — tấm
partial vẫn tiêu một request nhưng được chấm riêng ngoài 72 điểm, theo tiêu chí "có bịa ra trường nào
không" (TEST-SPEC mục 12).

### Kết quả đã đo

| Lượt | Model | 4 trường bắt buộc | `ja-01-partial` | Độ trễ |
| --- | --- | --- | --- | --- |
| 14/09 09:17 | `gemini-3.5-flash-lite` | **72/72** | Đạt | p50 2.473 ms · p95 5.334 ms |
| 14/09 09:42 | `gemini-3.5-flash-lite` | **72/72** | Đạt | p50 2.297 ms · p95 3.654 ms |
| 14/09 09:57 | `gemini-3.5-flash-lite` | **72/72** | Đạt | p50 2.158 ms · p95 3.634 ms |

Số đếm thô từng trường, trường phụ và chỗ sai nằm trong [`EVAL.md`](EVAL.md).

### Hạn mức — đọc trước khi lập lịch chạy

Quan sát trên khoá của dự án ngày 11/09/2026:

| Model | RPM | RPD | Một lượt đo 19 thẻ |
| --- | --- | --- | --- |
| `gemini-3.8-flash` | 5 | 20 | **Không đủ cho hai lượt** — 19 sát trần 20 |
| `gemini-3.5-flash-lite` | 15 | 500 | Thoải mái, hơn 25 lượt mỗi ngày |

**RPD reset lúc nửa đêm giờ Thái Bình Dương = 15:00 giờ Việt Nam.** Sáng hôm sau **vẫn là cùng một ngày
hạn mức** — thấy `429` lúc 9 giờ sáng thì đợi tới đầu giờ chiều, không phải đợi "mai".

Vì vậy không chỉnh prompt được trên `3.8-flash` (một lượt mỗi ngày thì không có vế trước–sau để so).
Dùng `--model gemini-3.5-flash-lite` cho vòng lặp, hoặc `--cards` chạy tập con. Chi tiết ở SPEC mục 4.6.

---

## 12. Kiểm thử

Bộ test mặc định chạy **hoàn toàn offline** — `FakeExtractor` thay Gemini, không cần khoá API.
Lượt chạy gần nhất ghi nhận (25/09, trước tag `v1.0`): **310 test**, 309 pass, 1 skip (ca `Live`, opt-in).

```bash
dotnet test                              # phải xanh cả khi đã ngắt mạng
dotnet test --filter Category=Guard      # một nhóm
dotnet test --filter "Category!=Live"    # đúng lệnh CI chạy
```

| Category | File test |
| --- | --- |
| `Guard` | `SchemaGuardTests`, `CardSchemaTests`, `CardJsonTests` |
| `Extract` | `GeminiExtractorTests` (`HttpClient` giả), `FakeExtractorTests`, `PromptsTests` |
| `Normalize` · `Confidence` · `Duplicate` | `NormalizerTests` · `ConfidenceTests` · `DuplicateTests` |
| `Store` | `JsonPartnerStoreTests`, `ImageEndpointTests`, `CsvExportEndpointTests` |
| `Tool` | `ToolRegistrationTests`, `CardToolsTests`, `PartnerToolsTests`, `SessionContextTests` |
| `Eval` | Bộ chấm điểm và cổng `fake` = 72/72 (`EvalGateTests`, ca `B-11`) |
| `Integration` | `CardPipelineTests`, `SmokeTests`, `SecretLoaderTests`, `PartnerSeedTests`, audit, cấu hình |
| `Observability` | `TelemetryTests` |
| `Live` | `GeminiLiveTests` — gọi Gemini thật, tự `Skipped` khi thiếu `GEMINI_API_KEY` |

`Fakes/HostileExtractor` **cố tình trả rác** — mọi trường điền đầy, `isBusinessCard = false`, email
không có `@`, `company` là ` ```json ` — kịch bản tệ nhất một mô hình có thể tạo ra, để chứng minh guard
chặn được. `ThrowingExtractor`/`ThrowingPartnerStore` chứng minh không exception nào thoát khỏi đường ống.

**`Skipped` không bao giờ được tính là PASS.** Bằng chứng về mô hình thật đến từ ca `Live` và bộ đo §11,
không mượn kết quả offline để thay thế.

### CI

`.github/workflows/ci.yml` chạy trên mỗi PR và push vào `develop`/`main`, `windows-latest`, **không dùng
secret nào**:

1. Quét chuỗi giống khoá Google API (`AIza` + 35 ký tự) — chỉ in tên file.
2. `dotnet build -c Release`.
3. `dotnet test -c Release --no-build --filter "Category!=Live"` với `PartnerCard__Extractor=fake`.

---

## 13. Giới hạn đã biết

- **Không đăng nhập** — cả trang lẫn `/mcp`. Deploy công khai mà không đăng nhập là rủi ro đã chấp nhận
  cho bản demo ngắn hạn; giảm thiểu bằng cách không đăng URL công khai và xoá service sau demo.
- **Không lưu trữ bền trên Cloud Run.** Kho nằm ở `/tmp` của instance; instance mới là về 3 hồ sơ mồi.
  Bền thật cần ổ gắn ngoài hoặc database.
- **Một instance duy nhất.** Blazor Server giữ trạng thái trong bộ nhớ; hai instance là mạch vỡ khi nối
  lại và hai kho khác nhau.
- **Điểm tin cậy gần như luôn 1,0** trên bộ mẫu — mô hình ít khi tự hạ điểm, nên tô vàng hiếm bật.
  Ngưỡng 0,9 chưa được hiệu chỉnh trên số đo thật.
- **Chỉ đo tiếng Anh và tiếng Nhật** — 2 trên 4 ngôn ngữ của đề bài. Không có con số cam kết cho Hàn/Trung.
- **Bổ sung thông tin từ website đã cắt.** `enrich_partner` chỉ giữ chỗ trong `tools/list`.
- **Không xoá, không gộp hồ sơ** qua giao diện hay MCP.
- **CSV tối đa 100 hồ sơ** — `IPartnerStore` không có hàm "lấy tất cả". Kho lớn hơn thì file thiếu những
  hồ sơ lâu không cập nhật nhất.
- **Bộ đo gọi tay**, không nằm trong CI (tốn hạn mức ngày). Không có test giao diện tự động.
- **`/health` chỉ là liveness** — không probe Gemini hay kho.
- **Khoá gói miễn phí**: không được đổi sang danh thiếp thật khi vẫn dùng khoá này.

---

## 14. Bản đồ tài liệu

Đặc tả nằm **ngoài repo**, tại `..\PartnerCard\docs\` trên máy phát triển. Mọi tham chiếu "SPEC mục …",
"BACKLOG T-…", "TEST-SPEC mục …" trong mã nguồn và trong file này đều trỏ tới bộ tài liệu đó.

| File | Nội dung |
| --- | --- |
| `BRD.md` | Bối cảnh, vấn đề nghiệp vụ, mục tiêu đo được, ràng buộc pháp lý, phạm vi, rủi ro |
| `PRD.md` | User story, tiêu chí chấp nhận giao diện |
| `SPEC.md` | **Nguồn sự thật kỹ thuật**: đường ống, schema, guard, tool MCP, cấu hình, deploy |
| `TEST-SPEC.md` | Mã ca kiểm thử và ngưỡng nghiệm thu |
| `BACKLOG.md` | Task, điều kiện "Xong khi" |
| `HARNESS.md` | Harness engineering: rule, quan sát, human in the loop — cho app và cho quy trình |
| `PROMPTS.md` | Runbook thao tác Claude Code theo từng task |
| `DEMO.md` | Kịch bản demo, checklist, đường lui |
| `GIT.md` | Nhánh, commit và lộ trình |
| `T14B-LOG.md` | Nhật ký chạy thử 24/09 |

Trong repo:

| File | Nội dung |
| --- | --- |
| [`DEPLOY.md`](DEPLOY.md) | Runbook Cloud Run, quay về bản trước, quan sát cục bộ |
| [`EVAL.md`](EVAL.md) | Kết quả thô mọi lượt đo |
