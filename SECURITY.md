# Bảo mật và dữ liệu

Hub dùng CLI/API do người dùng cấu hình. CLI có quyền file/terminal theo lựa chọn của người dùng; source và lệnh trong project có thể được thực thi. Từ 5.14.0, harness API có công cụ do Hub thực thi: Chat không truy cập workspace, Code đọc/tìm file và chỉ ghi theo quyền; full + bật lệnh cho phép executable/đối số, không có sandbox OS. Xem docs/AGENT_HARNESS.md.

API key riêng được lưu trong Windows Credential Manager. Biến môi trường hỗ trợ theo hướng dẫn sử dụng. Cấu hình JSON và bản xuất hội thoại không chứa trường API key riêng, nhưng prompt/log/model có thể lặp lại dữ liệu nhạy cảm đã xuất hiện trong project hoặc yêu cầu.

Trước khi đăng issue, chia sẻ transcript hoặc đưa source lên Git:

- Xóa key, token, email/tài khoản và nội dung riêng khỏi ví dụ tái hiện.
- Không đính kèm toàn bộ thư mục LocalAppData, Credential Manager, CLI auth cache hoặc `.env`.
- Nếu credential đã lộ, thu hồi/thay credential tại nhà cung cấp; xóa file khỏi phiên bản hiện tại không xóa nó khỏi lịch sử Git.

Với lỗ hổng làm lộ credential hoặc thực thi ngoài quyền, dùng kênh báo cáo riêng của repository nếu có. Nếu chưa có, yêu cầu maintainer một kênh riêng trước khi gửi chi tiết nhạy cảm; không đăng credential hoặc khai thác vào issue công khai.

Phạm vi hiện tại: một tác vụ trong một instance; luồng nhóm/harness ghi khóa một writer trong workspace nhưng không kiểm soát editor/công cụ ngoài. CLI tùy chỉnh tự quản lý sandbox. API chỉ nhận công cụ theo không gian/quyền/bước công việc thực tế; dữ liệu model không cấp quyền. Không có cập nhật tự động hoặc telemetry do Hub gửi.

Harness web chỉ HTTPS công khai, DNS kiểm tra trên socket và redirect, không cookie/credential website hoặc proxy hệ thống. Bộ nhớ/receipt/artifact nằm riêng từng hội thoại trong profile; có thể chứa dữ liệu người dùng và kết quả lệnh. Receipt pending sau gián đoạn không được tự replay; không hoàn tác side effect đã có. Hub không tự ghi HTTP authorization/token vào nhật ký; nội dung file/lệnh vẫn có thể chứa thông tin nhạy cảm do người dùng/model đưa vào.


Bridge chatbot tùy chọn chỉ dùng tab người dùng đã liên kết, qua activeTab + Native Messaging. Observer đọc bản sao response POST cùng origin chứa đúng prompt Hub trong lượt đang arm; chỉ chuyển answer/model/trạng thái đã parse. Luồng DOM không đọc cookie/localStorage/Authorization hoặc đưa phiên đăng nhập về app; không replay endpoint nội bộ, không tự dò tab/tài khoản khác. MAIN-world/DOM response là dữ liệu website không tin cậy, không được phép điều khiển native host ngoài giao thức answer của lượt hiện tại. Không hỗ trợ mọi giao thức website; xem docs/BROWSER_CHAT.md về giới hạn và cách ngắt/gỡ.


Gateway web tùy chọn 5.11.0 dùng broker riêng trong document được liên kết chủ động. Broker học một request generation văn bản đầu tiên (body và header cần thiết như Authorization/CSRF nếu website cung cấp) và giữ trong closure bộ nhớ của document, không chuyển credential sang native hoặc storage. Không đọc cookie jar/localStorage, mật khẩu hoặc tab khác. Chỉ gọi generation endpoint cùng origin đã nhận diện; không phải proxy URL tùy ý. Không tái sử dụng challenge/proof chống bot. Ngắt liên kết xóa template; tải lại document cũng mất template. Native nhận descriptor model và câu trả lời, registry chỉ có alias/binding/origin.

HTTP gateway bind riêng 127.0.0.1, phải bật thủ công, key riêng trong Windows Credential Manager, kiểm tra Host/remote IP và từ chối Origin/CORS. Không mở LAN, không lộ key/token trong HTTP errors hoặc log server. Text API không có tools/terminal/filesystem. Không tự tạo quota mới hoặc chuyển model/quota sang kết nối CLI. Adapter web thử nghiệm có thể cần cập nhật khi website đổi handshake; chưa kiểm chứng live tài khoản thật.
