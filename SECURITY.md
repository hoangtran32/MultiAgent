# Bảo mật và dữ liệu

Hub dùng CLI/API do người dùng cấu hình. CLI có quyền file/terminal theo lựa chọn của người dùng; source và lệnh trong project có thể được thực thi. API thuần văn bản chỉ nhận prompt/ngữ cảnh/đính kèm do Hub gửi, chưa có tool executor truy cập máy.

API key riêng được lưu trong Windows Credential Manager. Biến môi trường hỗ trợ theo hướng dẫn sử dụng. Cấu hình JSON và bản xuất hội thoại không chứa trường API key riêng, nhưng prompt/log/model có thể lặp lại dữ liệu nhạy cảm đã xuất hiện trong project hoặc yêu cầu.

Trước khi đăng issue, chia sẻ transcript hoặc đưa source lên Git:

- Xóa key, token, email/tài khoản và nội dung riêng khỏi ví dụ tái hiện.
- Không đính kèm toàn bộ thư mục LocalAppData, Credential Manager, CLI auth cache hoặc `.env`.
- Nếu credential đã lộ, thu hồi/thay credential tại nhà cung cấp; xóa file khỏi phiên bản hiện tại không xóa nó khỏi lịch sử Git.

Với lỗ hổng làm lộ credential hoặc thực thi ngoài quyền, dùng kênh báo cáo riêng của repository nếu có. Nếu chưa có, yêu cầu maintainer một kênh riêng trước khi gửi chi tiết nhạy cảm; không đăng credential hoặc khai thác vào issue công khai.

Phạm vi hiện tại: một tác vụ trong một instance; luồng nhóm khóa một writer trong workspace nhưng không kiểm soát editor/công cụ ngoài. CLI tùy chỉnh tự quản lý sandbox. `full` không tạo công cụ filesystem cho API. Không có cập nhật tự động hoặc telemetry do Hub gửi.


Bridge chatbot tùy chọn chỉ dùng tab người dùng đã liên kết, qua activeTab + Native Messaging. Observer đọc bản sao response POST cùng origin chứa đúng prompt Hub trong lượt đang arm; chỉ chuyển answer/model/trạng thái đã parse. Không đọc cookie/localStorage/Authorization hoặc đưa phiên đăng nhập về app; không replay endpoint nội bộ, không tự dò tab/tài khoản khác. MAIN-world/DOM response là dữ liệu website không tin cậy, không được phép điều khiển native host ngoài giao thức answer của lượt hiện tại. Không hỗ trợ mọi giao thức website; xem docs/BROWSER_CHAT.md về giới hạn và cách ngắt/gỡ.
