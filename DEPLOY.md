# DEPLOY — Google Cloud Run

Runbook đưa PartnerCard lên Cloud Run. **Chung chạy mọi lệnh** — PowerShell, đứng ở `Code\`.
Lý do chi tiết của từng lựa chọn: SPEC mục 13 và 19 (ngoài repo, `..\PartnerCard\docs\`).

> **Repo này công khai.** Không ghi vào đây, vào `README.md`, hay vào khung chat Teams: URL của
> service, số project, khoá API. Trang không có đăng nhập — ai có URL cũng tiêu được hạn mức Gemini.

## 1. Điều kiện

| Cần | Kiểm |
|---|---|
| gcloud CLI đã đăng nhập | `gcloud auth list` có một tài khoản đánh dấu `*` |
| Mặc định: project `scanie`, vùng `asia-southeast1` | `gcloud config list` in `project = scanie` và, dưới `[run]`, `region = asia-southeast1` |
| .NET SDK theo `global.json` (10.0.4xx) | `dotnet --version`, chạy trong `Code\` |
| Docker | **Không cần.** Image dựng bằng Cloud Build |

Chưa có mặc định thì:

```powershell
gcloud config set project scanie
gcloud config set run/region asia-southeast1
```

## 2. Thiết lập một lần

Đã làm 21/09. Ghi lại để dựng lại được từ đầu.

**Bật API** — Compute để có service account mặc định:

```powershell
gcloud services enable run.googleapis.com artifactregistry.googleapis.com secretmanager.googleapis.com cloudbuild.googleapis.com compute.googleapis.com
```

**Repository chứa image:**

```powershell
gcloud artifacts repositories create scanie --repository-format=docker --location=asia-southeast1
```

**Secret `gemini-api-key` — tạo bằng Console, KHÔNG BAO GIỜ bằng dòng lệnh.** Console → Security →
Secret Manager → *Create secret* → Name `gemini-api-key` → dán khoá vào ô *Secret value* → *Create*.
Khoá gõ vào một lệnh sẽ nằm lại trong lịch sử PowerShell. Đổi khoá = thêm version mới cho secret;
instance khởi động sau đó tự lấy `:latest`.

**Quyền cho service account mặc định** — Cloud Run và Cloud Build cùng dùng nó:

```powershell
$pn = gcloud projects describe scanie --format="value(projectNumber)"
$sa = "serviceAccount:$pn-compute@developer.gserviceaccount.com"

gcloud secrets add-iam-policy-binding gemini-api-key --member=$sa --role=roles/secretmanager.secretAccessor

