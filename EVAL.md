# EVAL — Kết quả đo PartnerCard

Sinh bằng `dotnet run --project tools/eval`. Mỗi lượt chạy là một khối `##` ở dưới, khối mới
nối vào cuối file — để so được số trước và số sau mỗi lần sửa prompt (SPEC mục 14).

Báo cáo bằng **số đếm thô kèm phần trăm**. 18 thẻ × 4 trường bắt buộc = 72 điểm dữ liệu.

## Lượt đo · 2026-09-11 07:16 +07:00

> ⚠ **Lượt này chạy ở chế độ `fake`, không dùng để nghiệm thu.** `FakeExtractor` đọc
> chính `expected.json`, nên con số dưới đây chỉ chứng minh **bộ đo tự nó đúng** — bất cứ
> kết quả nào khác 100% là lỗi của bộ đo, không phải của mô hình.

```
model         : fake (FakeExtractor — đọc expected.json)
promptVersion : -
thinkingLevel : -
ảnh           : tests/PartnerCard.Tests/TestData/realcards (ảnh chụp thật)
nghỉ giữa lượt: 0s
```

`thinkingLevel` nằm trong `generationConfig` chứ không trong prompt, nên `promptVersion`
không ghi lại được nó — vì vậy nó có một dòng riêng ở đây (SPEC mục 14).

**19/19 file gọi được · 18/18 thẻ tính điểm**

**19 file ≠ 18 thẻ tính điểm.** Phần chênh là `ja-01-partial` (ca chống bịa).
Phần chênh vẫn tiêu một request như mọi tấm khác, nhưng được chấm theo tiêu chí khác hẳn và
**nằm ngoài 72 điểm** (TEST-SPEC mục 12) — xem mục riêng ở dưới. **Mọi mẫu số điểm số trong
khối này đếm theo thẻ tính điểm, không theo file.**

### Bốn trường bắt buộc — 18 thẻ tính điểm × 4 = 72 điểm

| Nhóm | Thẻ tính điểm | fullName | company | phones | emails | Tổng |
|---|---|---|---|---|---|---|
| Anh | 8 | 8/8 | 8/8 | 8/8 | 8/8 | 32/32 (100,0%) |
| Nhật + song ngữ | 10 | 10/10 | 10/10 | 10/10 | 10/10 | 40/40 (100,0%) |
| **Tổng** | 18 | 18/18 | 18/18 | 18/18 | 18/18 | **72/72 (100,0%)** |

### Trường phụ — báo cáo, không gate

| Nhóm | Thẻ tính điểm | jobTitle | website | address | detectedLanguage | searchAlias |
|---|---|---|---|---|---|---|
| Anh | 8 | 8/8 | 8/8 | 8/8 | 8/8 | 8/8 |
| Nhật + song ngữ | 10 | 10/10 | 10/10 | 10/10 | 10/10 | 10/10 |
| **Tổng** | 18 | 18/18 | 18/18 | 18/18 | 18/18 | 18/18 |

### Sai ở đâu

Không trường nào sai.

### ja-01-partial — ca chống bịa (ngoài 72 điểm)

`emails`: rỗng ✔ · `website`: rỗng ✔ → **ĐẠT**

Tấm này bị cắt mất khối liên hệ. Mô hình thấy tên công ty nhưng **không hề thấy tên miền**,
nên hai trường trên có giá trị nghĩa là nó đã suy ra thứ không nhìn thấy (TEST-SPEC mục 12).

### fieldConfidence

```
min (trường có giá trị)      : 1,00
trung vị (trường có giá trị) : 1,00
số mục bằng đúng 1.0         : 138/138
```

Chỉ tính trên **trường có giá trị**: trường rỗng luôn được 0,0 theo SPEC mục 4.5, gộp vào thì
`min` luôn bằng 0 và cả hai con số mất sạch ý nghĩa. `min` bằng 1,0 nghĩa là mô hình chấm 1.0
cho mọi trường — khi ấy ngưỡng 0,9 của D-4 không bao giờ tô vàng gì, và điều 6 của prompt
(SPEC mục 4.5) chưa ăn.

Ở chế độ `fake` thì ba con số trên **không nói gì về mô hình**: `FakeExtractor` tự sinh 1,0
cho mọi trường có giá trị (SPEC mục 4.1), nên `min` bằng 1,0 ở đây là đương nhiên.

### Độ trễ và token

```
p50 0 ms · p95 30 ms
tokensIn 0 · tokensOut 0 · tổng 0
```

TEST-SPEC mục 11 ghi ngưỡng P-01 là p95 ≤ 8.000 ms. **Đó là ngưỡng đặt trước khi có số đo thật**
nên ở đây chỉ in số cạnh nó, không phán ĐẠT/TRƯỢT — mục 11 vốn nói rõ "chỉ đo, không gate".

Bộ đo gửi ảnh **nguyên 2560px**, còn ứng dụng thu nhỏ về 1600px trong trình duyệt trước khi gửi
(SPEC mục 11.3). Dự án không có thư viện ảnh nào (SPEC mục 1) nên bộ đo không thu nhỏ được, và
vì vậy `tokensIn` ở đây **cao hơn** lúc chạy thật.


