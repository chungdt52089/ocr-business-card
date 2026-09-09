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

Dữ liệu đo ở `tests/PartnerCard.Tests/TestData/cards/`: 18 danh thiếp (8 tiếng Anh,
8 tiếng Nhật, 2 song ngữ) và 2 ca âm tính. Nhân vật và công ty **hư cấu 100%**,
tên miền đuôi `.example`.

- `*.png` — bản render, dùng cho test đơn vị
- `*.jpg` — ảnh chụp lại từ bản in, **đây mới là bộ đo thật**
- `expected.json` — đáp án, tra theo mã thẻ lấy từ tên file
