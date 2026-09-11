# PartnerCard — Code

Chụp danh thiếp → trích xuất bằng Gemini → hồ sơ đối tác chuẩn hoá.
.NET 10 · Blazor Server · MCP · dữ liệu JSON.

**Thư mục này chỉ chứa những gì cần để chạy.** Đặc tả, backlog, kịch bản demo và
tài liệu thiết kế nằm ngoài repo, tại `..\PartnerCard\docs\` trên máy phát triển.

## Chạy

```bash
dotnet run --project src/PartnerCard.Web --urls http://0.0.0.0:5080
```

Mở `http://localhost:5080`, hoặc `http://<IP-LAN>:5080` từ điện thoại (cùng Wi-Fi,
cổng 5080 đã mở trên tường lửa).

## Hai chế độ

| Cấu hình | Dùng khi |
|---|---|
| `PartnerCard:Extractor = fake` | Mặc định khi phát triển và chạy test. Không cần mạng, không cần khoá API |
| `PartnerCard:Extractor = gemini` | Khi đo thật hoặc demo. Cần `GEMINI_API_KEY` |

Khoá API đặt trong `src/PartnerCard.Web/appsettings.Development.json` — file này
nằm trong `.gitignore`, **không bao giờ commit**.

```json
{ "GEMINI_API_KEY": "<khoá>" }
```

## Test

```bash
dotnet test                          # phải xanh cả khi đã ngắt mạng
dotnet test --filter Category=Guard
```

## Bộ đo

```bash
dotnet run --project tools/eval -- --out EVAL.md
```

| Cờ | |
|---|---|
| `--out <path>` | File kết quả. Mặc định `EVAL.md` ở gốc repo. Đường dẫn tường minh tính theo thư mục hiện tại, nên `--out ..\PartnerCard\docs\EVAL.md` chạy đúng khi đứng ở `Code\` |
| `--overwrite` | Ghi đè cả file thay vì nối thêm một khối mới |
| `--extractor fake\|gemini` | Đè cấu hình. Không truyền thì đọc `appsettings.json` |
| `--model <id>` | Đè `PartnerCard:Model` — để so hai model mà chỉ khác đúng một biến |
| `--cards a,b,c` | Chỉ chạy các mã thẻ này — thử bộ đo bằng 3 request thay vì 19 |
| `--delay <giây>` | Nghỉ giữa các lượt gọi. Mặc định 0 ở `fake`; ở `gemini` suy từ trần phút của model — 3.8 Flash 13s · 3.5 Flash Lite 5s |
| `--no-retry` | Không chờ–gọi lại thẻ dính trần phút (mặc định có, chờ 60s, đúng một lần) |
| `--dir <path>` | Đè thư mục ảnh |

Mặc định **nối thêm** một khối mới mỗi lượt chạy, không ghi đè: một thay đổi prompt không kèm
số đo trước–sau là một thay đổi không ai biết tốt hay xấu (SPEC mục 14).

### Cổng bắt buộc: chạy `fake` trước

```bash
dotnet run --project tools/eval -- --extractor fake --overwrite
```

Phải ra đúng **72/72 (100%)**. `FakeExtractor` đọc chính `expected.json`, tức đáp án đi vào rồi
đi ra không đổi — nên con số nào khác 100% là **lỗi của bộ đo**, không phải của mô hình. Chưa đạt
thì đừng gọi Gemini thật: mỗi lượt đo tiêu 19 request của hạn mức ngày.

Ca `B-11` trong `dotnet test` giữ cổng này mãi, không phải kiểm bằng tay từng lần.

### Hạn mức — đọc trước khi lập lịch chạy

Quan sát trên khoá của dự án ngày 11/09/2026:

| Model | RPM | RPD | Một lượt đo 19 thẻ |
|---|---|---|---|
| `gemini-3.8-flash` | 5 | 20 | **Không đủ cho hai lượt** — 19 sát trần 20 |
| `gemini-3.5-flash-lite` | 15 | 500 | Thoải mái, hơn 25 lượt mỗi ngày |

**RPD reset lúc nửa đêm giờ Thái Bình Dương = 15:00 giờ Việt Nam.** Sáng hôm sau **vẫn là cùng một
ngày hạn mức** — thấy `429` lúc 9 giờ sáng thì đợi tới đầu giờ chiều, không phải đợi "mai".

Vì vậy T-09 không chỉnh prompt được trên `3.8-flash` (một lượt mỗi ngày thì không có vế trước–sau
để so). Dùng `--model gemini-3.5-flash-lite` cho vòng lặp, hoặc `--cards` chạy tập con. Chi tiết ở
SPEC mục 4.6.

### Dữ liệu đo

18 danh thiếp (8 tiếng Anh, 8 tiếng Nhật, 2 song ngữ) và 2 ca âm tính. Nhân vật và công ty
**hư cấu 100%**, tên miền đuôi `.example`.

| Đường dẫn | |
|---|---|
| `tests/…/TestData/cards/*.png` | Bản render — test đơn vị và `FakeExtractor` |
| `tests/…/TestData/cards/expected.json` | Đáp án, tra theo mã thẻ lấy từ tên file |
| `tests/…/TestData/realcards/*.jpg` | Ảnh chụp lại từ bản in — **đây mới là bộ đo thật** |

`realcards/` có **19 file**: 18 tấm thẻ cộng `ja-01-partial.jpg`. **19 lời gọi, 18 thẻ tính điểm**
— tấm partial vẫn tiêu một request nhưng được chấm riêng ngoài 72 điểm, theo tiêu chí "có bịa ra
trường nào không" (TEST-SPEC mục 12).