## Lượt đo · 2026-09-11 07:24 +07:00

```
model         : gemini-3.8-flash
promptVersion : v1.2 — vá lỗ đặc tả (thêm luật thẻ song ngữ), KHÔNG phải vòng tinh chỉnh của T-09
thinkingLevel : low
ảnh           : tests/PartnerCard.Tests/TestData/realcards (ảnh chụp thật)
nghỉ giữa lượt: 3s
```

`thinkingLevel` nằm trong `generationConfig` chứ không trong prompt, nên `promptVersion`
không ghi lại được nó — vì vậy nó có một dòng riêng ở đây (SPEC mục 14).

Ghi chú cạnh `promptVersion` phân biệt **hai loại thay đổi prompt**, và đó là mục đích duy
nhất của nó. *Vá lỗ đặc tả* là prompt trước thiếu hẳn một luật nên mô hình làm đúng thứ nó
được bảo; điểm lên là vì cái lỗ được vá. *Vòng tinh chỉnh* của T-09 mới là thử một cách diễn
đạt khác để xem điểm có lên không. Trộn hai loại vào một bảng số thì T-09 sẽ tưởng mình vừa
tìm ra một cách diễn đạt hiệu quả, rồi đi tối ưu tiếp theo hướng đó.

**1/2 file gọi được · 1/2 thẻ tính điểm · 1 hỏng: bi-01 extract_unavailable**

### Bốn trường bắt buộc — 2 thẻ tính điểm × 4 = 8 điểm

| Nhóm | Thẻ tính điểm | fullName | company | phones | emails | Tổng |
|---|---|---|---|---|---|---|
| Nhật + song ngữ | 2 | 1/2 | 1/2 | 1/2 | 1/2 | 4/8 (50,0%) |
| **Tổng** | 2 | 1/2 | 1/2 | 1/2 | 1/2 | **4/8 (50,0%)** |

```
tính cả thẻ hỏng   : 4/8 (50,0%)   ← con số nghiệm thu · mẫu số 2 thẻ tính điểm
chỉ thẻ có kết quả : 4/4 (100,0%)   ← mẫu số 1/2 thẻ tính điểm có kết quả
```

**Con số nghiệm thu là dòng trên**: 1 thẻ không có kết quả được tính
**0 điểm cho cả bốn trường** và mẫu số giữ nguyên. Bỏ chúng ra khỏi mẫu số thì tỷ lệ đẹp lên
đúng vì lượt đo hỏng nhiều hơn — con số tự thưởng cho chính thất bại của nó.

Dòng dưới có mặt để chặn cái sai **ngược lại**: đọc mỗi con số nghiệm thu rồi kết luận mô hình
đọc kém, trong khi thật ra mấy tấm kia không có kết quả vì mạng rớt chứ không phải vì đọc sai.
Xem dòng đếm ở đầu khối để biết chúng hỏng vì mã lỗi nào.

### Trường phụ — báo cáo, không gate

| Nhóm | Thẻ tính điểm | jobTitle | website | address | detectedLanguage | searchAlias |
|---|---|---|---|---|---|---|
| Nhật + song ngữ | 2 | 1/2 | 1/2 | 1/2 | 1/2 | 1/2 |
| **Tổng** | 2 | 1/2 | 1/2 | 1/2 | 1/2 | 1/2 |

### Sai ở đâu

| Thẻ | Trường | Kỳ vọng | Nhận được | Chỉ lỗi dấu? |
|---|---|---|---|---|
| bi-01 | *(cả thẻ)* | — | `extract_unavailable` | |

### fieldConfidence

```
min (trường có giá trị)      : 1,00
trung vị (trường có giá trị) : 1,00
số mục bằng đúng 1.0         : 8/8
```

Chỉ tính trên **trường có giá trị**: trường rỗng luôn được 0,0 theo SPEC mục 4.5, gộp vào thì
`min` luôn bằng 0 và cả hai con số mất sạch ý nghĩa. `min` bằng 1,0 nghĩa là mô hình chấm 1.0
cho mọi trường — khi ấy ngưỡng 0,9 của D-4 không bao giờ tô vàng gì, và điều 6 của prompt
(SPEC mục 4.5) chưa ăn.

### Độ trễ và token

```
p50 4.370 ms · p95 4.370 ms
tokensIn 2.951 · tokensOut 279 · tổng 3.230
```

TEST-SPEC mục 11 ghi ngưỡng P-01 là p95 ≤ 8.000 ms. **Đó là ngưỡng đặt trước khi có số đo thật**
nên ở đây chỉ in số cạnh nó, không phán ĐẠT/TRƯỢT — mục 11 vốn nói rõ "chỉ đo, không gate".

Bộ đo gửi ảnh **nguyên 2560px**, còn ứng dụng thu nhỏ về 1600px trong trình duyệt trước khi gửi
(SPEC mục 11.3). Dự án không có thư viện ảnh nào (SPEC mục 1) nên bộ đo không thu nhỏ được, và
vì vậy `tokensIn` ở đây **cao hơn** lúc chạy thật.

