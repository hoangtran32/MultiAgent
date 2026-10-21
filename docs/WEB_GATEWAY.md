# Gateway web · 5.11.0 (thử nghiệm)

Hub có gateway và danh sách tài khoản riêng. Để giữ **đúng model/quota trên web**, đăng nhập/xác minh thực hiện trong trình duyệt; Hub quản lý alias, liên kết phiên và định tuyến model. Không chuyển sang Codex OAuth/Gemini Code Assist hoặc API key tính phí. Tab cần giữ mở. Đây chưa phải cơ chế đăng nhập độc lập không cần trình duyệt.

## Thiết lập

1. Trong **Agent & model → Gateway & tài khoản web**, bấm **Cài/cập nhật extension & mở thư mục**. Load unpacked hoặc Reload tại `chrome://extensions` / `edge://extensions`. Sau cập nhật cần tải lại tab để thay script cũ; đợi mọi lượt đang chạy hoàn tất trước.
2. Mở website, đăng nhập bằng tài khoản mong muốn, chọn model rồi mở **chat mới trống**. Bấm extension, xác nhận và **Kết nối tab hiện tại**. Chỉ tab được chọn được liên kết, không tự quét các tài khoản/tab khác.
3. Gửi một tin **văn bản đầu tiên** trên website. Bước này dùng quota web như gửi bình thường. Extension nhận diện request API/model trong phiên đó, không đọc toàn bộ tài khoản. Nhãn model có thể đọc từ UI; nếu website không lộ tên thì đặt nhãn đúng trong extension và nhận diện lại từ chat mới. Nhãn không phải chứng thực backend.
4. Trong dialog gateway, chọn phiên, đặt tên riêng và **Liên kết / cập nhật tài khoản**. Chọn model nhận diện được → **Dùng model trong Hub**. Lặp lại với chatbot/tài khoản khác nếu cần. Không có cặp agent mặc định.
5. Chọn **Trò chuyện → Trò chuyện nhóm**, chọn các chatbot rồi đưa chủ đề. Không cần project. Hub gửi ngữ cảnh hội thoại qua API trong tab đã liên kết; bot trả lời người dùng và đồng đội. Các lượt gateway không gõ vào composer hoặc đọc câu trả lời bằng DOM.

Mỗi model/template lấy từ request đầu tiên trong một document; để nhận diện model khác mở tab/chat mới và chọn model đó. Không học template từ continuation hoặc upload. Đổi/tải lại document làm mất template trong bộ nhớ; liên kết và nhận diện lại, sau đó cập nhật tài khoản cũ trong Hub. Lịch sử Hub được giữ.

## API cục bộ

Bấm **Mở gateway cục bộ** khi muốn dùng Hub từ client khác. Không cần bật HTTP server để các agent gateway trong Hub trao đổi với nhau. Endpoint mặc định `http://127.0.0.1:20129/v1`, cổng có thể đổi khi server đang đóng. Chỉ bind IPv4 loopback, không chia sẻ LAN, không tự mở lúc khởi động.

- `GET /v1/models`: các model đang nhận diện, đúng origin/binding của tài khoản đã thêm. Dùng nguyên `id` trả về (dạng accountId:web-descriptor).
- `POST /v1/chat/completions`: `model`, `messages` văn bản và `stream` boolean. Nhiều messages được ghép thành transcript có nhãn vai trò, không phải native conversation schema của từng provider. Hỗ trợ SSE `chat.completion.chunk`, hoàn tất bằng `[DONE]`.
- Header `Authorization: Bearer <local-key>`, lấy bằng **Chép API key cục bộ**. Key lưu trong Windows Credential Manager, không phải token web. Không ghi vào repo. Request từ browser có Origin bị từ chối; không hỗ trợ CORS.
- Chưa hỗ trợ tools, function calling, JSON mode, multimodal hoặc điều khiển temperature/token budget. Những tham số này bị từ chối. Quota vẫn do website quyết định; không có quota “không giới hạn” do gateway tạo ra.
- Mỗi phiên chỉ chạy một lượt tại một thời điểm. Request bị lỗi không tự gửi lại, đổi tài khoản/model hoặc replay sang tab khác.

## Tương thích và xử lý lỗi

Adapter hiện có cho **Gemini web RPC**, **ChatGPT web conversation** và **cùng origin Chat Completions** có request/response schema nhận diện được. Danh sách này là các giao thức, không phải danh sách model/agent cố định. Không phải website HTTPS nào cũng tự trở thành API.

ChatGPT có nhiều biến thể handshake, proof và xác minh tương tác. Hub không phát lại token chống bot cũ hoặc giải challenge. Khi gặp biến thể chưa hỗ trợ, lượt API báo rõ cần website/xác minh hoặc adapter chưa hỗ trợ; việc “đã nhận diện request” chưa có nghĩa đã kiểm chứng gọi API thành công. Bạn có thể chủ động chọn kết nối tab DOM cũ riêng nếu muốn; gateway không tự fallback.

HTTP/RPC lỗi, thiếu frame kết thúc hoặc luồng bị cắt không được coi là hoàn thành. Văn bản nhận được vẫn được giữ trong Hub. Adapter web thử nghiệm, có thể cần cập nhật khi website đổi giao thức. Bản này chỉ build/cú pháp cục bộ và fixture offline CI; chưa kiểm thử đăng nhập/inference/màn hình trên tài khoản thật.

## Dữ liệu

Profile `gateway-accounts.json` chỉ lưu alias, bindingId và origin, không lưu cookie/token/model template. Extension giữ template và header cần cho API trong closure của document đã chọn, không ghi storage hoặc gửi native. Hub nhận descriptor và văn bản; mật khẩu/cookie jar không được đọc. Gỡ tài khoản trong Hub chỉ gỡ liên kết, không đăng xuất website hoặc xóa lịch sử.

Gateway theo mô hình endpoint/tài khoản tương tự [9router](https://github.com/decolua/9router), nhưng không cài/chạy 9router và không dùng kết nối OAuth CLI để thay quota web. Không sao chép mã nguồn 9router.