foreach ($role in "roles/artifactregistry.writer", "roles/logging.logWriter", "roles/storage.objectViewer") {
    gcloud projects add-iam-policy-binding scanie --member=$sa --role=$role --condition=None
}
```

| Quyền | Để làm gì |
|---|---|
| `secretmanager.secretAccessor` — **chỉ trên secret này** | Cloud Run đọc khoá lúc khởi động |
| `artifactregistry.writer` | Cloud Build đẩy image vào repository |
| `logging.logWriter` | Cloud Build ghi log build |
| `storage.objectViewer` | Cloud Build đọc gói mã nguồn mà `gcloud builds submit` vừa tải lên |

## 3. Mỗi lần đẩy bản

Trước khi bắt đầu: **cây sạch** — `git status --short` không in gì. Tag image là mã commit; build từ
file chưa commit là nói dối về thứ đang chạy.

```powershell
$tag = git rev-parse --short HEAD        # từ 25/09: $tag = "v1.0"
dotnet publish src/PartnerCard.Web -c Release -r linux-x64 --self-contained false -o deploy/publish
Test-Path deploy/publish/appsettings.Development.json     # PHẢI in False
gcloud builds submit deploy --tag "asia-southeast1-docker.pkg.dev/scanie/scanie/scanie:$tag"
gcloud run services update scanie --region asia-southeast1 --image "asia-southeast1-docker.pkg.dev/scanie/scanie/scanie:$tag"
```

- **`Test-Path` in `True` → DỪNG, không chạy dòng 4.** File đó chứa khoá API, và dòng 4 sẽ đóng nó
  vào image. `deploy/publish/` không tự dọn file của lần publish trước, nên xoá cả thư mục
  (`Remove-Item -Recurse -Force deploy/publish`), kiểm csproj còn dòng
  `CopyToPublishDirectory="Never"`, rồi chạy lại từ dòng 2. Lỡ đẩy rồi thì coi khoá đã lộ: tạo khoá
  mới, thêm version mới cho secret, xoá image đó.
- **`$tag` chỉ sống trong cửa sổ PowerShell đã tạo nó.** Mở cửa sổ mới rồi chạy thẳng dòng 5 thì
  `--image …/scanie:` rỗng → *"expected a container image path"*. Chạy lại dòng 1 trước.
- **Không đẩy đè một tag đã có.** Commit đó đã có image thì dùng lại, không build lần hai. Xem các tag
  đã có: `gcloud artifacts docker tags list asia-southeast1-docker.pkg.dev/scanie/scanie/scanie`
- Mỗi lần `update` = revision mới = instance mới = **kho về 3 hồ sơ mồi**.

## 4. Lần đầu tạo service

Chạy dòng 1–4 ở mục 3 để có image, rồi thay dòng 5 bằng:

```powershell
gcloud run deploy scanie `
  --image "asia-southeast1-docker.pkg.dev/scanie/scanie/scanie:$tag" `
  --region asia-southeast1 `
  --allow-unauthenticated `
  --max-instances 1 `
  --timeout 3600 `
  --cpu 1 --memory 1Gi `
  --session-affinity `
  --set-env-vars "PartnerCard__Extractor=gemini,PartnerCard__DataDirectory=/tmp/scanie/data,PartnerCard__LogsDirectory=/tmp/scanie/logs,PartnerCard__ExtractTimeoutSeconds=45,DOTNET_SYSTEM_NET_DISABLEIPV6=1" `
  --set-secrets "GEMINI_API_KEY=gemini-api-key:latest"
```

| Cờ | Vì sao |
|---|---|
| **`PartnerCard__Extractor=gemini`** | **Dòng dễ quên nhất và nguy hiểm nhất.** `appsettings.json` ship `fake`. Quên dòng này thì app vẫn chạy, `/health` vẫn xanh, thẻ mẫu "đọc" hoàn hảo — bằng đáp án dựng sẵn |
| **`--max-instances 1`** | **Bắt buộc.** Blazor Server giữ trạng thái trong bộ nhớ của một tiến trình, kho dữ liệu nằm trên đĩa riêng từng instance. Hai instance = mạch vỡ khi nối lại và hai kho khác nhau |
| `--allow-unauthenticated` | Điện thoại mở URL không cần đăng nhập Google |
| `--timeout 3600` | Trần sống của một kết nối WebSocket. Mặc định 300 s thì cứ 5 phút trang lại nối lại |
| `--session-affinity` | Bảo hiểm cho lúc nối lại |
| `--cpu 1 --memory 1Gi` | `/tmp` nằm trong RAM và tính vào 1 GiB — dư cho ảnh vài trăm KB mỗi tấm |
| `PartnerCard__DataDirectory`, `PartnerCard__LogsDirectory` | `/tmp/scanie/…`. Image chạy bằng user không phải root; đường tương đối mặc định giải thành `/data`, `/logs` — không ghi được |
| `PartnerCard__ExtractTimeoutSeconds=45` | Gemini có tối chậm 15–20 s; hạn mặc định 20 s đã hết giờ hai lần |
| `DOTNET_SYSTEM_NET_DISABLEIPV6=1` | Cloud Run không có IPv6 ra ngoài. Chưa chứng minh là cần, vô hại |
| `--set-secrets GEMINI_API_KEY=gemini-api-key:latest` | Khoá lấy từ Secret Manager — không nằm trong cấu hình service, không nằm trong lệnh |

