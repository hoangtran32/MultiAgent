# Hub Code 6.0.0 • Code OSS

Hub Code dùng nền Code OSS qua bản phân phối VSCodium. Đây là editor chạy thật, thay cho cửa sổ editor WinForms trước đây. Hub Chat và màn hình quản lý tài khoản vẫn dùng ứng dụng native.

## Mở và sử dụng

Mở `MultiAgentHub.exe` khi đang ở Làm việc, hoặc chạy:

```powershell
MultiAgentHub.exe --code "E:\YourProject"
```

Lần đầu tải VSCodium Windows x64 khoảng 229 MB từ kho phát hành chính thức. Launcher kiểm tra kích thước và SHA-256 ghim trong source trước khi giải nén vào `%LOCALAPPDATA%\MultiAgentHub\codeoss`. Các lần sau dùng bản đã cài. Bộ EXE Hub vẫn chỉ một file; cần Internet để tải nền editor lần đầu. Giữ giấy phép và thông tin sản phẩm của VSCodium.

Editor có cây file, các tab code, IntelliSense cho ngôn ngữ được hỗ trợ, tìm kiếm toàn project, Git/Source Control, terminal tương tác, debug và trình cài extension của VSCodium/Open VSX. Ngôn ngữ hoặc debugger bổ sung có thể cần extension tương ứng. Cửa sổ đầu chia editor và Agent; bạn có thể kéo, đổi kích thước, theme và vị trí các view bằng chức năng Code OSS.

`Ctrl+Alt+A` mở Agent. Hub Code trên Activity Bar mở Agents & Tasks và lịch sử. Trong editor, chọn code rồi dùng menu chuột phải **Hub Code: Gửi đoạn code đang chọn** để đưa đúng phần đó vào ngữ cảnh lượt tiếp theo.

### Ba cách chạy

- **Làm ngay:** chọn đúng một agent/model, gửi yêu cầu, nhận kết quả rồi kết thúc lượt. Không tự gọi cả nhóm hoặc chia một yêu cầu nhỏ thành nhiều vòng bàn luận. Đây là mặc định của giao diện mới.
- **Kế hoạch → triển khai:** chọn Đầu não và Thợ code trong Phân công. Nhóm review toàn bộ giải pháp trước khi chạy code, rồi thực hiện theo vai đã lưu. Các agent được chọn trong cấu hình thảo luận tiếp tục tham gia review.
- **Review code:** agent chỉ đọc và trả nhận xét, không cấp công cụ ghi project cho lượt này.

Không có cặp agent/model mặc định. Nút **⚙** mở Agent & model dùng cấu hình/tài khoản Hub hiện có. Nút **Dò model** đọc metadata của kết nối; không gọi suy luận. Phân công code được lưu riêng theo project; có thể đổi khi đang chạy và áp dụng tại ranh giới bước. Quyền ghi vẫn phải được cấp cho kết nối thực hiện.

## Điểm dừng và tiến độ

Khung Agent hiển thị trạng thái, người/model đang xử lý, hoạt động công cụ, số task đã review và thời gian thực tế. Không hiển thị ETA giả. Kết quả đang sinh xuất hiện trong chat; khi agent kết thúc thì nút Gửi dùng lại được. Kế hoạch, task và kết quả kiểm chứng mở thành tài liệu editor, Git changes mở Source Control, nhật ký mở Output, Terminal mở shell của project.

Kế hoạch và thực thi không được coi là cùng một việc. Các task xong được đối chiếu với yêu cầu để kết thúc, không mở thêm cải tiến tùy chọn. Ba vòng bàn luận liên tiếp giữ nguyên task/tiêu chí/xác nhận được nhận diện là vòng lặp và dừng ở trạng thái chưa xong với lý do rõ. Đây là bảo vệ khi nhóm không hội tụ, không phải giả hoàn thành. Với kế hoạch đã ổn định mà model quên báo sẵn sàng, Hub chủ động chuyển sang lượt review toàn bộ thay vì hỏi qua lại vô hạn.

