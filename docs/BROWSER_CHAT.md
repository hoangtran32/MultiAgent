# Gemini + ChatGPT bằng tài khoản trình duyệt

Bridge thử nghiệm từ 5.7.0 dùng Gemini web (`gemini.google.com`) và ChatGPT web (`chatgpt.com`) trong Chrome/Edge. Không gọi API key, Antigravity hay Codex CLI; tài khoản trên website quyết định model/quota. Không điều khiển trực tiếp native desktop app.

## Kết nối lần đầu

1. Trong Hub, mở **Agent & model → Chatbot từ trình duyệt… → Chuẩn bị bridge & mở thư mục…**. Thao tác xuất extension đi kèm EXE, đăng ký native host ở HKCU cho tài khoản Windows hiện tại, không cần admin. Chưa gửi prompt/đăng nhập thay bạn.
2. Mở `chrome://extensions` hoặc `edge://extensions`, bật **Developer mode → Load unpacked**, chọn thư mục `extension` vừa mở. Tiện ích tên **Multi-Agent Hub · Browser Chat**, ID `koeckebjfmkgjanknbifdcgahkbbalpb`. Chrome yêu cầu người dùng chủ động cài; EXE không cài ngầm.
3. Mở hai **chat mới, trống** trên Gemini và ChatGPT; đăng nhập trực tiếp nếu cần. Chọn Argon và High trong Gemini nếu tài khoản có, chọn model ChatGPT mong muốn trên website. Bấm extension ở từng tab: tiện ích tự quét khung chat, nút gửi và nhãn model đang hiển thị. Kiểm tra/sửa nhãn nếu cần, xác nhận rồi **Kết nối**. Chỉ báo nối sau khi bắt tay với Hub.
4. Chọn hai tab trong hộp **Chatbot từ trình duyệt** rồi **Dùng các chatbot đã chọn**. Hub chuyển sang **Hub Chat → Bàn luận qua lại**, giữ lựa chọn agent Hub Code riêng. Nhập chủ đề rồi gửi; không tự chạy chỉ vì đã nối tab.

Không tạo sẵn cặp agent cho profile mới; danh sách từ tab bạn nối. Không xóa thư mục extension sau khi Load unpacked. Đổi chỗ EXE thì chạy lại **Chuẩn bị bridge** để cập nhật đường dẫn native host.

## Tự quét từ 5.7.1

Mỗi lần mở popup, extension đọc các ô nhập đang hiển thị, bỏ ô tìm kiếm/ô khóa và ưu tiên vùng soạn chat. Nút gửi được dò lại sau khi có nội dung, vì website có thể ẩn nút này khi ô nhập trống. Quét không nhập thử, bấm gửi hay thay model. Nhãn model lấy từ điều khiển trên giao diện khi xác định được duy nhất; không suy đoán từ lịch sử chat và không xác minh backend.

Nếu chưa nhận diện chắc, bấm **Tự quét lại** để bỏ CSS ô nhập/nút gửi cũ. Nếu vẫn có nhiều ô hợp lệ, bấm **Chọn ô nhập trên trang**, rồi bấm đúng ô soạn chat được viền xanh; mở lại popup để kết nối. Esc hủy chọn, bộ chọn tự đóng sau 30 giây. Lựa chọn trực tiếp chỉ có hiệu lực trên trang hiện tại; không áp dụng nhầm sang tab khác. CSS nội dung phản hồi nâng cao vẫn được giữ.

Sau khi cập nhật EXE, bấm **Chuẩn bị bridge & mở thư mục…** để xuất extension mới nếu thư mục chưa được cập nhật. Trong `chrome://extensions` hoặc `edge://extensions`, bấm **Reload** của tiện ích, sau đó **tải lại trang chatbot** và mở chat mới để nối lại. Script đã tiêm ở phiên bản cũ không được thay chỉ bằng việc mở lại popup. Không tải lại tab đang tạo phản hồi; đợi hoàn tất trước.

## Trao đổi, dừng và tiếp tục

Model luân phiên phát triển, phản biện, đề xuất cách kiểm chứng và giữ sổ ý tưởng. Link HTTP(S) trong phần trả lời được chuyển tiếp; Hub không bảo đảm nguồn của model đúng. Mặc định liên tục, không giới hạn lượt/thời gian/timeout của Hub. Chỉnh tại **Giới hạn bàn luận…**, 0 = không giới hạn; quota của website vẫn áp dụng.

Gửi bổ sung trong Hub để đưa vào lượt tiếp theo. **⋯ → Kết thúc & tổng hợp** chờ lượt đang chạy rồi tạo tổng hợp chủ đề. **Dừng** dừng vòng Hub ngay; nếu website vẫn tạo phản hồi, dừng trực tiếp tại tab. Prompt đã gửi có thể vẫn được dịch vụ xử lý. Giữ Hub, các tab mở và máy không sleep; không gõ thêm/đổi chat trong tab đang nối.

Thư viện JSON/TXT/Markdown giữ các lượt như trước. Mỗi binding tab thuộc một chủ đề Hub: **Đoạn chat mới** trong Hub cần chat web mới và nối lại. Tiếp tục chủ đề cũ từ lịch sử Hub. Nếu tab cũ mất nối, kết nối chat web trống mới rồi mở lịch sử Hub; ngữ cảnh được gửi từ bản ghi. Không xóa chat web cũ.

## Giới hạn cần biết

Đây là bridge DOM thử nghiệm, **không phải API chính thức của Gemini/ChatGPT**. Bản này chỉ build, kiểm tra cú pháp và harness giả lập; **chưa kiểm thử live thao tác gửi/nhận trên website**. Nhãn model do bạn xác nhận, không xác minh backend hay tự đổi model. Đổi model cần nối/xác nhận lại.

Lượt chỉ thành công khi nhận diện tin người dùng mới, câu trả lời mới, trạng thái đang tạo đã xuất hiện rồi tín hiệu hoàn tất UI. Chữ đứng yên không đủ. Không nhận diện rõ thì giữ partial, báo lỗi cần xử lý và không gửi lượt kế tiếp vào tab đó; engine có thể tiếp tục bằng model còn khả dụng và ghi rõ thiếu đồng đội.

Website đổi UI/ngôn ngữ có thể cần **Tự quét lại**, chọn ô nhập hoặc chỉnh **Bộ chọn CSS nâng cao** trong extension, nhất là cấu trúc nội dung phản hồi/tín hiệu hoàn tất. Không vượt đăng nhập/CAPTCHA hoặc hạn mức. Extension không đọc cookie, access token hay localStorage của website. Chỉ quét ở tab đang mở popup và gửi tại tab người dùng chủ động nối; không ghi đè bản nháp hoặc tự replay khi lỗi.

## Gỡ bridge

Gỡ extension tại trang quản lý tiện ích. Có thể xóa đúng subkey `com.multiagenthub.browser_chat` dưới `HKCU\Software\Google\Chrome\NativeMessagingHosts` và `HKCU\Software\Microsoft\Edge\NativeMessagingHosts`; sau đó xóa `%LOCALAPPDATA%\MultiAgentHub\browser-bridge`. Không xóa thư mục dữ liệu Hub để tránh mất lịch sử.

## Tài liệu thiết kế

- [Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging) và [activeTab](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab).
- [Gemini 4 Argon](https://ai.google.dev/gemini-api/docs/models/gemini-4-argon): tham khảo tên model, không gọi API trong bridge này.
- [ChatGPT, Work và Codex](https://learn.chatgpt.com/docs/use-chatgpt): phân biệt chat web và chế độ làm việc trong app.