Các lần sau chỉ đổi image (dòng 5 ở mục 3); biến môi trường và secret giữ nguyên.

## 5. Kiểm sau deploy

Lấy URL — **chỉ để lưu dấu trang trên điện thoại**, không dán vào đâu khác:

```powershell
gcloud run services describe scanie --region asia-southeast1 --format="value(status.url)"
```

1. **`/health` qua mạng di động** — điện thoại **tắt Wi-Fi**, mở `<URL>/health` → `{"status":"ok"}`.
   Lần đầu có thể chờ vài giây: khởi động nguội.
2. **Chụp 1 thẻ mẫu** — dòng nhỏ dưới kết quả phải bắt đầu bằng **`gemini-3.5-flash-lite`**. Thấy
   `fake` (thường kèm câu *"không phải danh thiếp"*, vì `fake` tra đáp án theo tên file mà điện thoại
   đặt tên mọi ảnh là `image.jpg`) là thiếu biến Extractor:

   ```powershell
   gcloud run services update scanie --region asia-southeast1 --update-env-vars PartnerCard__Extractor=gemini
   ```

3. **Dòng nạp mồi trong log:**

   ```powershell
   gcloud run services logs read scanie --region asia-southeast1 --limit 50
   ```

   Phải có: *"Kho rỗng — đã nạp 3 hồ sơ mồi. · thư mục dữ liệu: /tmp/scanie/data · thư mục nhật ký:
   /tmp/scanie/logs"*. PowerShell hiện dấu tiếng Việt thành `?` — chữ vẫn đúng. Bản có dấu:
   Console → Cloud Run → `scanie` → *Logs*.

## 6. Quay về bản trước

```powershell
gcloud run revisions list --service scanie --region asia-southeast1
gcloud run services update-traffic scanie --region asia-southeast1 --to-revisions=<revision>=100
```

- `<revision>` lấy ở cột REVISION của lệnh đầu. Revision cũ mang nguyên image và biến môi trường của
  nó.
- **Đã ghim lưu lượng thì lần `update` kế tiếp không tự nhận lưu lượng** — revision mới chạy nhưng
  0%. Xong sự cố thì trả về:

  ```powershell
  gcloud run services update-traffic scanie --region asia-southeast1 --to-latest
  ```

- Đổi revision cũng là instance mới → kho về mồi.

## 7. Hôm demo và sau demo

**Đóng băng.** Từ 25/09 chỉ đẩy image build từ tag git `v1.0` (bản vá `v1.0.x` nếu buộc phải có):
đứng ở commit của tag — `git describe --tags --exact-match` in `v1.0` — rồi `$tag = "v1.0"` ở dòng 1
mục 3. **28–29/09 không đẩy image nào.**

**Làm ấm 30 phút trước giờ họp:** mở `/health`, chụp 2–3 thẻ. Vừa đánh thức instance, vừa đo độ trễ
Gemini của đúng hôm đó.

- Instance ngủ sau ~15 phút không ai dùng; lần mở kế tiếp là instance mới, **kho về 3 hồ sơ mồi**.
  Kịch bản cần hồ sơ đã lưu thì làm ấm và lưu ngay trước giờ họp, hoặc giữ một instance thức từ tối
  28/09 (tốn giờ chạy, trừ vào credit):

  ```powershell
  gcloud run services update scanie --region asia-southeast1 --min-instances 1   # tối 28/09
  gcloud run services update scanie --region asia-southeast1 --min-instances 0   # sau demo
  ```

  Đây là đổi cấu hình, không đổi image — nhưng nó tạo revision mới, nên làm **trước** khi làm ấm.
- URL dài: **lưu dấu trang trên điện thoại từ trước**, đừng gõ tay trong buổi demo.

**Khi mentor không cần xem nữa** — xoá service; URL chết theo và hết tốn tiền chạy:

```powershell
gcloud run services delete scanie --region asia-southeast1
```

Image vẫn nằm trong Artifact Registry. Muốn dọn luôn: `gcloud artifacts repositories delete scanie --location asia-southeast1`.
