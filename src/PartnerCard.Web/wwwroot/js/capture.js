// Thu nhỏ ảnh NGAY TRONG TRÌNH DUYỆT rồi mới gửi lên — SPEC mục 11.3.
//
// Không có getUserMedia ở đây, và đó là cả điểm của Đường A (SPEC mục 11.1): trên
// http://<IP-LAN> thì navigator.mediaDevices là undefined, mà lỗi ấy KHÔNG xuất hiện khi
// thử bằng localhost — nó chỉ vỡ đúng lúc cầm điện thoại lên. Ở đây chỉ có một thẻ
// <input type="file" capture="environment">, tức là một file input thường, không cần HTTPS.
//
// Trang gọi hàm này TRƯỚC khi chuỗi base64 đi qua SignalR, nên 12MP của điện thoại không bao
// giờ chạm tới server. Đó cũng là lý do trang không dùng <InputFile> của Blazor: thẻ đó stream
// nguyên file gốc lên server, tức làm đúng cái việc mà bước thu nhỏ sinh ra để tránh.

const MAX_EDGE = 1600;
const QUALITY = 0.8;

// ---- Bắt ảnh phía trình duyệt, giữ tới khi gửi được ----------------------------------------
//
// Trang KHÔNG dùng @onchange của Blazor. Trên Android, mở app camera gốc đẩy Chrome xuống nền và
// SignalR đứt; quay lại thì 'change' bắn ra lúc circuit còn đang nối lại. Blazor không xếp hàng sự
// kiện giao diện khi mất kết nối, nên sự kiện ấy mất âm thầm và trang đứng im ở trạng thái đầu.
// Ở đây sự kiện được bắt ngay trong trình duyệt, ảnh thu nhỏ ngay, và kết quả nằm trong module này
// cho tới khi server nhận được.
//
// Vì sao dò bằng Ping trước rồi mới gửi ảnh: blazor.web.js (.NET 10) gọi connection.send() mà bỏ
// rơi promise của nó, nên lúc mất kết nối invokeMethodAsync KHÔNG reject — nó treo mãi. Mỗi lượt
// thử vì vậy phải có hạn giờ riêng. Hạn 1 giây cho một lời gọi rỗng là đủ; hạn 1 giây cho 500 KB
// base64 trên 4G yếu thì sẽ gửi đôi, gửi ba trước khi lượt đầu kịp trả lời.

const RETRY_EVERY_MS = 1000;
const PING_TIMEOUT_MS = 1000;
const SEND_TIMEOUT_MS = 15000;
const GIVE_UP_AFTER_MS = 30000;

// Vỏ rỗng do Blazor render một lần, nội dung do module này dựng (Capture.razor).
const STATUS_ID = 'captureStatus';

// Trạng thái ở cấp module chứ không trong closure của init. .NET 10 nối lại theo thứ tự
// reconnect() → resumeCircuit(); nhánh resume dựng component MỚI với DotNetObjectReference mới
// và gọi init lần nữa. Ảnh đang chờ phải sống qua đó rồi đi bằng ref mới — trình duyệt giữ nguyên
// module ES qua cả hai nhánh. (Tab bị Android huỷ hẳn rồi tải lại thì module nạp lại và ảnh chờ
// mất theo: người dùng thấy trang trống và chụp lại.)
let current = null;       // { input, dotNetRef, onChange } của lần init gần nhất
let pending = null;       // { payload, since } — ảnh đã thu nhỏ, server chưa nhận
let seq = 0;              // mã ảnh tăng dần; server dựa vào nó để không trích xuất một ảnh hai lần
let delivering = false;   // chỉ một vòng gửi chạy tại một thời điểm

export function init(input, dotNetRef) {
  if (current) {
    current.input.removeEventListener('change', current.onChange);
  }

  const onChange = () => onFileChosen(input);
  input.addEventListener('change', onChange);
  current = { input, dotNetRef, onChange };

  if (pending) {
    deliver();
  }
}

async function onFileChosen(input) {
  const id = ++seq;
  pending = null;   // ảnh mới nhất thắng; ảnh cũ chưa gửi được thì thôi
  showProgress('Đang thu nhỏ ảnh…');

  const captured = await shrink(input, MAX_EDGE, QUALITY);
  if (id !== seq) {
    return;   // người dùng đã chọn ảnh khác trong lúc thu nhỏ
  }

  if (!captured) {
    clearStatus();
    return;
  }

  pending = { payload: { ...captured, id }, since: Date.now() };
  deliver();
}

async function deliver() {
  if (delivering) {
    return;
  }

  delivering = true;
  try {
    while (pending) {
      const item = pending;

      // Trần 30 giây chặn việc BẮT ĐẦU lượt mới. Một lần gửi đang dở thì được chờ hết hạn của nó,
      // để trang không báo "tải lại" trong lúc server đã nhận ảnh và sắp hiện kết quả.
      if (Date.now() - item.since > GIVE_UP_AFTER_MS) {
        pending = null;
        showError('Mất kết nối tới máy chủ, ảnh chưa gửi được. Tải lại trang rồi chụp lại.');
        return;
      }

      // Đọc ref ở MỖI lượt: sau resumeCircuit, init đã thay nó bằng ref của component mới.
      const started = Date.now();
      const ref = current?.dotNetRef;
      const alive = ref ? await callWithin(PING_TIMEOUT_MS, () => ref.invokeMethodAsync('Ping')) : null;

      if (pending !== item) {
        continue;   // có ảnh mới thay chỗ trong lúc chờ
      }

      if (alive !== true) {
        showProgress(`Đang kết nối lại… (${Math.round((Date.now() - item.since) / 1000)} giây)`);
        await sleep(RETRY_EVERY_MS - (Date.now() - started));
        continue;
      }

      showProgress('Đang gửi ảnh…');
      const reply = await callWithin(
        SEND_TIMEOUT_MS, () => ref.invokeMethodAsync('OnShrunkAsync', item.payload));

      if (pending !== item) {
        continue;
      }

      // Ba chuỗi này khớp Capture.razor (Delivery). "duplicate" nghĩa là server đã nhận ảnh này từ
      // một lượt trước mà phản hồi bị mất — cũng là đã tới nơi.
      if (reply === 'accepted' || reply === 'duplicate') {
        pending = null;
        clearStatus();   // từ đây Blazor hiện "Đang đọc danh thiếp…"
        return;
      }

      if (reply === 'busy') {
        pending = null;
        showError('Đang đọc ảnh trước — đợi xong rồi chụp lại.');
        return;
      }

      // Không trả lời trong hạn: có thể đứt giữa chừng. Quay lại dò; nếu server thật ra đã nhận
      // thì lượt sau nó trả "duplicate" chứ không trích xuất lần hai.
    }
  } finally {
    delivering = false;
  }
}

