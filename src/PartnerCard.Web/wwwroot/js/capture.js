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
