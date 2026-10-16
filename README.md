# Multi-Agent Hub

Ứng dụng Windows native để dùng một hoặc nhiều AI agent trong cùng project và hội thoại.

**C# · .NET 10 · WinForms · MIT** — giao diện tiếng Việt, không dùng JavaScript, web server hoặc WebView.

## Bắt đầu

Tải `MultiAgentHub-<version>-win-x64.exe` từ mục Releases của repository rồi mở trực tiếp. Bản EXE có sẵn .NET runtime, không cần cài đặt. Nếu dùng bản ZIP, giải nén toàn bộ rồi mở `MultiAgentHub.exe`.

1. Lần đầu app không có agent được cài sẵn. Bấm **Thêm agent của bạn** hoặc **Agent & model** để dò công cụ đã cài và model.
2. Chọn kết quả dò, cấu hình rồi lưu agent. Hoặc chọn công cụ AI, kiểu kết nối và số lượng để thêm thủ công. Kết quả dò không tự tạo/bật agent.
3. Mở **Thiết lập / model** để chọn model, executable/API endpoint, key và quyền. CLI dùng tài khoản đã đăng nhập trên máy; API key lưu trong Windows Credential Manager.
4. Bật **Chạy** cho agent đã cấu hình, chọn project và gửi yêu cầu bằng **Ctrl+Enter**.

Chỉ cần **một agent bất kỳ đã cấu hình** để chạy độc lập. Thêm đồng đội để cùng làm việc; tối đa 32 agent. Không bắt buộc có Codex, Antigravity, hoặc một cặp nhà cung cấp cụ thể. Có thể tạo nhiều agent dùng cùng công cụ với model/vai trò khác nhau. App không tự cài CLI, đăng nhập, gọi model hoặc nâng quyền khi khởi động.

## Kết nối

| Công cụ | Cách sử dụng |
|---|---|
| Claude / Anthropic | Claude Code CLI, Anthropic API hoặc sao chép/dán |
| Google Gemini | Gemini API hoặc sao chép/dán |
| Google Antigravity | CLI `agy`; IDE và app 2.0 qua sao chép/dán |
| Codex | Codex CLI hoặc sao chép/dán |
| API tương thích OpenAI / local AI | Chat Completions endpoint, model và key của người dùng |
| Công cụ khác | Executable/script CLI với đối số cấu hình, hoặc sao chép/dán |

Danh sách trên là các adapter có sẵn để lựa chọn. CLI tùy chỉnh và API tương thích dùng được mà không cần sửa source. API thuần văn bản chưa có công cụ tự đọc/sửa filesystem hoặc chạy terminal. IDE/app thủ công cần dán kết quả về Hub và không tham gia phiên bàn luận/nhóm tự động.

**Dò công cụ & model** tìm Codex/Claude/Antigravity CLI đã cài và các kết nối API/CLI tùy chỉnh đã cấu hình. Model có nguồn rõ ràng: `agy models`, cache Codex, cấu hình Claude hoặc API `/models`. Cache/cấu hình cục bộ không xác nhận model còn được cấp quyền; công cụ chưa cung cấp danh sách thì dùng mặc định CLI hoặc nhập ID. Không gợi ý ID model cố định, không gọi inference khi dò. Bộ chọn kết nối vẫn dùng được trong lúc tải model.

## Cách làm việc