// Lời gọi .NET có hạn giờ. Hết hạn, reject, hay ném ngay đều thành null — với vòng gửi, ba thứ đó
// cùng một nghĩa: lượt này chưa tới được server.
function callWithin(ms, invoke) {
  let call;
  try {
    call = Promise.resolve(invoke()).catch(() => null);
  } catch {
    call = Promise.resolve(null);
  }

  return Promise.race([call, sleep(ms).then(() => null)]);
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, Math.max(0, ms)));
}

// ---- Dòng trạng thái ------------------------------------------------------------------------
//
// Tìm vỏ bằng id ở MỖI lần hiện, không giữ tham chiếu: sau resumeCircuit Blazor render lại và vỏ
// là một phần tử mới. Dựng bằng textContent, và dùng lại class sẵn có trong app.css.

function showProgress(text) {
  const host = document.getElementById(STATUS_ID);
  if (!host) {
    return;
  }

  // Chỉ thay chữ nếu khung chờ đã có, để vòng quay không khởi động lại mỗi giây.
  const existing = host.firstElementChild;
  if (existing?.classList.contains('waiting')) {
    existing.lastElementChild.textContent = text;
    return;
  }

  const box = document.createElement('div');
  box.className = 'waiting';

  const spinner = document.createElement('span');
  spinner.className = 'spinner';
  spinner.setAttribute('aria-hidden', 'true');

  const label = document.createElement('span');
  label.textContent = text;

  box.append(spinner, label);
  host.replaceChildren(box);
}

function showError(text) {
  const host = document.getElementById(STATUS_ID);
  if (!host) {
    return;
  }

  const box = document.createElement('div');
  box.className = 'result result-error';
  box.setAttribute('role', 'alert');

  const line = document.createElement('p');
  line.textContent = text;

  box.append(line);
  host.replaceChildren(box);
}

function clearStatus() {
  document.getElementById(STATUS_ID)?.replaceChildren();
}

export async function shrink(input, maxEdge, quality) {
  const file = input?.files?.[0];
  if (!file) {
    return null;
  }

  try {
    const source = await toDrawable(file);
    const edge = maxEdge || MAX_EDGE;
    const scale = Math.min(1, edge / Math.max(source.width, source.height));

    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(source.width * scale));
    canvas.height = Math.max(1, Math.round(source.height * scale));
    canvas.getContext('2d').drawImage(source.image, 0, 0, canvas.width, canvas.height);
    source.close();

    // split(',')[1] bỏ tiền tố "data:image/jpeg;base64," — đường ống nhận base64 thuần.
    const base64 = canvas.toDataURL('image/jpeg', quality || QUALITY).split(',')[1];

    return {
      ok: true,
      error: '',
      base64,
      fileName: file.name || '',
      width: canvas.width,
      height: canvas.height
    };
  } catch (e) {
    // Trả lỗi thành giá trị chứ không ném: một exception qua ranh giới JS interop tới server
    // dưới dạng chuỗi lộn xộn, còn ở đây trang hiện được một câu đọc được.
    return {
      ok: false,
      error: String((e && e.message) || e),
      base64: '',
      fileName: file.name || '',
      width: 0,
      height: 0
    };
  } finally {
    // Xoá lựa chọn để chụp lại ĐÚNG tấm vừa rồi vẫn bắn onchange. Không có dòng này thì lần
    // chụp thứ hai của cùng một file im lặng không làm gì.
    input.value = '';
  }
}

async function toDrawable(file) {
  if (typeof createImageBitmap === 'function') {
    try {
      // imageOrientation từ ảnh: điện thoại ghi hướng vào EXIF chứ không xoay pixel, và một
      // tấm thẻ nằm ngang gửi cho mô hình là tự làm khó nó.
      const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
      return {
        image: bitmap,
        width: bitmap.width,
        height: bitmap.height,
        close: () => bitmap.close?.()
      };
    } catch {
      // Lui xuống <img>. Safari đời cũ trên iOS là chỗ createImageBitmap hay vắng mặt hoặc
      // từ chối định dạng — mà đó lại đúng là thiết bị quyết định buổi demo.
    }
  }

  const img = await viaImageElement(file);
  return {
    image: img,
    width: img.naturalWidth,
    height: img.naturalHeight,
    close: () => {}
  };
}

function viaImageElement(file) {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file);
    const img = new Image();

    img.onload = () => {
      URL.revokeObjectURL(url);
      resolve(img);
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Trình duyệt không mở được ảnh này.'));
    };

    img.src = url;
  });
}