Làm ngay giảm số lượt điều phối; thời gian suy luận/công cụ của model vẫn tùy task, kết nối và tài nguyên. Code OSS không làm model phía máy chủ nhanh hơn.

## Hội thoại, tệp và khôi phục

Nút **＋** trong composer chọn nhiều ảnh, PDF, Word `.docx` hoặc tệp văn bản; bỏ từng tệp trước khi gửi. Giới hạn hiện hành: 10 tệp, 10 MB mỗi tệp, tổng 20 MB; khả năng nhận ảnh/PDF phụ thuộc kết nối đã chọn. Không tự chuyển model khi định dạng không được hỗ trợ.

Lịch sử Hub Code lưu theo project tại `%LOCALAPPDATA%\MultiAgentHub\ide\<workspace-hash>`. Các phiên Làm việc gần đây của project trong profile native được nhập khi backend mở; Hub Chat giữ lịch sử riêng. Cửa sổ IDE chỉ nhận một thư mục local để tránh lẫn ngữ cảnh. Mỗi project có backend và checkpoint riêng. Workspace có khóa ghi chung giữa IDE, native và API harness.

Đóng editor/extension sẽ hủy lượt đang chạy và lưu phần đã nhận. Phiên bị ngắt không tự chạy lại khi mở app. Chọn lịch sử, kiểm tra kết quả/thay đổi rồi gửi Tiếp tục. Khi backend mất kết nối, UI giữ phản hồi và báo gián đoạn. Timeout xác nhận không tự gửi lại prompt, tránh chạy hai lần. Để thay EXE Hub, đóng các cửa sổ IDE đang dùng backend đó.

## Ranh giới hiện tại

Nền editor/terminal/debugger là Code OSS; agent dùng harness, CLI và tài khoản Hub đã hỗ trợ. Tính năng và giao diện không được cam kết giống 100% Antigravity. Còn những phần riêng cần triển khai thêm: inline AI completion, comment vào artifact theo dòng, checkpoint/undo theo lượt agent, agent điều khiển và ghi hình browser. Tìm kiếm web/đọc trang của API harness không thay thế browser automation. Debugger/extension của Code OSS không tự tạo các tính năng này.

Backend không cung cấp API riêng của Gemini desktop hay model/quota nội bộ Antigravity. Kết nối hiện có nào hoạt động thì dùng kết nối đó, không chuyển ngầm sang model hoặc tài khoản khác. Mở `MultiAgentHub.exe --native` để dùng Hub Chat hoặc công cụ native dự phòng; `--settings` mở Agent & model.

## Cấu trúc và kiểm tra

- `ide-extension`: extension Code OSS và webview, không phụ thuộc CDN hoặc npm runtime. CSP giới hạn tài nguyên, phản hồi model dựng bằng DOM/text, liên kết chỉ mở khi người dùng bấm.
- `--ide-backend <project>`: tiến trình native riêng, JSON một dòng qua stdin/stdout. Không có cổng mạng mới. DTO trả ra chỉ chứa tên/ID/model/quyền/trạng thái, không trả cấu hình credential, token hoặc prompt nội bộ.
- Shared core giữ JobRunner, TeamEngine, API harness, quota, quyền và lưu trữ. Model metadata chạy riêng để không chặn lệnh Dừng.
- CI chạy fixture C#, kiểm tra protocol Node và smoke test headless backend với profile rỗng. Không gọi model/tài khoản thật. UI editor chưa được kiểm thử tương tác trong bản này.

Nguồn nền editor: [VSCodium](https://github.com/VSCodium/vscodium), bản [1.140.06880](https://github.com/VSCodium/vscodium/releases/tag/1.140.06880). Đối chiếu luồng tham khảo: [Google Antigravity IDE codelab](https://codelabs.developers.google.com/getting-started-agy-ide).