- **Hub Code** dùng project và giữ nguyên quyền CLI đã chọn trong mọi lượt. **Hub Chat** chỉ trao đổi văn bản.
- **Chốt từng ý**: một ý nhỏ → phản biện/sửa đến khi cùng chốt phiên bản → thực hiện/review → ý kế tiếp. [Hướng dẫn](docs/CONSENSUS_USER_GUIDE.md).
- **Song song**: cùng nhận yêu cầu. **Lần lượt / phản biện**: kết quả trước chuyển cho agent sau. **Cùng làm việc**: thực hiện → rà soát → hoàn thiện.
- **Bàn luận qua lại**: các model phát triển, phản biện ý tưởng theo giới hạn lượt/thời gian, có bản tổng hợp cuối.
- **Bàn tròn & thực hiện**: trao đổi ngang hàng, đề xuất task có phụ thuộc/tiêu chí, thực hiện tuần tự và review. PM, Architect, Coder, Tester, Reviewer là chuyên môn, không phải năm tài khoản bắt buộc.
- Thư viện project và hội thoại hỗ trợ tiếp tục, tìm kiếm, ghim, đổi tên, lưu trữ và bản TXT tự cập nhật.
- Nhấp phải project để ghim, đổi tên hiển thị, chuyển/tạo mục, mở Explorer, lưu trữ hội thoại hoặc gỡ khỏi thư viện. Gỡ giữ nguyên thư mục và dữ liệu chat; thêm lại cùng đường dẫn để khôi phục. Chuyển chat đọc file ở nền và giữ cache các chat gần nhất cùng vị trí cuộn.
- Tin nhắn của bạn có bong bóng xanh nhạt bên phải; các AI có khung riêng bên trái, đường phân cách nhẹ và nút Sao chép/Chi tiết. Chỉ tin đang thay đổi được cập nhật khi streaming.
- Hết quota rõ ràng thì bỏ chọn agent đó và tiếp tục với kết nối còn khả dụng; còn một thì chạy đơn. Không tự bật lại hoặc gửi lại khi mở app.

**Toàn quyền** cho phép CLI dùng file/terminal theo yêu cầu. Người dùng chọn quyền phù hợp cho từng kết nối. Antigravity chạy ngầm có thể từ chối lệnh cần phê duyệt khi chưa có allow-rule hoặc toàn quyền; xem hướng dẫn chi tiết. Trong luồng nhóm, mặc định người dùng tự test; kết quả không có bằng chứng kiểm tra được ghi **Chưa kiểm chứng**.

## Tài liệu

- [Hướng dẫn sử dụng đầy đủ](docs/USER_GUIDE.md)
- [Nhóm AI, task và kiểm chứng](docs/TEAM_USER_GUIDE.md)
- [Kiến trúc và giới hạn hiện tại](docs/ARCHITECTURE.md)
- [Đóng góp và phát triển](CONTRIBUTING.md)
- [Thay đổi phiên bản](CHANGELOG.md)
- [Bảo mật và dữ liệu cá nhân](SECURITY.md)

## Build từ source

Cần Windows x64 và .NET 10 SDK. Không có NuGet dependencies bên ngoài.

```powershell
dotnet build MultiAgentHub.slnx -c Release
# Build một EXE có runtime để tải lên GitHub Releases, không test chức năng:
./scripts/build-release.ps1 -SkipTests
# Chỉ biên dịch/đóng gói, không test chức năng, không thay app đang chạy:
./scripts/build-desktop.ps1 -PackageOnly -SkipTests
# Cài bản build vào app-bin và tạo shortcut (đóng Hub trước):
./scripts/build-desktop.ps1 -Zip -CreateShortcut -SkipTests
```

EXE nằm tại `release/MultiAgentHub-<version>-win-x64.exe`, kèm file SHA-256; ZIP nằm tại `release/MultiAgentHub-win-x64.zip`. `Open_App.bat` mở bản cài trong `app-bin`; `build-desktop.bat` build, chạy harness và tạo shortcut. Các build script mặc định chạy harness nếu không có `-SkipTests`. GitHub Actions chạy harness và đóng gói trên Windows; kiểm tra này không thay thế kiểm thử UI/live agent. Bản 5.5.1 hiện chỉ được kiểm tra biên dịch/đóng gói cục bộ, chưa chạy test chức năng/màn hình hoặc đo hiệu năng chuyển chat.

## Dữ liệu và Git

Cấu hình/hội thoại nằm ngoài repo tại `%LOCALAPPDATA%\MultiAgentHub`; API key riêng nằm trong Windows Credential Manager. ZIP chỉ chứa app, tài liệu và giấy phép, không kèm tài khoản hoặc agent của người build. Cấu hình cũ vẫn được giữ khi cập nhật; profile mới bắt đầu trống.

`.gitignore` loại build/cache, cấu hình cá nhân, lịch sử, key và file môi trường. Push source và tài liệu; đưa ZIP lên GitHub Releases thay vì commit binary. Dữ liệu task đang chạy không được thay đổi bởi chế độ `-PackageOnly`.

MIT License. Bản Windows đi kèm giấy phép .NET và thông báo thành phần bên thứ ba trong `licenses/`.
