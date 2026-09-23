// Hộp nối lại tiếng Việt — F-18. Thay hộp mặc định tiếng Anh của Blazor ("Rejoining the server…").
//
// VÌ SAO PHẦN TỬ <dialog> DỰNG Ở ĐÂY CHỨ KHÔNG ĐẶT SẴN TRONG App.razor — đọc trước khi "dọn dẹp":
//
//   blazor.web.js chọn hộp MỘT LẦN, ở lần mất kết nối đầu tiên, rồi nhớ suốt đời trang:
//       const el = document.getElementById('components-reconnect-modal');
//       display = el ? <hộp của ta> : <hộp mặc định tiếng Anh>;
//
//   File này 404 hoặc ném lỗi  →  không có phần tử  →  Blazor dựng hộp mặc định.
//   Xấu, nhưng mất kết nối VẪN ĐƯỢC BÁO.
//
//   Đặt sẵn <dialog> trong markup thì ngược lại: file hỏng vẫn còn cái vỏ, Blazor thấy phần tử nên
//   không dựng hộp mặc định, mà không ai viết chữ vào vỏ đó. Mất kết nối thành ra không báo gì —
//   đúng loại sai âm thầm dự án đang chặn ở nhiều chỗ khác.
//
// Thẻ <script> nạp file này là script CỔ ĐIỂN đặt ngay TRƯỚC blazor.web.js trong <body>, nên nó chạy
// xong trước khi Blazor chạy dòng đầu tiên. Xem chú thích ở App.razor.
(function () {
    'use strict';

    // Ngưỡng hiện chậm. Trên Android, mở app camera gốc đẩy Chrome xuống nền và cắt SignalR
    // (SPEC 11.2); quay lại thì Blazor nối lại trong khoảng 1–2 giây. Ngưỡng này để NUỐT trọn lần
    // nối lại đó: hộp không kịp hiện, người xem không thấy gì chớp sau mỗi lần chụp. Hạ xuống dưới
    // ~2000 là hộp bắt đầu chớp lại; nâng lên cao quá thì đứt thật cũng im lặng quá lâu.
    var REVEAL_DELAY_MS = 2000;

    var DIALOG_ID = 'components-reconnect-modal';   // blazor.web.js tìm đúng id này
    var STATE_EVENT = 'components-reconnect-state-changed';

    // ---- Dựng DOM một lần, ngay lúc script chạy ----------------------------------------------

    var dialog = document.createElement('dialog');
    dialog.id = DIALOG_ID;
    dialog.className = 'reconnect';
    dialog.setAttribute('role', 'alertdialog');
    dialog.setAttribute('aria-labelledby', 'reconnect-text');

    var card = document.createElement('div');
    card.className = 'reconnect-card';

    var spinner = document.createElement('div');
    spinner.className = 'spinner';                  // dùng lại kiểu con quay của app.css
    spinner.setAttribute('aria-hidden', 'true');

    var text = document.createElement('p');
    text.className = 'reconnect-text';
    text.id = 'reconnect-text';
    text.setAttribute('aria-live', 'assertive');

    var actions = document.createElement('div');
    actions.className = 'reconnect-actions';

    // Nút chính đổi chữ và đổi việc theo trạng thái (Thử lại / Tiếp tục), nên chỉ dựng một cái.
    var primary = document.createElement('button');
    primary.type = 'button';
    primary.className = 'button';

    var reload = document.createElement('button');
    reload.type = 'button';
    reload.className = 'button button-secondary';
    reload.textContent = 'Tải lại trang';
    reload.addEventListener('click', function () { location.reload(); });

    actions.appendChild(primary);
    actions.appendChild(reload);
    card.appendChild(spinner);
    card.appendChild(text);
    card.appendChild(actions);
    dialog.appendChild(card);

    // <body> chắc chắn đã có: thẻ <script> nằm trong <body>.
    document.body.appendChild(dialog);

    // ---- Hiện / ẩn ---------------------------------------------------------------------------

    var timer = 0;          // id setTimeout của lần hiện chậm đang chờ, 0 = không có
    var visible = false;

    function cancelTimer() {
        if (timer) {
            clearTimeout(timer);
            timer = 0;
        }
    }

    function reveal() {
        timer = 0;
        visible = true;
        dialog.setAttribute('data-open', '');
        if (!primary.hidden) {
            primary.focus();
        }
    }

    // delayed = true  → chờ REVEAL_DELAY_MS rồi mới hiện (show, retrying)
    // delayed = false → hiện ngay (paused, failed, rejected, resume-failed)
    function open(delayed) {
        if (visible) {
            return;
        }
        if (!delayed) {
            cancelTimer();
            reveal();
            return;
        }
        // KHÔNG đặt lại bộ đếm đang chạy: sự kiện `retrying` bắn dồn dập (10 lượt đầu cách nhau
        // 0 ms), đặt lại mỗi lượt thì mốc 2 giây bị đẩy đi mãi và hộp không bao giờ hiện.
        if (!timer) {
            timer = setTimeout(reveal, REVEAL_DELAY_MS);
        }
    }

    function close() {
        cancelTimer();
        visible = false;
        dialog.removeAttribute('data-open');
    }

    // ---- Nút ---------------------------------------------------------------------------------

    // Chép đúng cách hộp mặc định làm: reconnect() không được thì thử resumeCircuit(), cả hai trả
    // false nghĩa là máy chủ đã bỏ circuit — lúc đó chỉ còn đường tải lại trang.
    function runPrimary(action) {
        primary.disabled = true;
        reload.disabled = true;
        var done = function (ok) {
            primary.disabled = false;
            reload.disabled = false;
            if (!ok) {
                render({ state: 'rejected' });
            }
        };
        try {
            action().then(done, function () { done(false); });
        } catch (e) {
            done(false);
        }
    }

    function retry() {
        runPrimary(function () {
            return Blazor.reconnect().then(function (ok) {
                return ok ? true : Blazor.resumeCircuit();
            });
        });
    }

    function resume() {
        runPrimary(function () { return Blazor.resumeCircuit(); });
    }

    primary.addEventListener('click', function () {
        if (typeof Blazor === 'undefined') {
            location.reload();          // blazor.web.js hỏng: hết đường, tải lại là hết
            return;
        }
        if (primary.dataset.action === 'resume') {
            resume();
        } else {
            retry();
        }
    });

    // ---- Trạng thái --------------------------------------------------------------------------

    function setPrimary(label, action) {
        primary.hidden = false;
        primary.textContent = label;
        primary.dataset.action = action;
    }

    function retryingText(detail) {
        var seconds = detail && detail.secondsToNextAttempt;
        return seconds > 0
            ? 'Đang kết nối lại… thử lại sau ' + seconds + ' giây'
            : 'Đang kết nối lại…';
    }

    function render(detail) {
        var state = detail.state;

        if (state === 'hide') {
            close();
            return;
        }

        // Chữ và nút dựng lại ở MỌI sự kiện, kể cả lúc hộp còn ẩn — để lúc nó lộ ra thì chữ đã đúng.
        spinner.hidden = false;
        primary.hidden = true;
        reload.hidden = false;
        actions.hidden = false;
        primary.disabled = false;
        reload.disabled = false;

        switch (state) {
            case 'show':
            case 'retrying':
                text.textContent = retryingText(detail);
                // Còn đang thử thì chưa mời người dùng làm gì — giấu cả hàng nút, không chỉ từng nút:
                // một <div> rỗng vẫn khiến card cộng thêm một khoảng `gap` dưới chữ, nhìn lệch.
                reload.hidden = true;
                actions.hidden = true;
                open(true);
                return;

            case 'paused':
                spinner.hidden = true;
                text.textContent = 'Phiên đã tạm dừng';
                setPrimary('Tiếp tục', 'resume');
                open(false);
                return;

            case 'failed':
                spinner.hidden = true;
                text.textContent = 'Không kết nối lại được';
                setPrimary('Thử lại', 'retry');
                open(false);
                return;

            case 'resume-failed':
                spinner.hidden = true;
                text.textContent = 'Không tiếp tục được phiên';
                setPrimary('Tiếp tục', 'resume');
                open(false);
                return;

            case 'rejected':
                // Máy chủ đã bỏ circuit — hay gặp nhất là vừa deploy bản mới. "Thử lại" chỉ bị từ
                // chối tiếp, nên không có nút đó. Chữ nói VIỆC CẦN LÀM, không nói khái niệm "phiên".
                spinner.hidden = true;
                text.textContent = 'Ứng dụng vừa được cập nhật — tải lại trang để tiếp tục';
                open(false);
                return;

            default:
                return;
        }
    }

    dialog.addEventListener(STATE_EVENT, function (e) { render(e.detail); });
})();
